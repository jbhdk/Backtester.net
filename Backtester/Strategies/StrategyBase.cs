using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Base class for all strategies, providing a default <see cref="IStrategy"/> implementation scaffold
    /// and opt-in seams for exposing computed indicator series (<see cref="IIndicatorSource"/>) and for
    /// observing round trips as they close (<see cref="IRoundTripObserver"/>).
    /// </summary>
    public abstract class StrategyBase : IStrategy, IIndicatorSource, IRoundTripObserver
    {
        private readonly List<Indicator> _indicators = new();

        /// <summary>Gets the composite indicators the strategy has exposed via the <c>RecordIndicator</c> helpers.</summary>
        public IReadOnlyList<Indicator> Indicators => _indicators;

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
        /// Called as each round trip closes during a run, before that bar's <c>OnBar</c>. Override to react
        /// to a round trip's result (e.g. count losses); the default implementation is a no-op.
        /// </summary>
        public virtual void OnRoundTripClosed(RoundTrip roundTrip) { }

        /// <summary>
        /// Exposes a pre-built composite indicator for reporting, intact. The overload for a multi-series
        /// study (e.g. a MACD's line, signal, and histogram in one shared pane): construct the
        /// <see cref="Indicator"/> with its series and placement and hand it over. Intended to be called
        /// from <see cref="OnStart"/> after the series have been computed from the bar history.
        /// </summary>
        protected void RecordIndicator(Indicator indicator)
        {
            _indicators.Add(indicator);
        }

        /// <summary>
        /// Exposes a computed single-line indicator for reporting, not bound to any symbol (drawn on
        /// every symbol's chart). Wraps the line in a one-series <see cref="Indicator"/>, defaulting the
        /// series shape by pane (line on the price overlay, area in a separate pane). Intended to be
        /// called from <see cref="OnStart"/> after the series has been computed from the bar history.
        /// </summary>
        protected void RecordIndicator(string name, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            _indicators.Add(new Indicator(name, pane, new[] { new IndicatorSeries(name, DefaultShape(pane), points) }));
        }

        /// <summary>
        /// Exposes a computed single-line indicator for reporting, bound to a symbol so a multi-symbol
        /// report draws it only on that symbol's chart. Wraps the line in a one-series
        /// <see cref="Indicator"/>, defaulting the series shape by pane. Intended to be called from
        /// <see cref="OnStart"/>.
        /// </summary>
        protected void RecordIndicator(string name, string symbol, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            _indicators.Add(new Indicator(name, symbol, pane, new[] { new IndicatorSeries(name, DefaultShape(pane), points) }));
        }

        /// <summary>
        /// Picks the default shape for a single-line indicator from its pane, preserving the report's
        /// existing look: a line on the price overlay, a filled area in its own separate pane.
        /// </summary>
        private static IndicatorShape DefaultShape(IndicatorPane pane)
        {
            return pane == IndicatorPane.PriceOverlay ? IndicatorShape.Line : IndicatorShape.Area;
        }
    }
}
