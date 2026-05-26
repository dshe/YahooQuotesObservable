using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
namespace YahooQuotesObservable;

public sealed class YahooQuoteHub : IAsyncDisposable
{
    internal sealed class SymbolStream : IDisposable
    {
        internal ISubject<PricingData> Stream { get; } = Subject.Synchronize(new ReplaySubject<PricingData>(1));
        internal int ObserverCount;
        public void Dispose()
        {
            Stream.OnCompleted();
            (Stream as IDisposable)?.Dispose();
        }
    }

    private static readonly TimeSpan IdleShutdownDelay = TimeSpan.FromSeconds(30);
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger logger;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SymbolStream> streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISubject<PricingData> aggregate = Subject.Synchronize(new Subject<PricingData>());
    private readonly CancellationTokenSource cts = new();
#pragma warning disable CA2213 // Types that own disposable fields should be disposable
    private YahooStreamer? yahooStreamer;
    private CancellationTokenSource? idleShutdownCts;
#pragma warning restore CA2213
    private Task? routingTask;
    private int disposed;
    public IObservable<PricingData> Aggregate => aggregate.AsObservable();
    public IEnumerable<string> Symbols => streams.Keys;

    public YahooQuoteHub() : this(NullLoggerFactory.Instance) { }
    public YahooQuoteHub(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<YahooQuoteHub>();
    }

    public IObservable<PricingData> CreateObservable(string symbol)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (!Symbol.TryCreate(symbol, out _))
            throw new ArgumentException($"Invalid symbol format: '{symbol}'.");

        return Observable.Create<PricingData>(observer =>
        {
            logger.LogInformation("Creating observable for symbol: {Symbol}", symbol);

            SymbolStream stream = streams.GetOrAdd(symbol, static _ => new SymbolStream());
            IDisposable subscription = stream.Stream.Subscribe(observer);
            if (Interlocked.Increment(ref stream.ObserverCount) == 1)
                _ = EnsureStartedAndSubscribeAsync(symbol);

            return Disposable.Create(() =>
            {
                subscription.Dispose();
                if (Interlocked.Decrement(ref stream.ObserverCount) == 0)
                {
                    streams.TryRemove(symbol, out _);
                    YahooStreamer? streamer = yahooStreamer;
                    if (streamer != null)
                        _ = streamer.UnsubscribeAsync([symbol]);
                    logger.LogDebug("Disposing stream for {Symbol}", symbol);
                    stream.Dispose();
                    if (streams.IsEmpty)
                        _ = ScheduleIdleShutdownAsync();
                }
            });
        });
    }

    private async Task EnsureStartedAndSubscribeAsync(string symbol)
    {
        CancelIdleShutdown();
        await lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            YahooStreamer streamer;
            if (yahooStreamer is null)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                streamer = new(loggerFactory);
#pragma warning restore CA2000
                await streamer.ConnectAsync().ConfigureAwait(false);
                yahooStreamer = streamer;
                routingTask = RouteLoop(streamer);
                logger.LogInformation("YahooStreamer started.");
            }
            else
            {
                streamer = yahooStreamer;
            }
            await streamer.SubscribeAsync([symbol]).ConfigureAwait(false);
            logger.LogInformation("Subscribed to {Symbol}", symbol);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private async Task RouteLoop(YahooStreamer streamer)
    {
        try
        {
            await foreach (PricingData pricing in streamer.Messages.ReadAllAsync(cts.Token).ConfigureAwait(false))
            {
                aggregate.OnNext(pricing);
                if (streams.TryGetValue(pricing.Symbol, out SymbolStream? stream))
                    stream.Stream.OnNext(pricing);
            }
        }
        catch (OperationCanceledException) 
        {
            logger.LogInformation("RouteLoop canceled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RouteLoop terminated unexpectedly.");
            await StopAsync(fromRouteLoop: true).ConfigureAwait(false);
        }
    }

    private async Task ScheduleIdleShutdownAsync()
    {
        CancellationTokenSource shutdownCts = new();
        Interlocked.Exchange(ref idleShutdownCts, shutdownCts);
        try
        {
            await Task.Delay(IdleShutdownDelay, shutdownCts.Token).ConfigureAwait(false);
            logger.LogInformation("Idle shutdown triggered.");
            await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) 
        {
            logger.LogDebug("Idle shutdown canceled.");
        }
        finally
        {
            Interlocked.CompareExchange(ref idleShutdownCts, null, shutdownCts);
            shutdownCts.Dispose();
        }
    }

    private async Task StopAsync(bool fromRouteLoop = false)
    {
        await lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            YahooStreamer? streamer = Interlocked.Exchange(ref yahooStreamer, null);
            if (streamer is null)
                return;
            await streamer.DisposeAsync().ConfigureAwait(false);
            Task? task = routingTask;
            routingTask = null;
            CancelIdleShutdown();
            if (!fromRouteLoop && task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Routing task ended with exception.");
                }
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private void CancelIdleShutdown()
    {
        CancellationTokenSource? old = Interlocked.Exchange(ref idleShutdownCts, null);
        if (old != null)
        {
            old.Cancel();
            old.Dispose();
            logger.LogDebug("Idle shutdown canceled.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        logger.LogInformation("Disposing YahooObserverHub.");
#pragma warning disable CA1849 // Call async methods when in an async method
        cts.Cancel();
#pragma warning restore CA1849
        await StopAsync().ConfigureAwait(false);
        foreach (SymbolStream stream in streams.Values)
            stream.Dispose();
        streams.Clear(); // not strictly necessary
        aggregate.OnCompleted();
        (aggregate as IDisposable)?.Dispose();  
        lifecycleLock.Dispose();
        cts.Dispose();
        logger.LogInformation("YahooObserverHub disposed.");
    }

}
