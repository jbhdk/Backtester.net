using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Simulates order submission and fill processing during a backtest.
    /// Extends <see cref="IBroker"/> so the engine can pass the simulator directly to strategies.
    /// </summary>
    public interface IBrokerSimulator : IBroker
    {
        /// <summary>
        /// Queues an order for fill processing on the next bar, applying sizing and risk models.
        /// Returns the assigned order ID, or null if rejected.
        /// </summary>
        string SubmitOrder(OrderRequest request);

        /// <summary>
        /// Matches all working orders against the current market slice and returns the resulting trades.
        /// Unfilled orders remain working (GTC) and are evaluated again on subsequent bars.
        /// </summary>
        IEnumerable<Trade> ProcessBar(MarketSlice slice);
    }
}
