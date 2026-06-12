using System.Collections.Generic;

namespace Backtester.Broker
{
    using Backtester.Core;

    public interface IFillModel
    {
        IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar);
    }
}
