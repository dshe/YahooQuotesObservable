using System.Reactive.Linq;
namespace YahooQuotesObservable.Tests;

// Most of these tests require financial markets to be open.

public class QuoteHubTests : XunitTestBase, IAsyncDisposable
{
    public YahooQuoteHub yahooQuoteHub;

    public QuoteHubTests(ITestOutputHelper output) : base(output, LogLevel.Trace)
    {
        yahooQuoteHub = new(LogFactory);
    }

    [Fact]
    public void BadSymbolTest()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => yahooQuoteHub.CreateObservable("Bad Symbol"));
        Write(exception.Message);
    }

    [Fact]
    public async Task UnknownSymbolTest() // Unknown symbols are ignored -> Timeout.
    {
        IObservable<PricingData> observable = yahooQuoteHub.CreateObservable("UnknownSymbol");
        await Assert.ThrowsAsync<TimeoutException>(async () => await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task NoDataTest()
    {
        IObservable<PricingData> observable = yahooQuoteHub.CreateObservable("DFSV");
        await Assert.ThrowsAsync<TimeoutException>(async () => await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SnapshotTest()
    {
        string symbol = "EURUSD=X";

        // Create the observable.
        IObservable<PricingData> observable = yahooQuoteHub.CreateObservable(symbol);

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
        IObservable<PricingData> observable = yahooQuoteHub.CreateObservable("EURUSD=X");

        // Subscribe to the observable.
        IDisposable subscription = observable.Subscribe(onNext: pricingData =>
        {
            Write($"Id: {pricingData.Symbol}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
        });

        await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        subscription.Dispose();
    }

    [Fact]
    public async Task DataExample()
    {
        string symbol = "EURUSD=X";

        IObservable<PricingData> observable = yahooQuoteHub.CreateObservable(symbol);
        PricingData pricingData = await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(10));

        foreach (var pi in typeof(PricingData).GetProperties())
        {
            object? value = pi.GetValue(pricingData);
            if (value != pi.PropertyType.DefaultValueOfType())
                Write($"{pi.Name}: {value}");
        }
    }

    [Fact]
    public async Task YahooStreamerTest()
    {
        YahooStreamer transport = new(LogFactory);
        await transport.ConnectAsync();
        await transport.SubscribeAsync(["EURUSD=X"]);
        PricingData pricingData = await transport.Messages.ReadAsync(TestContext.Current.CancellationToken);
        await transport.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await yahooQuoteHub.DisposeAsync();
}

