using System;
using System.Linq;
using Backtester.Core;

namespace Backtester.Models.Sizing
{
    /// <summary>
    /// Sizes positions so that a stop-out loses a fixed fraction of realized (cost-basis) equity.
    /// Formula: shares = floor(RiskFraction * realizedEquity / |entryPrice - stopPrice|).
    /// Returns zero when Price, StopPrice, or stop distance is missing.
    /// </summary>
    public class RiskPerTradeSizing : ISizingModel
    {
        /// <summary>Gets or sets the fraction of realized equity to risk per trade (e.g. 0.01 for 1%).</summary>
        public decimal RiskFraction { get; set; }

        /// <summary>
        /// Returns the number of shares that limits the stop-out loss to <see cref="RiskFraction"/> of realized equity.
        /// </summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price is null or 0m)
            {
                return 0;
            }


            if (request.StopPrice is null)
            {
                return 0;
            }


            decimal stopDistance = Math.Abs(request.Price.Value - request.StopPrice.Value);
            if (stopDistance == 0m)
            {
                return 0;
            }


            decimal realizedEquity = portfolio.Cash + portfolio.Positions.Sum(p => p.AveragePrice * p.Quantity);
            return (int)(RiskFraction * realizedEquity / stopDistance);
        }
    }
}
