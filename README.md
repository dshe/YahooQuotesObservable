# YahooQuotesObservable&nbsp;&nbsp; [![Build status](https://ci.appveyor.com/api/projects/status/qx83p28cdqvcpbhm?svg=true)](https://ci.appveyor.com/project/dshe/yahooquotesapi) [![NuGet](https://img.shields.io/nuget/vpre/YahooQuotesApi.svg)](https://www.nuget.org/packages/YahooQuotesApi/) [![NuGet](https://img.shields.io/nuget/dt/YahooQuotesApi?color=orange)](https://www.nuget.org/packages/YahooQuotesApi/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/badges/StandWithUkraine.svg)](https://stand-with-ukraine.pp.ua)


**Streaming quotes from Yahoo Finances**
- **.NET 8.0** library
- simple and intuitive API
- fault-tolerant
- tested
- dependencies: 

### Installation
```bash
PM> Install-Package YahooQuotesObservable
```

### Examples
#### snapshot
```csharp
using System.Reactive.Linq;
using YahooQuotesObservable;

// Create the observable.
IObservable<PricingData> observable = YahooQuotes.CreateObservable(["AAPL", "EURUSD=X"]);

// Subscribe to the observable.
IDisposable subscription = observable.Subscribe(pricingData =>
{
    Write($"Id: {pricingData.Id}, Price: {pricingData.Price}, Time: {pricingData.Time.ToInstant()}");
});

await Task.Delay(TimeSpan.FromSeconds(10));

// Unsubscribe from the observable.
subscription.Dispose();
```
