using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Data;

namespace Backtester.Core
{
    /// <summary>
    /// Maintains portfolio state including cash, open positions, trades, and equity history.
    /// </summary>
    public class Portfolio
    {
        private readonly List<EquitySnapshot> _equityHistory = new();
        private readonly List<Trade> _trades = new();

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
                Equity = costBasisEquity,
                Positions = Positions.ToList()
            };
        }

        /// <summary>
        /// Applies a filled trade to the portfolio, adjusting cash and creating or updating the relevant position.
        /// </summary>
        public void ApplyTrade(Trade trade)
        {
            _trades.Add(trade);
            if (trade.Side == OrderSide.Buy)
            {
                Cash -= trade.Price * trade.Quantity + trade.Commission;
                Position position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
                if (position == null)
                {
                    position = new Position { Id = Guid.NewGuid().ToString(), Symbol = trade.Symbol };
                    Positions.Add(position);
                }
                position.AddTrade(trade);
            }
            else
            {
                Cash += trade.Price * trade.Quantity - trade.Commission;
                Position position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
                if (position != null)
                {
                    RealizedPnL += (trade.Price - position.AveragePrice) * trade.Quantity;
                    position.AddTrade(trade);
                }
            }
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
                TotalEquity = Cash + unrealized
            });
        }
    }
}
