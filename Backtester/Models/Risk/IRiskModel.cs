using Backtester.Core;

namespace Backtester.Models.Risk
{
    /// <summary>
    /// Evaluates whether an order request is acceptable given current portfolio state.
    /// </summary>
    public interface IRiskModel
    {
        /// <summary>
        /// Returns true if the order should be allowed to proceed, false if it should be rejected.
        /// </summary>
        bool Accept(OrderRequest request, Portfolio portfolio);
    }
}
