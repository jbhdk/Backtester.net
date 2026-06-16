namespace Backtester.Models.Sizing
{
    using Backtester.Core;

    public class RiskPercentSizing : ISizingModel
    {
        public decimal RiskPercent { get; set; }

        public int Size(OrderRequest request, Portfolio portfolio)
        {
            if (request.Price is null or 0m) return 0;
            return (int)(portfolio.Cash * RiskPercent / request.Price.Value);
        }
    }
}
