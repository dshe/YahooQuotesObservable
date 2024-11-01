using NodaTime;
namespace YahooQuotesObservable;

public static class Extension
{
    public static DateTimeOffset ToDateTimeOffset(this long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    // Nodatime
    public static Instant ToInstant(this long ms) => Instant.FromUnixTimeMilliseconds(ms);

}
