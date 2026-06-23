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

        // Key: symbol/ticker -> cumulative realized P&L from that symbol's closed trades.
        private readonly Dictionary<string, decimal> _realizedPnLBySymbol = new();

        /// <summary>Gets the cash balance the portfolio started with (its starting equity).</summary>
        public decimal StartingCash { get; }

        /// <summary>Gets the current available cash balance.</summary>
        public decimal Cash { get; private set; }

        /// <summary>Gets the cash amount reserved for pending orders.</summary>
        public decimal ReservedCash { get; private set; }

        /// <summary>Gets the cumulative realized profit/loss from all closed trades.</summary>
        public decimal RealizedPnL { get; private set; }

        /// <summary>Gets the list of all open positions.</summary>
        public List<Position> Positions { get; } = new();

        /// <summary>Gets the chronological series of equity snapshots recorded after each bar.</summary>
        public IReadOnlyList<EquitySnapshot> EquityHistory => _equityHistory;

        /// <summary>Gets the complete trade history in submission order.</summary>
        public IReadOnlyList<Trade> Trades => _trades;

        /// <summary>Initializes a new portfolio with the given starting cash balance.</summary>
        public Portfolio(decimal startingCash)
        {
            StartingCash = startingCash;
            Cash = startingCash;
        }

        /// <summary>
        /// Returns a snapshot of the portfolio's state at the given timestamp using cost-basis equity.
        /// </summary>
        public PortfolioSnapshot SnapshotAt(DateTime timestamp)
        {
            decimal costBasisEquity = Cash + Positions.Sum(p => p.AveragePrice * p.Quantity);
            return new PortfolioSnapshot
            {
                Timestamp = timestamp,
                Cash = Cash,
                CostBasisEquity = costBasisEquity,
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
        public void ApplyTrade(Trade trade)
        {
            Position position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
            int currentQty = position?.Quantity ?? 0;
            int delta = trade.Side == OrderSide.Buy ? trade.Quantity : -trade.Quantity;
            bool isReducing = currentQty != 0 && Math.Sign(delta) != Math.Sign(currentQty);

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

            // A Buy spends cash, a Sell receives it; commission is always a cost.
            decimal cashDirection = trade.Side == OrderSide.Sell ? 1m : -1m;
            Cash += cashDirection * effective.Price * executedQty - effective.Commission;

            if (isReducing)
            {
                decimal tradeRealized = (effective.Price - position.AveragePrice) * Math.Sign(currentQty) * executedQty;
                RealizedPnL += tradeRealized;
                _realizedPnLBySymbol[effective.Symbol] =
                    (_realizedPnLBySymbol.TryGetValue(effective.Symbol, out decimal prior) ? prior : 0m) + tradeRealized;
            }
            else if (position == null)
            {
                position = new Position { Id = Guid.NewGuid().ToString(), Symbol = trade.Symbol };
                Positions.Add(position);
            }

            position.AddTrade(effective);
            _trades.Add(effective);
        }

        /// <summary>
        /// Computes performance statistics by pairing trades into round trips and analysing the equity curve.
        /// </summary>
        public PerformanceStats GetPerformanceStats()
        {
            IReadOnlyList<RoundTrip> roundTrips = PerformanceCalculator.BuildRoundTrips(_trades, _equityHistory);
            return PerformanceCalculator.Calculate(roundTrips, _equityHistory, StartingCash);
        }

        /// <summary>
        /// Computes performance statistics for each traded symbol independently, keyed by symbol.
        /// </summary>
        public IReadOnlyDictionary<string, PerformanceStats> GetPerformanceStatsBySymbol()
        {
            IReadOnlyList<RoundTrip> roundTrips = PerformanceCalculator.BuildRoundTrips(_trades, _equityHistory);
            return PerformanceCalculator.CalculateBySymbol(roundTrips, _equityHistory, StartingCash);
        }

        /// <summary>
        /// Records a mark-to-market equity snapshot using closing prices from the provided market slice.
        /// Falls back to average entry price for symbols not present in the slice.
        /// </summary>
        public void RecordEquitySnapshot(MarketSlice slice)
        {
            decimal unrealized = Positions.Sum(p =>
            {
                decimal markPrice = slice.HasBar(p.Symbol) ? slice.BarsBySymbol[p.Symbol].Close : p.AveragePrice;
                return markPrice * p.Quantity;
            });
            _equityHistory.Add(new EquitySnapshot
            {
                Timestamp = slice.Timestamp,
                Cash = Cash,
                UnrealizedPnL = unrealized,
                RealizedPnL = RealizedPnL,
                MarkedEquity = Cash + unrealized,
                EquityBySymbol = MarkEquityBySymbol(slice)
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
                decimal unrealizedPnL = (markPrice - position.AveragePrice) * position.Quantity;
                decimal realized = _realizedPnLBySymbol.TryGetValue(position.Symbol, out decimal value) ? value : 0m;
                equityBySymbol[position.Symbol] = StartingCash + realized + unrealizedPnL;
            }

            return equityBySymbol;
        }
    }
}
