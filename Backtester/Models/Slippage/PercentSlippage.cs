namespace Backtester.Models.Slippage
{
    public class PercentSlippage : ISlippageModel
    {
        public decimal Percent { get; set; }

        public decimal Apply(decimal price)
        {
            return price * (1 + Percent);
        }
    }
}
