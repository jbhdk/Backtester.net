using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Optional seam a strategy implements to expose the composite indicators it computed, so the
    /// engine can surface them on the backtest result for reporting (ADR 0007 / 0012). Exposure is
    /// opt-in: a strategy that does not implement this contributes no indicators.
    /// </summary>
    public interface IIndicatorSource
    {
        /// <summary>Gets the composite indicators the strategy has exposed.</summary>
        IReadOnlyList<Indicator> Indicators { get; }
    }
}
