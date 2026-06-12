namespace Backtester.Models.Risk
{
    using Backtester.Core;

    public class PortfolioRiskModel : IRiskModel
    {
        public decimal MaxPortfolioHeatPercent { get; set; }

        public bool Accept(OrderRequest request, Portfolio portfolio)
        {
            throw new System.NotImplementedException();
        }
    }
}
