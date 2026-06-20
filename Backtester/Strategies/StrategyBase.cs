using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Base class for all strategies, providing a default <see cref="IStrategy"/> implementation scaffold
    /// and an opt-in seam for exposing computed indicator series (<see cref="IIndicatorSource"/>).
    /// </summary>
    public abstract class StrategyBase : IStrategy, IIndicatorSource
    {
        private readonly List<IndicatorSeries> _indicatorSeries = new();

        /// <summary>Gets the indicator series the strategy has exposed via the <c>RecordIndicator</c> helpers.</summary>
        public IReadOnlyList<IndicatorSeries> IndicatorSeries => _indicatorSeries;

        /// <summary>
        /// Called once before the first bar with the complete per-symbol bar history from the feed.
        /// Override to pre-compute indicators; the default implementation is a no-op.
        /// </summary>
        public virtual void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history) { }

        /// <summary>
        /// Called on each bar for the given symbol. Strategies submit orders directly via <paramref name="broker"/>.
        /// </summary>
        public abstract void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker);

        /// <summary>
        /// Exposes a computed indicator series for reporting, not bound to any symbol (drawn on every
        /// symbol's chart). Intended to be called from <see cref="OnStart"/> after the series has been
        /// computed from the bar history.
        /// </summary>
        protected void RecordIndicator(string name, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            _indicatorSeries.Add(new IndicatorSeries(name, pane, points));
        }

        /// <summary>
        /// Exposes a computed indicator series for reporting, bound to a symbol so a multi-symbol report
        /// draws it only on that symbol's chart. Intended to be called from <see cref="OnStart"/>.
        /// </summary>
        protected void RecordIndicator(string name, string symbol, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            _indicatorSeries.Add(new IndicatorSeries(name, symbol, pane, points));
        }
    }
}
