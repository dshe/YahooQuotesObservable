using System.Text;
using System.Reactive.Linq;
using System.Net.WebSockets;
using System.Net;
using ProtoBuf;
using System.Reactive.Disposables;
namespace YahooQuotesObservable;

public static class YahooQuotes
{
    public static IObservable<PricingData> CreateObservable(string symbol) => CreateObservable([symbol.ToSymbol()]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<string> symbols) => CreateObservable(symbols.Select(s => s.ToSymbol()));
    public static IObservable<PricingData> CreateObservable(Symbol symbol) => CreateObservable([symbol]);
    public static IObservable<PricingData> CreateObservable(IEnumerable<Symbol> symbols)
    {
        HashSet<Symbol> syms = [.. symbols];
        if (syms.Any(s => s.IsCurrency))
            throw new ArgumentException($"Invalid symbol: {syms.First(s => s.IsCurrency)}.");

        string requestMessage = CreateRequestMessage(syms);
        byte[] responseBuffer = new byte[1024];

        return Observable.Create<PricingData>(async (observer, ct) =>
        {
            SocketsHttpHandler httpHandler = new();
            ClientWebSocket webSocket = new();
            webSocket.Options.HttpVersion = HttpVersion.Version30; // default is 1.1 or lower
            webSocket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

            try
            {
                await webSocket.ConnectAsync(new Uri("wss://streamer.finance.yahoo.com/"),
                    new HttpMessageInvoker(httpHandler), ct).ConfigureAwait(false);
                if (webSocket.State != WebSocketState.Open)
                    throw new WebSocketException("WebSocketState is not open.");

                await webSocket.SendAsync(Encoding.UTF8.GetBytes(requestMessage), WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);

                while (true)
                {
                    WebSocketReceiveResult response = await webSocket.ReceiveAsync(responseBuffer, ct).ConfigureAwait(false);
                    if (!response.EndOfMessage)
                        throw new WebSocketException("The response message is not complete.");
                    string responseMessage = Encoding.UTF8.GetString(responseBuffer, 0, response.Count);
                    byte[] bytes = Convert.FromBase64String(responseMessage);
                    PricingData pricingData = Serializer.Deserialize<PricingData>((ReadOnlySpan<byte>)bytes);
                    observer.OnNext(pricingData);
                }
            }
            catch (Exception e)
            {
                if (ct.IsCancellationRequested) // unsubscribe from observable
                    observer.OnCompleted();
                else
                    observer.OnError(e);
            }

            return Disposable.Create(() =>
            {
                webSocket.Dispose();
                httpHandler.Dispose();
            });

        }).Publish().RefCount();
    }

    private static string CreateRequestMessage(IEnumerable<Symbol> symbols)
    {
        StringBuilder sb = new();
        sb.Append("{\"subscribe\":[");
        IEnumerable<string> symbolsList = symbols.Select(symbol => $"\"{symbol.Name}\"");
        sb.AppendJoin(", ", symbolsList);
        sb.Append("]}");
        return sb.ToString();
    }

}
