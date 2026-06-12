using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    public class Portfolio
    {
        public decimal Cash { get; private set; }
        public decimal ReservedCash { get; private set; }
        public List<Position> Positions { get; } = new List<Position>();

        public Portfolio(decimal startingCash)
        {
            Cash = startingCash;
        }

        public PortfolioSnapshot SnapshotAt(DateTime timestamp)
        {
            throw new System.NotImplementedException();
        }

        public void ApplyTrade(Trade trade)
        {
            throw new System.NotImplementedException();
        }
    }
}
