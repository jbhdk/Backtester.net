using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Optional seam a strategy implements to expose the indicator series it computed, so the engine
    /// can surface them on the backtest result for reporting (ADR 0007). Exposure is opt-in: a
    /// strategy that does not implement this contributes no series.
    /// </summary>
    public interface IIndicatorSource
    {
        /// <summary>Gets the indicator series the strategy has exposed.</summary>
        IReadOnlyList<IndicatorSeries> IndicatorSeries { get; }
    }
}
