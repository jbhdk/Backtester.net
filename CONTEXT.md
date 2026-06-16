Project context (placeholder)

Backtester.net is a small, in-memory backtesting engine for equity/time-series strategies. Key concepts:

- Symbols: string identifiers representing tradable instruments (CSV data under `samples/data`).
- Candle: OHLCV bar with a UTC `Timestamp` used as the single time cursor for the engine.
- Engine: advances a market-data feed and invokes strategy logic on synchronized `MarketSlice` snapshots.
- Portfolio: tracks positions, cash, P&L and exposes snapshots and performance stats.

Add domain conventions, terminology, and constraints here to help agent-driven workflows make domain-aware suggestions.
