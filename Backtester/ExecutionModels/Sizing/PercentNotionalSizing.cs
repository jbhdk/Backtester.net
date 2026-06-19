using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions by allocating a fixed percentage of available cash per trade.
    /// </summary>
    public class PercentNotionalSizing : ISizingModel
    {
        /// <summary>Gets or sets the fraction of cash to allocate per trade (e.g. 0.10 for 10%).</summary>
        public decimal Percent { get; set; }

        /// <summary>
        /// Returns the number of shares that fit within the cash allocation at the order's price.
        /// Returns zero if no price is set or the allocation cannot afford a single share.
        /// </summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price is null or 0m)
            {
                return 0;
            }

            return (int)(portfolio.Cash * Percent / request.Price.Value);
        }
    }
}
