using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Core
{
    using Backtester.Data;

    public class Portfolio
    {
        private readonly List<EquitySnapshot> _equityHistory = new();
        private readonly List<Trade> _trades = new();

        public decimal Cash { get; private set; }
        public decimal ReservedCash { get; private set; }
        public decimal RealizedPnL { get; private set; }
        public List<Position> Positions { get; } = new();
        public IReadOnlyList<EquitySnapshot> EquityHistory => _equityHistory;
        public IReadOnlyList<Trade> Trades => _trades;

        public Portfolio(decimal startingCash)
        {
            Cash = startingCash;
        }

        public PortfolioSnapshot SnapshotAt(DateTime timestamp)
        {
            var costBasisEquity = Cash + Positions.Sum(p => p.AveragePrice * p.Quantity);
            return new PortfolioSnapshot
            {
                Timestamp = timestamp,
                Cash = Cash,
                Equity = costBasisEquity,
                Positions = Positions.ToList()
            };
        }

        public void ApplyTrade(Trade trade)
        {
            _trades.Add(trade);
            if (trade.Side == OrderSide.Buy)
            {
                Cash -= trade.Price * trade.Quantity + trade.Commission;
                var position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
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
                var position = Positions.FirstOrDefault(p => p.Symbol == trade.Symbol);
                if (position != null)
                {
                    RealizedPnL += (trade.Price - position.AveragePrice) * trade.Quantity;
                    position.AddTrade(trade);
                }
            }
        }

        public void RecordEquitySnapshot(MarketSlice slice)
        {
            var unrealized = Positions.Sum(p =>
            {
                var markPrice = slice.HasBar(p.Symbol) ? slice.BarsBySymbol[p.Symbol].Close : p.AveragePrice;
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
