using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Determines the position size (number of shares or contracts) for an order.
    /// </summary>
    public interface ISizingModel
    {
        /// <summary>
        /// Returns the quantity to use for the given order request and current portfolio state.
        /// </summary>
        int Size(OrderRequest request, Portfolio portfolio);
    }
}
