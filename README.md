# YahooQuotesObservable&nbsp;&nbsp; 
[![Build status](https://ci.appveyor.com/api/projects/status/kmtn827fe0e668qy?svg=true)](https://ci.appveyor.com/project/dshe/yahooquotesobservable)
[![NuGet](https://img.shields.io/nuget/vpre/YahooQuotesObservable.svg)](https://www.nuget.org/packages/YahooQuotesObservable/) 
[![NuGet](https://img.shields.io/nuget/dt/YahooQuotesObservable?color=orange)](https://www.nuget.org/packages/YahooQuotesObservable/) 
[![License](https://img.shields.io/badge/license-Apache%202.0-7755BB.svg)](https://opensource.org/licenses/Apache-2.0) 

**Streaming quotes from Yahoo Finance.**
- **.NET 8.0** library
- simple and intuitive API
- fault-tolerant
- dependencies: protobuf-net

### Installation
```bash
PM> Install-Package YahooQuotesObservable
```

### Example
```csharp
using System.Reactive.Linq;
using YahooQuotesObservable;

// Create the observable.
IObservable<PricingData> observable = YahooQuotes.CreateObservable(["AAPL", "EURUSD=X"]);

// Subscribe to the observable.
IDisposable subscription = observable.Subscribe(pricingData =>
{
    Console.Write($"Id: {pricingData.Id}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
});

await Task.Delay(TimeSpan.FromSeconds(10));

// Unsubscribe from the observable.
subscription.Dispose();
```
