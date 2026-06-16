using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;
    using Backtester.Data;

    public interface IBrokerSimulator
    {
        string SubmitOrder(OrderRequest request);
        IEnumerable<Trade> ProcessBar(MarketSlice slice);
    }
}
