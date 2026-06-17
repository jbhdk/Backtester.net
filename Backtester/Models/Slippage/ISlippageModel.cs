using Backtester.Core;

namespace Backtester.Models.Slippage
{
    /// <summary>
    /// Applies a slippage adjustment to a fill price based on order direction.
    /// </summary>
    public interface ISlippageModel
    {
        /// <summary>
        /// Returns the adjusted fill price after applying slippage for the given order side.
        /// </summary>
        decimal Apply(decimal price, OrderSide side);
    }
}
