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
    private static readonly Uri _uri = new("wss://streamer.finance.yahoo.com/?version=2");
    private readonly ILogger<YahooStreamer> _logger;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<PricingData> _channel = Channel.CreateBounded<PricingData>(new BoundedChannelOptions(4096)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    internal ChannelReader<PricingData> Messages => _channel.Reader;
    private int _disposed;
    private Task? _receiveTask;

    internal YahooStreamer(ILoggerFactory loggerFactory) => _logger = loggerFactory.CreateLogger<YahooStreamer>();

    internal async Task ConnectAsync()
    {
        _logger.LogInformation("Connecting");
        await _socket.ConnectAsync(_uri, _cts.Token).ConfigureAwait(false);
        _receiveTask = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        CancellationToken ct = _cts.Token;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using MemoryStream ms = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket closed by server.");
                        _channel.Writer.TryComplete();
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
                    if (!_channel.Writer.TryWrite(pricing))
                        _logger.LogDebug("Dropped pricing tick for {Symbol}", pricing.Symbol);
                }
                catch (ProtoException)
                {
                    _logger.LogWarning("Failed to deserialize protobuf message.");
                    continue;
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Failed to parse JSON message.");
                    continue;
                }
                catch (FormatException)
                {
                    _logger.LogWarning("Failed to format message.");
                    continue;
                }
                finally
                {
                    if (rented != null)
                        ArrayPool<byte>.Shared.Return(rented);
                }
            }
            _channel.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Receive loop canceled.");
            _channel.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in receive loop.");
            _channel.Writer.TryComplete(ex);
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
            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
            _logger.LogDebug("Sent payload of {Length} bytes.", payload.Length);
        }
        catch (OperationCanceledException) 
        {
            _logger.LogInformation("Send operation canceled.");
        }
        catch (WebSocketException ex) 
        {
            _logger.LogWarning(ex, "WebSocket send failed.");
        }
        finally
        {
            _sendLock.Release();
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _logger.LogInformation("Disposing YahooStreamer...");
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None).ConfigureAwait(false);
                    _logger.LogInformation("WebSocket closed gracefully.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WebSocket close failed, aborting.");
                    _socket.Abort();
                }
            }
            else
            {
                _socket.Abort();
            }
            await _cts.CancelAsync().ConfigureAwait(false);
            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Receive loop ended with exception during disposal.");
                }
            }
            _channel.Writer.TryComplete();
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
            _cts.Dispose();
            _logger.LogInformation("YahooStreamer disposed.");
        }
    }
}
