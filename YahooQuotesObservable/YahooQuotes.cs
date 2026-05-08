using ProtoBuf;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
namespace YahooQuotesObservable;

/*
.Net10
wss://streamer.finance.yahoo.com/
ProtoBuf
Reactive Extensions
*/

public static class YahooQuotes
{
    private static readonly Uri YahooStreamerUri = new("wss://streamer.finance.yahoo.com/");
    public static IObservable<PricingData> CreateObservable(string symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(Symbol symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<string> symbols) => CreateObservable(symbols.Select(s => s.ToSymbol()));
    public static IObservable<PricingData> CreateObservable(IEnumerable<Symbol> symbols)
    {
        byte[] requestMessage = CreateRequestMessage(symbols);

        return Observable.Create<PricingData>(async (observer, ct0) =>
        {
            bool disposed = false;
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct0);
            CancellationToken ct = cts.Token;
            HttpMessageInvoker invoker = new(new SocketsHttpHandler()); // required to use HTTP/2
            ClientWebSocket socket = new();
            socket.Options.HttpVersion = HttpVersion.Version30; // default is 1.1 or lower
            socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            try
            {
                await socket.ConnectAsync(YahooStreamerUri, invoker, ct).ConfigureAwait(false);
                if (socket.State != WebSocketState.Open)
                    throw new WebSocketException("WebSocketState is not open.");

                await socket.SendAsync(requestMessage, WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);

                while (true)
                {
                    using Stream stream = WebSocketStream.CreateReadableMessageStream(socket);
                    using StreamReader reader = new(stream, Encoding.UTF8);
                    string message = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                    byte[] bytes = Convert.FromBase64String(message);
                    PricingData pricingData = Serializer.Deserialize<PricingData>(bytes);
                    observer.OnNext(pricingData);
                }
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception we)
            {
                observer.OnError(we);
            }
            finally
            {
                await socket.SafeCloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
            }

            return Disposable.Create(() =>
            {
                if (disposed)
                    return;
                disposed = true;
                cts.Cancel();
                socket.Dispose();
                invoker.Dispose();
                cts.Dispose();
            });

        }).Publish().RefCount();
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
