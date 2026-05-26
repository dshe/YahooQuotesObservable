global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: CLSCompliant(true)]
[assembly: InternalsVisibleTo("YahooQuotesObservable.Tests")]

[assembly: SuppressMessage("Usage", "CA1031: Catch a more specific allowed exception type")]
[assembly: SuppressMessage("Usage", "CA1873: Evaluation of this argument may be expensive")]
[assembly: SuppressMessage("Usage", "CA1848: Use the LoggerMessage delegates")]
[assembly: SuppressMessage("Usage", "IDE0130:Namespace does not match folder structure")]
