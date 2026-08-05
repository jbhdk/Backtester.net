using System.Runtime.CompilerServices;

// Engine-internal machinery stays internal and is asserted on directly from the test project
// (ADR 0033). This must be a hand-written attribute: the project sets GenerateAssemblyInfo=false,
// so the MSBuild <InternalsVisibleTo> item produces nothing and is silently inert.
[assembly: InternalsVisibleTo("BacktesterTests")]

// Two sibling packages reach Portfolio members that no strategy app needs, so those members are
// internal and only these two assemblies are named: Backtester.Report builds its report model from
// GetPerformanceStats, GetPerformanceStatsBySymbol and EquityHistory, and Backtester.Optimization
// refuses un-fetched ConversionSymbols. A uniform grant to every sibling was rejected — it would let
// Report reach into Bracket next. Hand-written for the same reason as the grant above:
// GenerateAssemblyInfo=false makes the MSBuild <InternalsVisibleTo> item silently inert.
[assembly: InternalsVisibleTo("Backtester.Report")]
[assembly: InternalsVisibleTo("Backtester.Optimization")]
