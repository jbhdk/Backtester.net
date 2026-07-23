# Coverage floor and priming

To support out-of-sample and walk-forward workflows — fetch a wide range once, then run many
backtests over sub-ranges of it — the `HistoricalDataFetcher` records a **Coverage floor** per
symbol-and-interval: the earliest range start it has ever asked the Provider for, stored in a small
sidecar file beside the Cache CSV. A run whose `from` precedes an existing floor is refused with a
`DataCoverageException` rather than served a silently short slice, and a new `IDataPrimer.PrimeAsync`
warms the Cache for a wide range without running a backtest. This amends
[ADR 0006](0006-cache-freshness-over-completeness.md), which deliberately tracked no coverage
metadata.

## Why this reopens ADR 0006

ADR 0006 rejected persisting coverage because the only workload then — offline backtesting where
"the last week is good enough" — did not justify the metadata, and because coverage *inferred from
bar timestamps* is irreducibly ambiguous: an earliest cached bar later than the requested `from`
cannot, from the bars alone, distinguish a **late listing** (the symbol did not trade earlier; the
Cache is complete) from an **under-fetch** (it did trade; we never asked). The prime workflow changes
both premises. Out-of-sample testing is a real workload that needs the precision, and — crucially —
the floor removes the ambiguity by recording what was *asked* rather than inferring from what was
*returned*: with the floor at 2020, an earliest bar of 2021 is a known-empty front (a late listing),
whereas a request from 2019 against a floor of 2020 is a known under-fetch and is refused.

## Considered options

- **Infer coverage from cached bars only (no sidecar).** Rejected: it cannot separate a late listing
  from an under-fetch at the front edge, so it must either falsely refuse every late-listed symbol or
  stay silent on genuine under-fetches — the exact ambiguity ADR 0006 documented.
- **A full coverage sidecar recording every fetched interval (a set of ranges).** Rejected as before:
  interval-set storage plus merge logic is more machinery than the workload needs. Coverage is always
  one contiguous range per symbol, so a single front low-water mark suffices.

## Decision detail

- The floor is a **front-edge low-water mark only**. The recent edge remains the Freshness window's
  concern (ADR 0006); the tail still self-heals by fetching `latest → to` on a stale Cache, which never
  lowers the floor.
- The floor may be lowered to `X` **only after actually calling the Provider from `X`** — otherwise the
  floor would claim coverage the Cache lacks. So it moves only on an empty-Cache fetch (which sets
  `floor = from`) and on a Prime.
- **Prime fetches the whole `[from, to]` wholesale** and lets `AppendAndMerge` dedup absorb the overlap
  with any existing Cache, then lowers the floor. This re-downloads the already-cached middle on a
  re-prime; we accepted that cost for simpler code, since priming is deliberate and infrequent.
- A run **never** back-fills the front (unchanged from ADR 0006) — it throws. Only a Prime fills the
  front, by virtue of fetching the wide range wholesale.
- `PrimeAsync` lives on a **separate `IDataPrimer` seam**, not on `IHistoricalDataFetcher`, so the
  engine's fetcher seam keeps only `FetchAsync` (ISP). One `HistoricalDataFetcher` implements both.

## Consequences

- **A legacy Cache has no floor**, and a missing floor means *trust the Cache* — the guard fires only
  when a floor exists and `from` precedes it. Existing caches keep working untouched; they gain a floor
  the next time the Fetcher calls the Provider for that symbol, or immediately if primed.
- **Widening a run's start over an existing Cache now throws** where ADR 0006 silently served a short
  slice. This is the intended reversal: silent-short is the cardinal sin for a backtester. The remedy
  is in the exception message — prime from the earlier date, or delete the Cache file.
- The Cache CSV format is unchanged; `CsvBarLoader` is untouched. The floor is a separate small sidecar
  so the CSV stays a clean, importable OHLCV file.
- A `DataCoverageException` carries `Symbol`, `RequestedFromUtc`, `CoverageFloorUtc`, and `Interval`, so
  a caller can catch the specific failure and a human is told exactly which fix to apply.
