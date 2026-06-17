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
        /// Queues an order for processing on the next bar. Returns the assigned order ID, or null if rejected.
        /// </summary>
        string SubmitOrder(OrderRequest request);

        /// <summary>
        /// Processes all pending orders against the current market slice and returns the resulting trades.
        /// </summary>
        IEnumerable<Trade> ProcessBar(MarketSlice slice);
    }
}
