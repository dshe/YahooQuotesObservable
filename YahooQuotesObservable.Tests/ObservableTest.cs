using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using NodaTime;
using Xunit;
using Xunit.Abstractions;
using YahooQuotesObservable;
namespace YahooQuotesApi.Tests;

public class ObservableTests(ITestOutputHelper output) : XunitTestBase(output, LogLevel.Trace)
{
    [Fact]
    public void BadSymbolTest()
    {
        Assert.Throws<ArgumentException>(() => YahooQuotes.CreateObservable(["Bad Symbol"]));
    }

    [Fact]
    public async Task UnknownSymbolTest() // Unknown symbols are ignored -> Timeout.
    {
        IObservable<PricingData> obs = YahooQuotes.CreateObservable(["UnknownSymbol"]).Timeout(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<TimeoutException>(async () => await obs.FirstAsync());
    }

    [Fact]
    public async Task SymbolOkTest()
    {
        string symbol = "EURUSD=X";
        IObservable<PricingData> obs = YahooQuotes.CreateObservable([symbol]).Timeout(TimeSpan.FromSeconds(10));
        PricingData pricingData = await obs.FirstAsync();
        Write($"Id: {pricingData.Id}");
        Assert.Equal(symbol, pricingData.Id);
    }


}

