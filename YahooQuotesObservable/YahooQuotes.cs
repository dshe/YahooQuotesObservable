using ProtoBuf;
using System.Buffers;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
namespace YahooQuotesObservable;

// Note: if decode is slower than feed rate, intermediate ticks will be lost.
// This is intentional for market data.

public static class YahooQuotes
{
    private static readonly Uri YahooStreamerUri = new("wss://streamer.finance.yahoo.com/?version=2");
    public static IObservable<PricingData> CreateObservable(string symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(Symbol symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<string> symbols) => CreateObservable(symbols.Select(s => s.ToSymbol()));
    public static IObservable<PricingData> CreateObservable(IEnumerable<Symbol> symbols)
    {
        byte[] requestMessage = CreateRequestMessage(symbols);

        return Observable.Create<PricingData>(observer =>
        {
            CancellationTokenSource cts = new();
            CancellationToken ct = cts.Token;

            Channel<ReadOnlyMemory<byte>> channel = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(1024)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            ClientWebSocket socket = new();

            _ = Task.Run(async () => // PRODUCER
            {
                try
                {
                    await socket.ConnectAsync(YahooStreamerUri, ct).ConfigureAwait(false);
                    await socket.SendAsync(requestMessage, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;
                            ReadOnlyMemory<byte> msg = buffer.AsMemory(0, result.Count);
                            await channel.Writer.WriteAsync(msg, ct).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });

            _ = Task.Run(async () => // CONSUMER
            {
                try
                {
                    await foreach (ReadOnlyMemory<byte> msg in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        try
                        {
                            using JsonDocument json = JsonDocument.Parse(msg);
                            if (!json.RootElement.TryGetProperty("message", out JsonElement m))
                                continue;
                            string? base64 = m.GetString();
                            if (string.IsNullOrEmpty(base64))
                                continue;
                            byte[] protobufBytes = Convert.FromBase64String(base64);
                            using MemoryStream ms = new(protobufBytes);
                            PricingData pricing = Serializer.Deserialize<PricingData>(ms);
                            observer.OnNext(pricing);
                        }
                        catch (ProtoException)
                        {
                            // ignore Yahoo noise packets
                        }
                    }
                    observer.OnCompleted();
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        observer.OnError(ex);
                }
            });

            return Disposable.Create(() =>
            {
                cts.Cancel();
                socket.CloseAndDispose();
                channel.Writer.TryComplete();
                cts.Dispose();
            });
        })
        .Publish()
        .RefCount();
    }

    private static byte[] CreateRequestMessage(IEnumerable<Symbol> symbols)
    {
        if (!symbols.Any())
            throw new ArgumentNullException(nameof(symbols));

        var anonymousObj = new
        {
            subscribe = symbols.Select(s => s.Name).Distinct()
        };

        string json = JsonSerializer.Serialize(anonymousObj);

        return Encoding.UTF8.GetBytes(json);
    }

}
