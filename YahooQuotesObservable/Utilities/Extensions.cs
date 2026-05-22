using NodaTime;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
namespace YahooQuotesObservable;

public static class Extension
{
    public static DateTimeOffset ToDateTimeOffset(this long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    // NodaTime
    public static Instant ToInstant(this long ms) => Instant.FromUnixTimeMilliseconds(ms);

    internal static object? DefaultValueOfType(this Type type) => type.IsValueType ? RuntimeHelpers.GetUninitializedObject(type) : null;

    internal static void CloseAndDispose(this ClientWebSocket socket)
    {
        try
        {
            socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {

        }
        socket.Dispose();
    }

}
