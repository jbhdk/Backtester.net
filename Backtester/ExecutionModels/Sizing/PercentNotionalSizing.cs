using System;
using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions by allocating a fixed percentage of the account's buying power per trade.
    /// </summary>
    public class PercentNotionalSizing : ISizingModel
    {
        /// <summary>Gets or sets the fraction of buying power to allocate per trade (e.g. 0.10 for 10%).</summary>
        public decimal Percent { get; set; }

        /// <summary>
        /// Returns the number of shares that fit within the buying-power allocation at the order's price.
        /// Returns zero if no price is set or the allocation cannot afford a single share, including once
        /// buying power is exhausted (<c>&lt;= 0</c>), so no new positions open past that point.
        /// </summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price is null or 0m)
            {
                return 0;
            }

            // Allocate from buying power so the model keeps deploying capital on margin, but floor the base
            // at zero: once buying power is exhausted there is nothing left to open with, and flooring here
            // keeps the share count non-negative so no negative quantity can corrupt Position/RoundTrip state.
            decimal allocatable = Math.Max(0m, portfolio.BuyingPower);
            return (int)(allocatable * Percent / request.Price.Value);
        }
    }
}
