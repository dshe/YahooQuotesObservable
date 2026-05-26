using ProtoBuf;
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
namespace YahooQuotesObservable;

/// Under sustained load, intermediate ticks may be dropped
/// to preserve bounded memory usage and low latency.
/// Market data delivery is intentionally lossy.

internal sealed class YahooStreamer : IAsyncDisposable
{
    private static readonly Uri Uri = new("wss://streamer.finance.yahoo.com/?version=2");
    private readonly ILogger<YahooStreamer> logger;
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource cts = new();
    private readonly Channel<PricingData> channel = Channel.CreateBounded<PricingData>(new BoundedChannelOptions(4096)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    internal ChannelReader<PricingData> Messages => channel.Reader;
    private int disposed;
    private Task? receiveTask;

    internal YahooStreamer(ILoggerFactory loggerFactory) => logger = loggerFactory.CreateLogger<YahooStreamer>();

    internal async Task ConnectAsync()
    {
        logger.LogInformation("Connecting");
        await socket.ConnectAsync(Uri, cts.Token).ConfigureAwait(false);
        receiveTask = ReceiveLoop();
    }

    private async Task ReceiveLoop()
    {
        CancellationToken ct = cts.Token;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using MemoryStream ms = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        logger.LogInformation("WebSocket closed by server.");
                        channel.Writer.TryComplete();
                        return;
                    }
#pragma warning disable CA1849 // Call async methods when in an async method
                    ms.Write(buffer, 0, result.Count);
#pragma warning restore CA1849
                } while (!result.EndOfMessage);

                ReadOnlyMemory<byte> jsonBytes = ms.ToArray();
                byte[]? rented = null;

                try
                {
                    using JsonDocument json = JsonDocument.Parse(jsonBytes); //.Net 10 does not accept ReadOnlySpan<byte>
                    if (!json.RootElement.TryGetProperty("message", out JsonElement message))
                        continue;
                    string? base64 = message.GetString();
                    if (string.IsNullOrWhiteSpace(base64))
                        continue;

                    rented = ArrayPool<byte>.Shared.Rent(base64.Length);

                    if (!Convert.TryFromBase64String(base64, rented, out int written))
                        continue;
                    using MemoryStream protobufStream = new(rented, 0, written);
                    PricingData? pricing = Serializer.Deserialize<PricingData>(protobufStream);
                    if (pricing is null || string.IsNullOrWhiteSpace(pricing.Symbol))
                        continue;
                    if (!channel.Writer.TryWrite(pricing))
                        logger.LogDebug("Dropped pricing tick for {Symbol}", pricing.Symbol);
                }
                catch (ProtoException)
                {
                    logger.LogWarning("Failed to deserialize protobuf message.");
                    continue;
                }
                catch (JsonException)
                {
                    logger.LogWarning("Failed to parse JSON message.");
                    continue;
                }
                catch (FormatException)
                {
                    logger.LogWarning("Failed to format message.");
                    continue;
                }
                finally
                {
                    if (rented != null)
                        ArrayPool<byte>.Shared.Return(rented);
                }
            }
            channel.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Receive loop canceled.");
            channel.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in receive loop.");
            channel.Writer.TryComplete(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal Task SubscribeAsync(IEnumerable<string> symbols) => SendAsync(CreateMessage("subscribe", symbols));
    internal Task UnsubscribeAsync(IEnumerable<string> symbols) => SendAsync(CreateMessage("unsubscribe", symbols));

    private async Task SendAsync(byte[] payload)
    {
        try
        {
            await sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
            logger.LogDebug("Sent payload of {Length} bytes.", payload.Length);
        }
        catch (OperationCanceledException) 
        {
            logger.LogInformation("Send operation canceled.");
        }
        catch (WebSocketException ex) 
        {
            logger.LogWarning(ex, "WebSocket send failed.");
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static byte[] CreateMessage(string action, IEnumerable<string> symbols)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, IEnumerable<string>>
            {
                [action] = symbols
            }));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        logger.LogInformation("Disposing YahooStreamer...");
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None).ConfigureAwait(false);
                    logger.LogInformation("WebSocket closed gracefully.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "WebSocket close failed, aborting.");
                    socket.Abort();
                }
            }
            else
            {
                socket.Abort();
            }
            await cts.CancelAsync().ConfigureAwait(false);
            if (receiveTask != null)
            {
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Receive loop ended with exception during disposal.");
                }
            }
            channel.Writer.TryComplete();
        }
        finally
        {
            socket.Dispose();
            sendLock.Dispose();
            cts.Dispose();
            logger.LogInformation("YahooStreamer disposed.");
        }
    }
}