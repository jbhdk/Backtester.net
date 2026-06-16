using System.Linq;

namespace Backtester.Models.Risk
{
    using Backtester.Core;

    public class PortfolioRiskModel : IRiskModel
    {
        public decimal MaxPortfolioHeatPercent { get; set; }

        public bool Accept(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price.HasValue)
            {
                var estimatedCost = request.Price.Value * request.Quantity;
                if (estimatedCost > portfolio.Cash) return false;

                var openNotional = portfolio.Positions.Sum(p => p.AveragePrice * p.Quantity);
                var totalEquity = portfolio.Cash + openNotional;
                if (totalEquity > 0)
                {
                    var heatAfter = (openNotional + estimatedCost) / totalEquity;
                    if (heatAfter > MaxPortfolioHeatPercent) return false;
                }
            }
            return true;
        }
    }
}
