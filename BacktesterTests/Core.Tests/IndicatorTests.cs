using System;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class IndicatorTests
    {
        private static IndicatorSeries Series(IndicatorShape shape)
        {
            return new IndicatorSeries("X", shape, Array.Empty<IndicatorPoint>());
        }

        [Fact]
        public void Constructor_PriceOverlayWithHistogramSeries_Throws()
        {
            // A histogram is a zero-baseline oscillator: on the shared price scale it collapses to an
            // invisible sliver and drags the price axis down, so it is not a valid price overlay.
            Assert.Throws<ArgumentException>(() =>
                new Indicator("MACD", IndicatorPane.PriceOverlay, new[] { Series(IndicatorShape.Histogram) }));
        }

        [Fact]
        public void Constructor_SeparatePaneWithHistogramSeries_IsAllowed()
        {
            // A histogram is valid in its own pane (the MACD case), so the guard must not reject it there.
            Indicator indicator = new("MACD", IndicatorPane.SeparatePane, new[] { Series(IndicatorShape.Histogram) });

            Assert.Equal(IndicatorShape.Histogram, indicator.Series[0].Shape);
        }
    }
}
