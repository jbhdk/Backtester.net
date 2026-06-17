using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Determines which pending orders are filled and at what price for a given bar.
    /// </summary>
    public interface IFillModel
    {
        /// <summary>
        /// Evaluates each order against the provided bar and returns fill results for those that match.
        /// </summary>
        IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar);
    }
}
