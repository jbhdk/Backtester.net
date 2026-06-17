using System.Linq;
using Backtester.Core;

namespace Backtester.Models.Risk
{
    /// <summary>
    /// Rejects orders that would exceed available cash or push portfolio heat above a configured threshold.
    /// </summary>
    public class PortfolioRiskModel : IRiskModel
    {
        /// <summary>
        /// Gets or sets the maximum fraction of total equity that may be deployed in open positions (e.g. 0.5 for 50%).
        /// </summary>
        public decimal MaxPortfolioHeatPercent { get; set; }

        /// <summary>
        /// Returns false if the order would exceed available cash or breach the maximum portfolio heat limit.
        /// </summary>
        public bool Accept(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price.HasValue)
            {
                decimal estimatedCost = request.Price.Value * request.Quantity;
                if (estimatedCost > portfolio.Cash) return false;

                decimal openNotional = portfolio.Positions.Sum(p => p.AveragePrice * p.Quantity);
                decimal totalEquity = portfolio.Cash + openNotional;
                if (totalEquity > 0)
                {
                    decimal heatAfter = (openNotional + estimatedCost) / totalEquity;
                    if (heatAfter > MaxPortfolioHeatPercent) return false;
                }
            }
            return true;
        }
    }
}
