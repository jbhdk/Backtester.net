using System.Collections.Generic;
using Backtester.Core;
using Backtester.Data;

namespace Backtester.Broker
{
    /// <summary>
    /// Simulates order submission and fill processing during a backtest.
    /// </summary>
    public interface IBrokerSimulator
    {
        /// <summary>
        /// Queues an order for fill processing on the next bar. Returns the assigned order ID, or null if rejected.
        /// </summary>
        string SubmitOrder(OrderRequest request);

        /// <summary>
        /// Matches all working orders against the current market slice and returns the resulting trades.
        /// Unfilled orders remain working (GTC) and are evaluated again on subsequent bars.
        /// </summary>
        IEnumerable<Trade> ProcessBar(MarketSlice slice);

        /// <summary>
        /// Removes a working order from the book so it will never fill. No-ops if the order has already filled or is unknown.
        /// </summary>
        void Cancel(string orderId);

        /// <summary>
        /// Updates the trigger price of a working order. No-ops if the order has already filled or is unknown.
        /// </summary>
        void Modify(string orderId, decimal newPrice);
    }
}
