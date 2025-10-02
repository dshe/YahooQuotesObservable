using System.Net.WebSockets;
namespace YahooQuotesObservable;

public static class WebSocketExtensions
{
    public static async Task SafeCloseAsync(this ClientWebSocket socket,
            WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
            string statusDescription = "Closing",
            CancellationToken cancellationToken = default)
    {
        if (socket == null)
            return;

        try
        {
            switch (socket.State)
            {
                case WebSocketState.Open:
                    // Initiate close handshake
                    await socket.CloseAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
                    break;

                case WebSocketState.CloseReceived:
                    // Server initiated close, we respond
                    await socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
                    break;

                case WebSocketState.CloseSent:
                    // We already sent close — wait for server response
                    byte[] buffer = new byte[1024];
                    while (socket.State is not WebSocketState.Closed and not WebSocketState.Aborted)
                    {
                        WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                    }
                    break;
                case WebSocketState.None:
                    break;
                case WebSocketState.Connecting:
                    break;
                case WebSocketState.Closed:
                    break;
                case WebSocketState.Aborted:
                    break;
                default:
                    break;
            }
        }
        catch (WebSocketException)
        {
            // Ignore — connection may already be terminated
        }
        catch (OperationCanceledException)
        {
            // Optional: respect cancellation
        }
        catch (Exception ex)
        {
            // Optional: log unexpected exceptions
            Console.WriteLine($"SafeCloseAsync failed: {ex}");
        }
    }
}
