using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Core
{
    /// <summary>
    /// Maintains portfolio state including cash, open positions, trades, and equity history.
    /// </summary>
    public class Portfolio
    {
        private readonly List<EquitySnapshot> _equityHistory = new();
        private readonly List<Trade> _trades = new();
        private readonly List<RoundTrip> _roundTrips = new();

        // Key: symbol/ticker -> cumulative realized P&L from that symbol's closed trades.
        private readonly Dictionary<string, decimal> _realizedPnLBySymbol = new();

        // Key: symbol/ticker -> the most recent close seen for that symbol, used to mark open positions
        // and value new orders for the Reg-T margin gate.
        private readonly Dictionary<string, decimal> _lastCloseBySymbol = new();

        // Owns every conversion declaration and the conversion-rate state behind it, kept apart from
        // _lastCloseBySymbol so marking positions and converting currencies never read each other's state.
        private readonly CurrencyConverter _currencyConverter;

        // Key: symbol/ticker -> a symmetric long/short initial-margin rate overriding the Reg-T split
        // (ADR 0030). Entries exist only for instruments that declare MarginRate; a symbol absent here
        // falls back to LongInitialMarginRate/ShortInitialMarginRate.
        private readonly Dictionary<string, decimal> _marginRateBySymbol = new();

        // Key: symbol/ticker -> the ISO code its prices are quoted in, as its Instrument declared it. The
        // converter cross-checks this at construction and then discards it, so the portfolio keeps its own
        // copy to stamp on each round trip.
        private readonly Dictionary<string, string> _quoteCurrencyBySymbol = new();

        /// <summary>Gets the cash balance the portfolio started with (its starting equity).</summary>
        public decimal StartingCash { get; }

        /// <summary>
        /// Gets the ISO currency code the portfolio's cash and equity are denominated in. A position whose
        /// Instrument quotes in a different currency is converted into this currency for reporting.
        /// </summary>
        public string AccountCurrency { get; }

        /// <summary>Gets the current available cash balance.</summary>
        public decimal Cash { get; private set; }

        /// <summary>Gets the cash amount reserved for pending orders.</summary>
        public decimal ReservedCash { get; private set; }

        /// <summary>Gets the cumulative realized profit/loss from all closed trades.</summary>
        public decimal RealizedPnL { get; private set; }

        /// <summary>Gets or sets the Reg-T initial-margin rate for long positions (default 0.5 = 2:1 leverage).</summary>
        public decimal LongInitialMarginRate { get; set; } = 0.5m;

        /// <summary>Gets or sets the Reg-T initial-margin rate for short positions (default 1.5).</summary>
        public decimal ShortInitialMarginRate { get; set; } = 1.5m;

        /// <summary>
        /// Gets the account's marked-to-market equity: cash plus open positions valued at their latest
        /// close (falling back to average entry price for symbols not yet marked), converted into
        /// AccountCurrency for any position whose Instrument quotes in a different currency (ADR 0029).
        /// </summary>
        public decimal MarkedEquity => Cash + Positions.Sum(ConvertedMarketValue);

        /// <summary>
        /// Gets the account's realized (cost-basis) equity: cash plus each open position's cost basis at
        /// its volume-weighted average entry price, converted into AccountCurrency for any position whose
        /// Instrument quotes in a different currency (ADR 0032). Excludes unrealized P&amp;L, so it equals
        /// cash when flat. The base risk-per-trade sizing budgets against.
        /// </summary>
        public decimal RealizedEquity => Cash + Positions.Sum(ConvertedCostBasis);

        /// <summary>
        /// Gets the initial margin committed by open positions, in AccountCurrency: each position's latest
        /// market value (converted) times its side's initial-margin rate, always a non-negative (gross)
        /// amount.
        /// </summary>
        public decimal CommittedMargin =>
            Positions.Sum(p => MarginRate(p.Symbol, p.Quantity) * Math.Abs(ConvertedMarketValue(p)));

        /// <summary>
        /// Gets the marked equity available above the initial margin already committed by open positions.
        /// A new opening order is acceptable only if its initial margin does not exceed this.
        /// </summary>
        public decimal BuyingPower => MarkedEquity - CommittedMargin;

        /// <summary>
        /// Returns the initial margin an order would commit. A reducing order (opposite to the open
        /// position) commits none; otherwise it is <c>rate · |price · quantity|</c>, valuing the order at
        /// its own price or, lacking one, the symbol's latest close. Returns zero when the order cannot be
        /// valued, so it is not gated.
        /// </summary>
        internal decimal InitialMarginForOrder(OrderRequest request)
        {
            Position position = Positions.FirstOrDefault(p => p.Symbol == request.Symbol);
            int currentQty = position?.Quantity ?? 0;
            if (IsReducing(currentQty, SignedDelta(request.Side, request.Quantity)))
            {
                return 0m;
            }

            decimal price = ValuationPriceForOrder(request);
            if (price <= 0m)
            {
                return 0m;
            }

            decimal rate = MarginRate(request.Symbol, request.Side == OrderSide.Buy ? 1 : -1);
            return rate * ToAccountCurrency(request.Symbol, price * request.Quantity);
        }

        /// <summary>
        /// Returns the signed quantity of the open position for a symbol — positive long, negative short,
        /// zero when flat — so a caller can size a closing order to the position it closes.
        /// </summary>
        internal int OpenQuantity(string symbol)
        {
            Position position = Positions.FirstOrDefault(p => p.Symbol == symbol);
            return position?.Quantity ?? 0;
        }

        /// <summary>
        /// Returns true when the order's side opposes the open position for its symbol, so filling it would
        /// reduce or close that position rather than open or add to one. Classified by side alone, so a
        /// not-yet-sized flatten (quantity zero) is still recognised as reducing.
        /// </summary>
        internal bool ReducesOpenPosition(OrderRequest request)
        {
            int currentQuantity = OpenQuantity(request.Symbol);
            return currentQuantity != 0
                && (request.Side == OrderSide.Buy ? currentQuantity < 0 : currentQuantity > 0);
        }

        /// <summary>
        /// Returns the price used to value an order: the order's own price, or the symbol's latest close
        /// when it has none, or zero when neither is known (so the order cannot be valued).
        /// </summary>
        internal decimal ValuationPriceForOrder(OrderRequest request)
        {
            return request.Price
                ?? (_lastCloseBySymbol.TryGetValue(request.Symbol, out decimal close) ? close : 0m);
        }

        private decimal MarkPrice(Position position)
        {
            return _lastCloseBySymbol.TryGetValue(position.Symbol, out decimal close) ? close : position.AveragePrice;
        }

        /// <summary>
        /// Returns a position's signed market value (latest mark price times quantity) converted into
        /// AccountCurrency, the shared basis for MarkedEquity and CommittedMargin so both stay denominated
        /// in the same currency for a cross-currency Instrument.
        /// </summary>
        private decimal ConvertedMarketValue(Position position)
        {
            return ToAccountCurrency(position.Symbol, MarkPrice(position) * position.Quantity);
        }

        /// <summary>
        /// Returns a position's signed cost basis (average entry price times quantity) converted into
        /// AccountCurrency — the same shape as <see cref="ConvertedMarketValue"/> but valued at what the
        /// position cost rather than what it is now worth, so RealizedEquity and MarkedEquity share
        /// denomination and differ only by unrealized P&amp;L.
        /// </summary>
        private decimal ConvertedCostBasis(Position position)
        {
            return ToAccountCurrency(position.Symbol, position.AveragePrice * position.Quantity);
        }

        /// <summary>
        /// Returns the initial-margin rate for a symbol and side: the Instrument's own MarginRate,
        /// applied symmetrically to both long and short, when declared (ADR 0030); otherwise the
        /// Reg-T LongInitialMarginRate/ShortInitialMarginRate split.
        /// </summary>
        private decimal MarginRate(string symbol, int quantity)
        {
            if (_marginRateBySymbol.TryGetValue(symbol, out decimal rate))
            {
                return rate;
            }

            return quantity >= 0 ? LongInitialMarginRate : ShortInitialMarginRate;
        }

        /// <summary>
        /// Returns the currency a symbol's prices are quoted in: its Instrument's declared quote currency,
        /// or <see cref="AccountCurrency"/> for a symbol that declares no Instrument — which is exactly the
        /// currency such a symbol is already converted as being in, the converter's identity case.
        /// </summary>
        private string QuoteCurrencyOf(string symbol)
        {
            return _quoteCurrencyBySymbol.TryGetValue(symbol, out string quoteCurrency)
                ? quoteCurrency
                : AccountCurrency;
        }

        /// <summary>Converts an order side and quantity into a signed change in position quantity.</summary>
        private static int SignedDelta(OrderSide side, int quantity)
        {
            return side == OrderSide.Buy ? quantity : -quantity;
        }

        /// <summary>Returns true when a fill opposes the open position, i.e. it reduces rather than opens or adds.</summary>
        private static bool IsReducing(int currentQuantity, int delta)
        {
            return currentQuantity != 0 && Math.Sign(delta) != Math.Sign(currentQuantity);
        }

        /// <summary>
        /// Maps the exit fill's bracket leg to the round trip's exit reason: a stop leg closed it at its
        /// stop-loss, a target leg at its take-profit, and any non-bracket exit is a strategy signal.
        /// </summary>
        private static ExitReason ExitReasonFor(BracketLeg leg)
        {
            return leg switch
            {
                BracketLeg.StopLoss   => ExitReason.StopLoss,
                BracketLeg.TakeProfit => ExitReason.TakeProfit,
                _                     => ExitReason.Signal
            };
        }

        /// <summary>Gets the list of all open positions.</summary>
        internal List<Position> Positions { get; } = new();

        /// <summary>Gets the chronological series of equity snapshots recorded after each bar.</summary>
        internal IReadOnlyList<EquitySnapshot> EquityHistory => _equityHistory;

        /// <summary>Gets the complete trade history in submission order.</summary>
        internal IReadOnlyList<Trade> Trades => _trades;

        /// <summary>
        /// Gets the round trips realized so far, in the order they closed. Each reducing fill that closes
        /// or partially closes a position appends one; the Portfolio is their single source of truth.
        /// </summary>
        internal IReadOnlyList<RoundTrip> RoundTrips => _roundTrips;

        /// <summary>
        /// Initializes a new portfolio with the given starting cash balance, denominated in
        /// <paramref name="accountCurrency"/> (defaulting to <c>"USD"</c> so every existing call site is
        /// unaffected). <paramref name="instruments"/> declares, for any symbol quoted in a currency other
        /// than <paramref name="accountCurrency"/>, which series to convert it through (ADR 0029); null or
        /// omitted when every traded symbol already quotes in the account's own currency.
        /// </summary>
        public Portfolio(decimal startingCash, string accountCurrency = "USD", Instrument[] instruments = null)
        {
            StartingCash = startingCash;
            Cash = startingCash;
            AccountCurrency = accountCurrency;
            _currencyConverter = new CurrencyConverter(accountCurrency, instruments);

            if (instruments != null)
            {
                foreach (Instrument instrument in instruments)
                {
                    if (instrument.MarginRate.HasValue)
                    {
                        _marginRateBySymbol[instrument.Symbol] = instrument.MarginRate.Value;
                    }

                    _quoteCurrencyBySymbol[instrument.Symbol] = instrument.QuoteCurrency;
                }
            }
        }

        /// <summary>
        /// Gets the distinct Conversion symbols the portfolio's Instruments declare — the extra series a
        /// run must fetch on top of its tradable symbols so every cross-currency position can be valued in
        /// <see cref="AccountCurrency"/>. Empty when every traded symbol already quotes in that currency.
        /// </summary>
        internal IReadOnlyCollection<string> ConversionSymbols => _currencyConverter.ConversionSymbols;

        /// <summary>
        /// Returns <paramref name="nativeAmount"/> (denominated in <paramref name="symbol"/>'s own quote
        /// currency) converted into <see cref="AccountCurrency"/> by the portfolio's
        /// <see cref="CurrencyConverter"/>, which owns the conversion rule and its rate state.
        /// A seam for callers outside Portfolio but inside the engine (e.g. risk-based sizing models) that
        /// must convert a quote-currency-denominated amount, such as a stop distance, into the same units
        /// as an account-currency-denominated budget before dividing (ADR 0029).
        /// </summary>
        internal decimal ToAccountCurrency(string symbol, decimal nativeAmount)
        {
            return _currencyConverter.ToAccountCurrency(symbol, nativeAmount);
        }

        /// <summary>
        /// Returns a snapshot of the portfolio's state at the given timestamp using cost-basis equity.
        /// </summary>
        internal PortfolioSnapshot SnapshotAt(DateTime timestamp)
        {
            return new PortfolioSnapshot
            {
                Timestamp = timestamp,
                Cash = Cash,
                CostBasisEquity = RealizedEquity,
                Positions = Positions.ToList()
            };
        }

        /// <summary>
        /// Applies a filled trade to the portfolio, adjusting cash and creating or updating the relevant
        /// position. Quantity is signed: a Sell from flat opens a short and credits cash by its proceeds,
        /// a Buy covers a short and debits cash. A fill that opposes the open position reduces it and is
        /// clamped at zero (overshoot discarded) so a single fill never flips the position's sign; on a
        /// reduction it realizes <c>(price − averagePrice) · sign(quantity) · closedQuantity</c>.
        /// </summary>
        internal void ApplyTrade(Trade trade)
        {
            Position position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
            int currentQty = position?.Quantity ?? 0;
            bool isReducing = IsReducing(currentQty, SignedDelta(trade.Side, trade.Quantity));

            int executedQty = isReducing ? Math.Min(trade.Quantity, Math.Abs(currentQty)) : trade.Quantity;
            if (executedQty == 0)
            {
                return;
            }

            Trade effective = executedQty == trade.Quantity ? trade : new Trade
            {
                Id = trade.Id,
                Symbol = trade.Symbol,
                Side = trade.Side,
                Price = trade.Price,
                Quantity = executedQty,
                Commission = trade.Commission,
                Slippage = trade.Slippage,
                Timestamp = trade.Timestamp
            };

            // A Buy spends cash, a Sell receives it; commission is always a cost. The notional is native to
            // the symbol's quote currency and converted into AccountCurrency before touching Cash; commission
            // is already account-currency-denominated and never converted (ADR 0029).
            decimal cashDirection = trade.Side == OrderSide.Sell ? 1m : -1m;
            decimal notionalAccountCurrency = ToAccountCurrency(effective.Symbol, effective.Price * executedQty);
            Cash += cashDirection * notionalAccountCurrency - effective.Commission;

            if (isReducing)
            {
                // Native (quote-currency) gain/loss, converted into AccountCurrency: a live trading platform
                // shows the native entry/exit price alongside account-currency PnL (ADR 0029), so RealizedPnL
                // here and on the RoundTrip below is converted while EntryPrice/ExitPrice stay native.
                decimal tradeRealizedNative = (effective.Price - position.AveragePrice) * Math.Sign(currentQty) * executedQty;
                decimal tradeRealized = ToAccountCurrency(effective.Symbol, tradeRealizedNative);
                RealizedPnL += tradeRealized;
                _realizedPnLBySymbol[effective.Symbol] =
                    (_realizedPnLBySymbol.TryGetValue(effective.Symbol, out decimal prior) ? prior : 0m) + tradeRealized;

                // This slice's share of the Account-currency capital the lot committed, accumulated fill by
                // fill at each fill's own rate — the numerator for the trip's leverage (ADR 0032). Taken
                // pro-rata and multiplied before dividing, so successive partial exits divide one lot's
                // cost basis between them rather than each claiming the whole of it.
                decimal exitedNotional = position.EntryNotional * executedQty / Math.Abs(currentQty);
                position.EntryNotional -= exitedNotional;

                _roundTrips.Add(new RoundTrip
                {
                    Symbol      = effective.Symbol,
                    Direction   = currentQty > 0 ? PositionDirection.Long : PositionDirection.Short,
                    EntryPrice  = position.AveragePrice,
                    ExitPrice   = effective.Price,
                    Quantity    = executedQty,
                    RealizedPnL = tradeRealized,
                    // The frozen per-share entry stop distance scaled to this exited slice; null (no
                    // R-multiple) when the opening entry declared no protective stop.
                    InitialRisk = position.EntryStopDistance.HasValue ? position.EntryStopDistance.Value * executedQty : (decimal?)null,
                    // The initial stop and target levels frozen at open, carried straight through so the
                    // report shows the entry setup without reconstructing it from the bracket ledger.
                    EntryStopPrice   = position.EntryStopPrice,
                    EntryTargetPrice = position.EntryTargetPrice,
                    BarsHeld    = Math.Max(0, _equityHistory.Count - position.EntryBarIndex),
                    EntryNotional = exitedNotional,
                    // The margin that notional committed, at the portfolio's own rate for this symbol and
                    // side — so an Instrument declaring its own rate reports that rather than Reg-T (ADR
                    // 0032). Stamped here so no consumer has to know the rule, let alone re-derive it.
                    EntryMargin = MarginRate(effective.Symbol, currentQty) * exitedNotional,
                    // The marked equity captured when this lot opened, the denominator for the trip's leverage.
                    EntryEquity = position.EntryEquity,
                    // The currency the entry and exit prices above are quoted in, so a mixed report can
                    // say what its native price columns mean (ADR 0032).
                    QuoteCurrency = QuoteCurrencyOf(effective.Symbol),
                    EntryTime   = position.EntryTime,
                    ExitTime    = effective.Timestamp,
                    ExitReason  = ExitReasonFor(effective.Leg)
                });
            }
            else if (position == null)
            {
                position = new Position { Id = Guid.NewGuid().ToString(), Symbol = trade.Symbol };
                Positions.Add(position);
            }

            // Opening from flat (a new position or a reused one that had reduced to zero): capture the
            // entry context the round trip will carry. A reduction never reaches here, so a partial exit
            // leaves the original entry time and bar index intact for the remainder.
            if (currentQty == 0)
            {
                position.EntryTime = effective.Timestamp;
                position.EntryBarIndex = _equityHistory.Count;
                // Freeze the per-share initial risk from the entry stop declared on this opening fill,
                // translated into AccountCurrency at the rate in force at this moment — what a broker would
                // have said was at risk as the position opened (ADR 0032). A later trailed stop, a scale-in
                // at a different stop, and a later rate move all leave it alone.
                position.EntryStopDistance = effective.EntryStopPrice.HasValue
                    ? ToAccountCurrency(effective.Symbol, Math.Abs(effective.Price - effective.EntryStopPrice.Value))
                    : (decimal?)null;
                // Freeze the raw initial stop and target levels from the opening fill, so the round trip
                // carries the entry setup (the levels before any trailing) for the report.
                position.EntryStopPrice = effective.EntryStopPrice;
                position.EntryTargetPrice = effective.EntryTargetPrice;
                // Start the lot's cost basis from nothing: a reused position that had reduced to zero must
                // not carry the previous lot's committed capital into this one.
                position.EntryNotional = 0m;
            }

            if (!isReducing)
            {
                // An opening or adding fill commits capital, so the lot's cost basis grows by the very
                // amount that just left Cash — already converted at this fill's own rate (ADR 0032).
                position.EntryNotional += notionalAccountCurrency;
            }

            position.AddTrade(effective);
            _trades.Add(effective);

            // Capture the marked equity at the opening bar once the fill is applied (so the new position is
            // included in the mark). A partial exit never reaches here, so the remainder keeps this value.
            if (currentQty == 0)
            {
                position.EntryEquity = MarkedEquity;
            }
        }

        /// <summary>
        /// Computes performance statistics by pairing trades into round trips and analysing the equity curve.
        /// </summary>
        internal PerformanceStats GetPerformanceStats()
        {
            return PerformanceCalculator.Calculate(_roundTrips, _equityHistory, StartingCash);
        }

        /// <summary>
        /// Computes performance statistics for each traded symbol independently, keyed by symbol.
        /// </summary>
        internal IReadOnlyDictionary<string, PerformanceStats> GetPerformanceStatsBySymbol()
        {
            return PerformanceCalculator.CalculateBySymbol(_roundTrips, _equityHistory, StartingCash);
        }

        /// <summary>
        /// Records a mark-to-market equity snapshot using closing prices from the provided market slice.
        /// Falls back to average entry price for symbols not present in the slice.
        /// </summary>
        internal void RecordEquitySnapshot(MarketSlice slice)
        {
            // The one place the two stores are fed, each from the same bars but for its own purpose: every
            // close becomes the symbol's mark price, and a close on a declared Conversion symbol is also
            // observed as that series' rate. A symbol that is both traded and another's conversion series
            // therefore feeds both, and neither store is ever read as if it were the other.
            foreach (KeyValuePair<string, Candle> bar in slice.BarsBySymbol)
            {
                if (bar.Value is null)
                {
                    continue;
                }

                _lastCloseBySymbol[bar.Key] = bar.Value.Close;

                if (_currencyConverter.ConversionSymbols.Contains(bar.Key))
                {
                    _currencyConverter.ObserveRate(bar.Key, bar.Value.Close);
                }
            }

            // Key: symbol/ticker -> the signed market value of its open position at this slice, converted
            // into AccountCurrency for a cross-currency Instrument (ADR 0029).
            Dictionary<string, decimal> positionValueBySymbol = new(Positions.Count);
            // Key: symbol/ticker -> the initial margin its open position commits at this slice (gross),
            // in AccountCurrency.
            Dictionary<string, decimal> heldMarginBySymbol = new(Positions.Count);
            decimal unrealized = 0m;
            foreach (Position position in Positions)
            {
                decimal markPrice = slice.HasBar(position.Symbol) ? slice.BarsBySymbol[position.Symbol].Close : position.AveragePrice;
                decimal value = ToAccountCurrency(position.Symbol, markPrice * position.Quantity);
                positionValueBySymbol[position.Symbol] = value;
                heldMarginBySymbol[position.Symbol] = MarginRate(position.Symbol, position.Quantity) * Math.Abs(value);
                unrealized += value;
            }

            _equityHistory.Add(new EquitySnapshot
            {
                Timestamp = slice.Timestamp,
                Cash = Cash,
                UnrealizedPnL = unrealized,
                RealizedPnL = RealizedPnL,
                MarkedEquity = Cash + unrealized,
                HeldInitialMargin = CommittedMargin,
                EquityBySymbol = MarkEquityBySymbol(slice),
                PositionValueBySymbol = positionValueBySymbol,
                HeldMarginBySymbol = heldMarginBySymbol
            });
        }

        /// <summary>
        /// Builds each traded symbol's isolated equity at the given slice: starting capital plus the
        /// symbol's own realized P&amp;L to date and the unrealized P&amp;L of its open position marked at
        /// the slice's close. Symbols with neither realized P&amp;L nor an open position are omitted (their
        /// isolated equity is unchanged at starting capital).
        /// </summary>
        private IReadOnlyDictionary<string, decimal> MarkEquityBySymbol(MarketSlice slice)
        {
            // Key: symbol/ticker -> the symbol's isolated marked equity at this slice.
            Dictionary<string, decimal> equityBySymbol = new();

            foreach (KeyValuePair<string, decimal> realized in _realizedPnLBySymbol)
            {
                equityBySymbol[realized.Key] = StartingCash + realized.Value;
            }

            foreach (Position position in Positions)
            {
                decimal markPrice = slice.HasBar(position.Symbol) ? slice.BarsBySymbol[position.Symbol].Close : position.AveragePrice;
                decimal unrealizedPnLNative = (markPrice - position.AveragePrice) * position.Quantity;
                decimal unrealizedPnL = ToAccountCurrency(position.Symbol, unrealizedPnLNative);
                decimal realized = _realizedPnLBySymbol.TryGetValue(position.Symbol, out decimal value) ? value : 0m;
                equityBySymbol[position.Symbol] = StartingCash + realized + unrealizedPnL;
            }

            return equityBySymbol;
        }
    }
}
