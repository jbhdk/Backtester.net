using System;
using System.Linq;
using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions so that a stop-out loses a fixed fraction of realized (cost-basis) equity.
    /// Formula: shares = floor(RiskFraction * realizedEquity / stopDistance), where the per-share stop
    /// distance is the fill-relative <see cref="OrderRequest.StopOffset"/> when set (a bracket entry whose
    /// absolute stop is not yet known), else the absolute <c>|entryPrice - stopPrice|</c>.
    /// Returns zero when no stop distance is available.
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
            decimal stopDistance = StopDistanceFor(request);
            if (stopDistance <= 0m)
            {
                return 0;
            }

            decimal realizedEquity = portfolio.Cash + portfolio.Positions.Sum(p => p.AveragePrice * p.Quantity);
            return (int)(RiskFraction * realizedEquity / stopDistance);
        }

        /// <summary>
        /// Resolves the per-share stop distance the size divides into: the fill-relative
        /// <see cref="OrderRequest.StopOffset"/> when set (a bracket entry whose absolute stop resolves
        /// against the fill later, so no absolute anchor exists at submit time), else the absolute distance
        /// <c>|Price - StopPrice|</c>. Returns zero when neither is available, so the caller sizes nothing
        /// rather than risking an unknown amount.
        /// </summary>
        private static decimal StopDistanceFor(OrderRequest request)
        {
            if (request.StopOffset is decimal offset && offset > 0m)
            {
                return offset;
            }

            if (request.Price is null or 0m || request.StopPrice is null)
            {
                return 0m;
            }

            return Math.Abs(request.Price.Value - request.StopPrice.Value);
        }
    }
}
