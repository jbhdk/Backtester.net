# Network data providers ship as separate packages

A concrete `IHistoricalDataProvider` that fetches from a **live external service** ships in its own
project and NuGet package referencing `Backtester`, rather than living in `Backtester\Data` inside
the core `backtester.net` package. The two such providers are `Backtester.Data.Yahoo`
(`backtester.net.yahoo`, BCL-only) and `Backtester.Data.Alpaca` (`backtester.net.alpaca`, on the
third-party `Alpaca.Markets` SDK). The engine is unaffected: it consumes the `IHistoricalDataProvider`
and `IHistoricalDataFetcher` seams, which stay in core, so a provider is wired into
`HistoricalDataFetcher` from its package exactly like any other.

The dividing line is **network vs. offline**, not third-party-vs-BCL. The core package keeps the data
seams, the cache orchestrator (`HistoricalDataFetcher`), and the offline, deterministic CSV path
(`CsvHistoricalDataFetcher`, `CsvBarLoader`) — all BCL and none of which make an outbound call. Every
provider that hits a live service is opt-in. This gives the core a property worth having: depending
on `backtester.net` pulls in nothing that calls the network or a vendor SDK on its own; a consumer
consciously adds a provider package to reach a data source. It also isolates `Alpaca.Markets` and its
transitive dependencies to consumers who actually use Alpaca.

This **supersedes the original framing of this ADR**, which drew the line at third-party dependencies
and explicitly kept BCL-only providers (Yahoo) in core. That rule was unstable: it left "a BCL-only
network provider" — exactly what Yahoo is — on the core side despite Yahoo carrying the same implicit
network coupling we wanted to keep out of the core. Network-vs-offline is the stable line.

## Considered options

- **Keep BCL-only network providers (Yahoo) in `Backtester\Data`, split only third-party ones
  (Alpaca).** The previous rule. Rejected: the core still ships a provider that makes outbound calls,
  so "core takes on no network dependency" was never actually true, and the next BCL network provider
  would have no principled home.
- **Make every provider its own package, including CSV.** Maximal symmetry. Rejected: it evicts the
  offline, deterministic CSV path that the tests, CI, and the README's reproducibility story rely on,
  for no benefit — CSV touches neither the network nor a vendor SDK.
- **Drop each network provider into `Backtester\Data`.** Simplest, faithful to the old "all providers
  live together" pattern. Rejected: bleeds vendor dependencies (Alpaca) and implicit network coupling
  (Yahoo) into every consumer of the core package.

## Consequences

- Core ships the data **seams** and the **offline** path only; CSV stays in `Backtester\Data`, while
  Yahoo and Alpaca are separate packages. The "all providers live together" pattern is gone.
- Removing `YahooHistoricalDataProvider` from `Backtester` is a **breaking change** to the
  `backtester.net` public surface: consumers add the `backtester.net.yahoo` package and switch to the
  `Backtester.Data.Yahoo` namespace (Alpaca already lived in `Backtester.Data.Alpaca`).
- Composing a live run means referencing `backtester.net` plus the chosen provider package; the
  provider is wired into `HistoricalDataFetcher` unchanged.
- Each provider package versions and packs independently, mirroring the existing `Report` split.
- A future provider's home is decided by one question: does it call a live service? Network → its own
  package; offline → core.
