using System;
using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;

    public class BrokerSimulator : IBrokerSimulator
    {
        public string SubmitOrder(OrderRequest request)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Trade> ProcessBar(Candle bar)
        {
            throw new NotImplementedException();
        }
    }
}
