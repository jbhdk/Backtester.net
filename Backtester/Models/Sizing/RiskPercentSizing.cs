namespace Backtester.Models.Sizing
{
    using Backtester.Core;

    public class RiskPercentSizing : ISizingModel
    {
        public decimal RiskPercent { get; set; }

        public int Size(OrderRequest request, Portfolio portfolio)
        {
            throw new System.NotImplementedException();
        }
    }
}
