# Margin-account shorting (Reg-T initial margin)

Supersedes [ADR 0004](0004-long-only.md), which deferred shorts because of the missing short
cash/margin accounting. We now model shorting on a Reg-T **margin account**. `Position.Quantity`
becomes signed — positive long, negative short, zero flat — and the same buy/sell mechanics work
both directions: a Sell from flat opens a short, a Buy covers it, and realized PnL on a short close
is `(entry − exit)·quantity`, the mirror of a long.

Accounting is mark-to-market. A short sale credits cash by its full proceeds; the margin it requires
is *held* against buying power rather than debited from cash, so marked equity (`Cash + Σ
Close·SignedQty`) stays correct through entry and mark-out. Order acceptance is gated by **initial**
margin: an order is accepted only if `rate · |price·quantity| ≤ MarkedEquity − Σ(initial margin of
open positions)`, with rate 0.5 for longs and 1.5 for shorts. This gate is intrinsic to the account
and always enforced — it is not a pluggable execution model — so the Reg-T model can never be
silently switched off. The previously cash-only long path therefore also becomes a 2:1 margin path.

## Considered options

- **Credit proceeds, no margin** (unlimited leverage) — simplest correct ledger, but lets a strategy
  take unbounded short/long exposure, which misrepresents achievable returns. Rejected for realism.
- **Maintenance margin with forced liquidation** — the engine would track a maintenance line and
  synthesize anti-lookahead exit orders on a breach. A large new engine behaviour (liquidation
  policy, fill timing, ordering against strategy orders). Deferred; would warrant its own ADR.
- **Shorts-only margin, longs stay 100% cash** — strictly additive, no change to existing long
  behaviour, but an incoherent half-cash/half-margin account. Rejected for consistency.
- **Allow a single fill to flip the sign** (close long + open short in one order) — matches broker
  reality and enables one-bar reversal, but a single fill would both close one round trip and open
  the opposite, complicating the ledger. Rejected in favour of the no-flip invariant below.

## Consequences

- **No single fill flips the sign of a position.** An order opposite to the open position reduces it
  and clamps at zero; any overshoot is discarded. Reversing direction takes a second order from flat,
  which under next-bar fill lands at least one bar later.
- **No maintenance margin, no margin calls.** A runaway short can drive marked equity below the
  maintenance line — even negative — with no intervention; the backtest reports it as drawdown.
- **`PortfolioRiskModel` is removed.** Its `estimatedCost > Cash` check contradicts margin leverage
  and its net-signed heat formula breaks for shorts; intrinsic Reg-T margin is now the sole gate.
- **Buying power, average price, realized-PnL, and round-trip pairing all generalize to signed
  quantity.** `Position`, `Portfolio`, and `PerformanceCalculator` change accordingly; the OHLC fill
  model and risk-per-trade sizing were already side-agnostic.
