using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Strategy-facing broker interface for submitting and managing orders during a backtest.
    /// </summary>
    public interface IBroker
    {
        /// <summary>
        /// Queues a single order for fill processing. Returns the assigned order ID, or null if rejected.
        /// </summary>
        string Submit(OrderRequest request);

        /// <summary>
        /// Queues an entry order with attached stop-loss and take-profit. Returns a handle whose
        /// StopOrderId and TargetOrderId are populated once the entry fills.
        /// </summary>
        BracketHandle SubmitBracket(BracketRequest request);

        /// <summary>
        /// Removes a working order from the book so it will never fill. No-ops if already filled or unknown.
        /// </summary>
        void Cancel(string orderId);

        /// <summary>
        /// Updates the trigger price of a working order. No-ops if already filled or unknown.
        /// </summary>
        void Modify(string orderId, decimal newPrice);
    }
}
