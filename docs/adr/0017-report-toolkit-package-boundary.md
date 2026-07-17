# Report-side convenience helpers ship in a separate opt-in toolkit package

Building the caller-supplied configuration cards of ADR 0016 by hand is repetitive: every strategy
that wants its settings in the report writes the same reflection-or-boilerplate to turn a parameter
object into `ReportCard`s. That convenience is worth sharing, but it must not leak into any of the
existing packages — it is neither engine logic nor part of the report's projection, and a strategy
that does not want it should pay nothing for it.

We decided report-side convenience helpers ship in their own opt-in package,
**`backtester.net.report.toolkit`** (assembly and root namespace `Backtester.Report.Toolkit`), which
**references `Report` only**. It needs `ReportCard` and nothing else: no engine, broker, network, or
vendor dependency. The first helper is `ConfigurationCardBuilder`, which reflects an attributed
settings object (`ReportSettingAttribute`) into a list of configuration cards the caller then passes
to the existing `HtmlReportWriter.Write(result, path, configuration)` overload. This follows **ADR
0009**'s precedent directly: opt-in concerns ship as separate packages that version and pack
independently against the local feed, exactly as the network providers and `Report` itself do.

This **does not reopen ADR 0016**. The toolkit is a caller-side convenience for *building* the
caller-supplied cards ADR 0016 introduced — it runs entirely on the caller's side of the report
seam. `ReportModelBuilder` is untouched and its `Build(BacktestResult)` path stays a pure projection;
the engine still never sees a strategy's settings; and ADR 0016's rejected *"engine surfaces strategy
settings"* option stays rejected. The toolkit reflects a plain object the caller already holds and
hands back `ReportCard`s the caller composes with its own hand-built cards — the same input channel,
now with less boilerplate.

The boundary is deliberately **report-scoped**: this package is the home for helpers that produce
report artifacts. Engine-level convenience helpers, if ever wanted, would get a *different* package
so that adding report sugar never drags in engine surface, and vice versa.

## Considered options

- **Fold the helper into `backtester.net.report`.** Fewer packages, one place to look. Rejected: it
  forces the reflection/attribute machinery onto every consumer of the report package, including those
  who build their cards by hand or take the report as a transitive dependency, breaking the "you pay
  for it only if you opt in" property ADR 0009 established. `Report` stays the minimal card primitive
  (`ReportCard`) plus projection; card-*building* sugar layers on top in its own package.
- **Ship one solution-wide `backtester.net.toolkit` package for all conveniences.** Symmetry — a
  single place for every helper. Rejected: it collapses the report-vs-engine boundary the split is
  meant to preserve. A solution-wide toolkit would either reference both the engine and the report
  (dragging engine surface into report-only consumers) or grow an internal seam we would then have to
  police; a report-scoped package makes the dependency boundary structural instead.

## Consequences

- The toolkit versions and packs independently with its own `buildnumber.txt` and
  `GeneratePackageOnBuild`, publishing to the local feed through the shared
  `Directory.Build.targets` target — the same machinery as `Report`.
- Its transitive closure is `report.toolkit → report → backtester` (the engine, which is
  dependency-free); it pulls in no network (Yahoo/Alpaca) or vendor (`Alpaca.Markets`) package. The
  published nuspec declares exactly one dependency, `backtester.net.report`.
- Composing a report with settings cards means referencing `backtester.net.report.toolkit` in
  addition to `backtester.net.report`; a consumer who hand-builds cards ignores the toolkit entirely.
- A future helper's home is decided by one question: does it produce a *report* artifact? Report →
  this package; engine → a separate engine-scoped toolkit that would not reference `Report`.
