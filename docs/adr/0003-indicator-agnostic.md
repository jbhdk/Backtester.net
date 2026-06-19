# Indicator-agnostic engine

> Refined by [ADR 0007](0007-engine-indicator-awareness.md): the engine may now be *aware* of
> indicator series a strategy chooses to expose, while still shipping no indicators and taking no
> indicator-library dependency.


The engine ships no indicators and takes no indicator-library dependency. Instead it exposes bar
**History** to strategies (full history on `OnStart`) so a consumer can compute indicators with
whatever library they prefer — Skender.Stock.Indicators, OoplesFinance.StockIndicators, or
hand-rolled — and bespoke indicators (e.g. the custom trend-strength) live in the consumer too.
We chose this boundary to avoid locking every consumer into one indicator library and one set of
conventions.

## Consequences

- Because indicators are causal, a consumer can precompute series once over the full history and
  read the value aligned to the current bar without lookahead.
- A future contributor should resist the temptation to bundle an indicator package into the
  engine; that belongs in the consuming strategy project.
