using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// An immutable snapshot of portfolio state passed to strategies on each bar.
    /// </summary>
    public class PortfolioSnapshot
    {
        /// <summary>Gets or sets the UTC timestamp of this snapshot.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the available cash at this point in time.</summary>
        public decimal Cash { get; set; }

        /// <summary>Gets or sets the total cost-basis equity (cash plus open position notional).</summary>
        public decimal Equity { get; set; }

        /// <summary>Gets or sets the list of open positions at this point in time.</summary>
        public IReadOnlyList<Position> Positions { get; set; }
    }
}
