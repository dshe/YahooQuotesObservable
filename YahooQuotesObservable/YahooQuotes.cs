using ProtoBuf;
using System.Net;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
namespace YahooQuotesObservable;

/*
.Net 10
wss://streamer.finance.yahoo.com/
ProtoBuf
Reactive Extensions
*/

public static class YahooQuotes
{
    public static IObservable<PricingData> CreateObservable(string symbol) => CreateObservable([symbol.ToSymbol()]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<string> symbols) => CreateObservable(symbols.Select(s => s.ToSymbol()));
    public static IObservable<PricingData> CreateObservable(Symbol symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<Symbol> symbols)
    {
        string requestMessage = CreateRequestMessage(symbols);

        return Observable.Create<PricingData>(async (observer, ct0) =>
        {
            bool disposed = false;
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct0);
            CancellationToken ct = cts.Token;

            SocketsHttpHandler handler = new();
            ClientWebSocket socket = new();
            socket.Options.HttpVersion = HttpVersion.Version30; // default is 1.1 or lower
            socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            try
            {
                await socket.ConnectAsync(new Uri("wss://streamer.finance.yahoo.com/"),
                    new HttpMessageInvoker(handler), ct).ConfigureAwait(false);
                if (socket.State != WebSocketState.Open)
                    throw new WebSocketException("WebSocketState is not open.");

                await socket.SendAsync(Encoding.UTF8.GetBytes(requestMessage), WebSocketMessageType.Text, true, ct)
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
                if (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
            }

            return Disposable.Create(() =>
            {
                if (disposed)
                    return;
                disposed = true;
                cts.Cancel();
                socket.Dispose();
                handler.Dispose();
                cts.Dispose();
            });

        }).Publish().RefCount();
    }


    private static string CreateRequestMessage(IEnumerable<Symbol> symbols)
    {
        IEnumerable<string> quotedSymbols = symbols.Distinct().Select(symbol => $"\"{symbol.Name}\"");

        return new StringBuilder()
        .Append("{\"subscribe\":[")
        .AppendJoin(", ", quotedSymbols)
        .Append("]}")
        .ToString();
    }

}
