using System;

namespace Backtester.Core
{
    public class EquitySnapshot
    {
        public DateTime Timestamp { get; set; }
        public decimal Cash { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public decimal RealizedPnL { get; set; }
        public decimal TotalEquity { get; set; }
    }
}
