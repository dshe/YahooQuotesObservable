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

    [Fact]
    public async Task SnapshotTest()
    {
        string symbol = "EURUSD=X";

        // Create the observable.
        IObservable<PricingData> observable = YahooQuotes.CreateObservable(symbol);

        // Subscribe to the observable, wait to receive the first output, then unsubscribe.
        PricingData pricingData = await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(10));
        Write($"Symbol: {pricingData.Symbol}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
        Assert.Equal(symbol, pricingData.Symbol);
        Assert.True(pricingData.Price > 0);
    }


    [Fact]
    public async Task StreamingTest()
    {
        // Create the observable.
        IObservable<PricingData> observable = YahooQuotes.CreateObservable(["AAPL", "EURUSD=X"]);

        // Subscribe to the observable.
        IDisposable subscription = observable.Subscribe(onNext: pricingData =>
        {
            Write($"Id: {pricingData.Symbol}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
        });

        await Task.Delay(TimeSpan.FromSeconds(10));

        // Unsubscribe from the observable.
        subscription.Dispose();
    }


    [Fact]
    public async Task DataExample()
    {
        string symbol = "EURUSD=X";

        IObservable<PricingData> observable = YahooQuotes.CreateObservable(symbol);
        PricingData pricingData = await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(10));

        foreach (var pi in typeof(PricingData).GetProperties())
        {
            object? value = pi.GetValue(pricingData);
            if (value == pi.PropertyType.DefaultValueOfType())
                continue;
            Write($"{pi.Name}: {value}");
        }
    }
}

