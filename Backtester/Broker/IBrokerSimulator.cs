using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;

    public interface IBrokerSimulator
    {
        string SubmitOrder(OrderRequest request);
        IEnumerable<Trade> ProcessBar(Candle bar);
    }
}
