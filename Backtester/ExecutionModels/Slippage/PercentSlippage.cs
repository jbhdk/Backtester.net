using Backtester.Core;

namespace Backtester.ExecutionModels.Slippage
{
    /// <summary>
    /// Applies a percentage-based slippage — buys pay more, sells receive less.
    /// </summary>
    public class PercentSlippage : ISlippageModel
    {
        /// <summary>Gets or sets the slippage rate (e.g. 0.001 for 0.1%).</summary>
        public decimal Percent { get; set; }

        /// <summary>Returns the fill price scaled by one plus or minus the slippage rate.</summary>
        public decimal Apply(decimal price, OrderSide side)
        {
            return side == OrderSide.Buy
                ? price * (1 + Percent)
                : price * (1 - Percent);
        }
    }
}
