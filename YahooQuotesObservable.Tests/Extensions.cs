using NodaTime;
using System.Runtime.CompilerServices;
namespace YahooQuotesObservable.Tests;

public static class Extension
{
    extension(long ms)
    {
        // NodaTime
        public Instant ToInstant() => Instant.FromUnixTimeMilliseconds(ms);
        public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    extension(Type type)
    {
        internal object? DefaultValueOfType() => type.IsValueType ? RuntimeHelpers.GetUninitializedObject(type) : null;
    }
}
