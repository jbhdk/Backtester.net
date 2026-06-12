using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;

    public class FillModel_OHLCHeuristic : IFillModel
    {
        public IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar)
        {
            throw new System.NotImplementedException();
        }
    }
}
