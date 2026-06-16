namespace Backtester.Models.Slippage
{
    using Backtester.Core;

    public class PercentSlippage : ISlippageModel
    {
        public decimal Percent { get; set; }

        public decimal Apply(decimal price, OrderSide side) =>
            side == OrderSide.Buy
                ? price * (1 + Percent)
                : price * (1 - Percent);
    }
}
