using Backtester.Core;

namespace Backtester.ExecutionModels.Slippage
{
    /// <summary>
    /// Applies a fixed price offset as slippage — buys pay more, sells receive less.
    /// </summary>
    public class FixedSlippage : ISlippageModel
    {
        /// <summary>Gets or sets the fixed price amount added on buys and subtracted on sells.</summary>
        public decimal Amount { get; set; }

        /// <summary>Returns the fill price adjusted by the fixed slippage amount.</summary>
        public decimal Apply(decimal price, OrderSide side)
        {
            return side == OrderSide.Buy ? price + Amount : price - Amount;
        }

    }
}
