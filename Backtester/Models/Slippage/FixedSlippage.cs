namespace Backtester.Models.Slippage
{
    public class FixedSlippage : ISlippageModel
    {
        public decimal Amount { get; set; }

        public decimal Apply(decimal price)
        {
            return price + Amount;
        }
    }
}
