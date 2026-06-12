using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    public class PortfolioSnapshot
    {
        public DateTime Timestamp { get; set; }
        public decimal Cash { get; set; }
        public decimal Equity { get; set; }
        public IReadOnlyList<Position> Positions { get; set; }
    }
}
