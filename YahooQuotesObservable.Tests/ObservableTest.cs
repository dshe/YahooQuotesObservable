using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using YahooQuotesObservable;
namespace YahooQuotesApi.Tests;

public class ObservableTests(ITestOutputHelper output) : XunitTestBase(output, LogLevel.Trace)
{
    [Fact]
    public void BadSymbolTest()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => YahooQuotes.CreateObservable(["Bad Symbol"]));
        Write(exception.Message);
    }

    [Fact]
    public async Task UnknownSymbolTest() // Unknown symbols are ignored -> Timeout.
    {
        IObservable<PricingData> obs = YahooQuotes.CreateObservable(["UnknownSymbol"]);
        await Assert.ThrowsAsync<TimeoutException>(async () => await obs.FirstAsync().Timeout(TimeSpan.FromSeconds(5)));
    }

    [Fact(Skip = "Timeout when market is closed")]
    public async Task SymbolOkTest()
    {
        string symbol = "EURUSD=X";
        IObservable<PricingData> obs = YahooQuotes.CreateObservable([symbol]).Timeout(TimeSpan.FromSeconds(30));
        PricingData pricingData = await obs.FirstAsync();
        Write($"Id: {pricingData.Id}");
        Assert.Equal(symbol, pricingData.Id);
    }

    [Fact(Skip = "Timeout when market is closed")]
    public async Task StreamingExample()
    {
        // Create the observable.
        IObservable<PricingData> observable = YahooQuotes.CreateObservable(["AAPL", "EURUSD=X"]);

        // Subscribe to the observable.
        IDisposable subscription = observable.Subscribe(onNext: pricingData =>
        {
            Write($"Id: {pricingData.Id}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
        });

        await Task.Delay(TimeSpan.FromSeconds(10));

        // Unsubscribe from the observable.
        subscription.Dispose();
    }

    [Fact(Skip = "Timeout when market is closed")]
    public async Task SnapshotExample()
    {
        string symbol = "EURUSD=X";

        // Create the observable.
        IObservable<PricingData> observable = YahooQuotes.CreateObservable(symbol);

        // Subscribe to the observable, wait to receive the first output, then unsubscribe.
        PricingData pricingData = await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(10));
        Write($"Id: {pricingData.Id}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
        Assert.Equal(symbol, pricingData.Id);
    }
}

