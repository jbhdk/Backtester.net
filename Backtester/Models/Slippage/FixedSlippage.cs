namespace Backtester.Models.Slippage
{
    using Backtester.Core;

    public class FixedSlippage : ISlippageModel
    {
        public decimal Amount { get; set; }

        public decimal Apply(decimal price, OrderSide side) =>
            side == OrderSide.Buy ? price + Amount : price - Amount;
    }
}
