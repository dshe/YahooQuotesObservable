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

    private static readonly TimeSpan _idleShutdownDelay = TimeSpan.FromSeconds(30);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SymbolStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISubject<PricingData> _aggregate = Subject.Synchronize(new Subject<PricingData>());
    private readonly CancellationTokenSource _cts = new();
#pragma warning disable CA2213 // Types that own disposable fields should be disposable
    private YahooStreamer? _yahooStreamer;
    private CancellationTokenSource? _idleShutdownCts;
#pragma warning restore CA2213
    private Task? _routingTask;
    private int _disposed;
    public IObservable<PricingData> AggregateObservable => _aggregate.AsObservable();
    public IEnumerable<string> Symbols => _streams.Keys;

    public YahooQuoteHub() : this(NullLoggerFactory.Instance) { }
    public YahooQuoteHub(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<YahooQuoteHub>();
    }

    public IObservable<PricingData> CreateObservable(string symbol)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!Symbol.TryCreate(symbol, out _))
            throw new ArgumentException($"Invalid symbol format: '{symbol}'.");

        return Observable.Create<PricingData>(observer =>
        {
            _logger.LogInformation("Creating observable for symbol: {Symbol}", symbol);

            SymbolStream stream = _streams.GetOrAdd(symbol, static _ => new SymbolStream());
            IDisposable subscription = stream.Stream.Subscribe(observer);
            if (Interlocked.Increment(ref stream.ObserverCount) == 1)
                _ = EnsureStartedAndSubscribeAsync(symbol);

            return Disposable.Create(() =>
            {
                subscription.Dispose();
                if (Interlocked.Decrement(ref stream.ObserverCount) == 0)
                {
                    _streams.TryRemove(symbol, out _);
                    YahooStreamer? streamer = _yahooStreamer;
                    if (streamer != null)
                        _ = streamer.UnsubscribeAsync([symbol]);
                    _logger.LogDebug("Disposing stream for {Symbol}", symbol);
                    stream.Dispose();
                    if (_streams.IsEmpty)
                        _ = ScheduleIdleShutdownAsync();
                }
            });
        });
    }

    private async Task EnsureStartedAndSubscribeAsync(string symbol)
    {
        CancelIdleShutdown();
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            YahooStreamer streamer;
            if (_yahooStreamer is null)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                streamer = new(_loggerFactory);
#pragma warning restore CA2000
                await streamer.ConnectAsync().ConfigureAwait(false);
                _yahooStreamer = streamer;
                _routingTask = RouteLoopAsync(streamer);
                _logger.LogInformation("YahooStreamer started.");
            }
            else
            {
                streamer = _yahooStreamer;
            }
            await streamer.SubscribeAsync([symbol]).ConfigureAwait(false);
            _logger.LogInformation("Subscribed to {Symbol}", symbol);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task RouteLoopAsync(YahooStreamer streamer)
    {
        try
        {
            await foreach (PricingData pricing in streamer.Messages.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                _aggregate.OnNext(pricing);
                if (_streams.TryGetValue(pricing.Symbol, out SymbolStream? stream))
                    stream.Stream.OnNext(pricing);
            }
        }
        catch (OperationCanceledException) 
        {
            _logger.LogInformation("RouteLoop canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RouteLoop terminated unexpectedly.");
            await StopAsync(fromRouteLoop: true).ConfigureAwait(false);
        }
    }

    private async Task ScheduleIdleShutdownAsync()
    {
        CancellationTokenSource shutdownCts = new();
        Interlocked.Exchange(ref _idleShutdownCts, shutdownCts);
        try
        {
            await Task.Delay(_idleShutdownDelay, shutdownCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Idle shutdown triggered.");
            await StopAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) 
        {
            _logger.LogDebug("Idle shutdown canceled.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _idleShutdownCts, null, shutdownCts);
            shutdownCts.Dispose();
        }
    }

    private async Task StopAsync(bool fromRouteLoop = false)
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            YahooStreamer? streamer = Interlocked.Exchange(ref _yahooStreamer, null);
            if (streamer is null)
                return;
            await streamer.DisposeAsync().ConfigureAwait(false);
            Task? task = _routingTask;
            _routingTask = null;
            CancelIdleShutdown();
            if (!fromRouteLoop && task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Routing task ended with exception.");
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void CancelIdleShutdown()
    {
        CancellationTokenSource? old = Interlocked.Exchange(ref _idleShutdownCts, null);
        if (old is not null)
        {
            old.Cancel();
            old.Dispose();
            _logger.LogDebug("Idle shutdown canceled.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _logger.LogInformation("Disposing YahooObserverHub.");
#pragma warning disable CA1849 // Call async methods when in an async method
        _cts.Cancel();
#pragma warning restore CA1849
        await StopAsync().ConfigureAwait(false);
        foreach (SymbolStream stream in _streams.Values)
            stream.Dispose();
        _streams.Clear(); // not strictly necessary
        _aggregate.OnCompleted();
        (_aggregate as IDisposable)?.Dispose();  
        _lifecycleLock.Dispose();
        _cts.Dispose();
        _logger.LogInformation("YahooObserverHub disposed.");
    }

}
