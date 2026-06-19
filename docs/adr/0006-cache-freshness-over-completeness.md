# Cache freshness over completeness

The `HistoricalDataFetcher` decides whether to serve cached bars or call the Provider purely from
a **Freshness window**: a non-empty Cache is trusted when its most recent bar is no older than one
week, measured against the requested end of range (or now, whichever is earlier). It deliberately
does *not* try to prove the Cache is *complete*. We chose this because the original "does the Cache
cover the full requested range?" logic inferred coverage from the timestamps of the bars it
received — and the absence of a bar is ambiguous: it cannot distinguish "no bar exists for this
window" (a weekend, a holiday, a late IPO start, the current incomplete interval) from "this window
was never fetched." That ambiguity made every repeated run re-request a tail (or front) that would
never yield data, which is what tripped Yahoo's HTTP 429 rate limit in offline backtesting.

## Considered options

- **Persist the requested coverage range (a sidecar per symbol+interval).** This is the only thing
  that truly removes the ambiguity — recording that we asked the Provider up to a given date lets us
  tell "asked, nothing there" from "never asked" at both edges. We rejected it: it adds a metadata
  file, a format, and merge/update logic for a workload (offline backtesting, where "the last week
  is good enough") that does not need that precision.

## Consequences

The Cache is deliberately lossy at the edges. A reader should not "fix" these — they are the
trade-off, not bugs:

- The current week's bars are not guaranteed; a fresh Cache is served as-is rather than topped up.
- An earlier `from` is never back-filled into an existing fresh Cache. To widen the start, delete
  the Cache file (`{SYMBOL}_{interval}.csv`) and re-fetch.
- A completed historical window is never re-fetched, because freshness is measured against the
  requested end — so a years-old range stays "fresh" forever. The flip side: a *small* (under a
  week) gap inside such a window is not auto-filled, and a market closure longer than a week
  immediately before the requested end can still re-fetch once per run.
- Coverage is no longer inferred from bar timestamps; `CoversRange` and the interval-stepping
  `AddInterval` helper are gone. Interval validation stays in the Provider, which is the layer that
  knows what an external source supports.
