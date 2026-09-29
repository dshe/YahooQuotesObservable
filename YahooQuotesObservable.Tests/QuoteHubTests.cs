using System.Reactive.Linq;
using System.Reflection;
namespace YahooQuotesObservable.Tests;

// Most of these tests require financial markets to be open.

public sealed class QuoteHubTests : XunitTestBase, IAsyncDisposable
{
    public YahooQuoteHub YahooQuoteHub;

    public QuoteHubTests(ITestOutputHelper output) : base(output, LogLevel.Trace)
    {
        YahooQuoteHub = new(LogFactory);
    }

    [Fact]
    public void BadSymbolTest()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => YahooQuoteHub.CreateObservable("Bad Symbol"));
        Write(exception.Message);
    }

    [Fact]
    public async Task UnknownSymbolTest() // Unknown symbols are ignored -> Timeout.
    {
        IObservable<PricingData> observable = YahooQuoteHub.CreateObservable("UnknownSymbol");
        await Assert.ThrowsAsync<TimeoutException>(async () => await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(5)));
    }

    [Fact(Skip = "Data is available for this symbol")]
    public async Task NoDataTest()
    {
        IObservable<PricingData> observable = YahooQuoteHub.CreateObservable("DFSV");
        await Assert.ThrowsAsync<TimeoutException>(async () => await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SnapshotTest()
    {
        string symbol = "EURUSD=X";

        // Create the observable.
        IObservable<PricingData> observable = YahooQuoteHub.CreateObservable(symbol);

        // Subscribe to the observable, wait to receive the first output, then unsubscribe.
        PricingData pricingData = await observable.FirstAsync().Timeout(TimeSpan.FromSeconds(10));

        Write($"Symbol: {pricingData.Symbol}, Price: {pricingData.Price}\n");
        Assert.Equal(symbol, pricingData.Symbol);
        Assert.True(pricingData.Price > 0);

        foreach (PropertyInfo pi in typeof(PricingData).GetProperties())
        {
            object? value = pi.GetValue(pricingData);
            if (value != pi.PropertyType.DefaultValueOfType())
                Write($"{pi.Name}: {value}");
        }
    }

    [Fact]
    public async Task StreamingTest()
    {
        string symbol = "EURUSD=X";

        // Create the observable.
        IObservable<PricingData> observable = YahooQuoteHub.CreateObservable(symbol);

        IList<PricingData> pricingData = await observable
            .Take(3)
            .ToList()
            .Timeout(TimeSpan.FromSeconds(10));

        foreach (PricingData data in pricingData)
            Write($"Id: {data.Symbol}, Price: {data.Price}, Time: {data.Time.ToInstant()}");
    }

    [Fact]
    public async Task YahooStreamerTest()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;

        YahooStreamer streamer = new(LogFactory);
        await streamer.ConnectAsync();
        await streamer.SubscribeAsync(["EURUSD=X"]);
        PricingData pricingData = await streamer.Messages.ReadAsync(ct);
        await streamer.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await YahooQuoteHub.DisposeAsync();
}

