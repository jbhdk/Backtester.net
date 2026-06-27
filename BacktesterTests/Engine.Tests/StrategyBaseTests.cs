using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    public class StrategyBaseTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void RecordIndicator_InOnStart_ExposesOneSeriesIndicatorViaSource()
        {
            RecordingStrategy strategy = new(
                ("SMA", IndicatorPane.PriceOverlay, new[]
                {
                    new IndicatorPoint { Timestamp = T0, Value = 100m },
                    new IndicatorPoint { Timestamp = T0.AddDays(1), Value = 101m }
                }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Indicator indicator = Assert.Single(source.Indicators);
            Assert.Equal("SMA", indicator.Name);
            Assert.Equal(IndicatorPane.PriceOverlay, indicator.Pane);
            IndicatorSeries series = Assert.Single(indicator.Series);
            Assert.Equal("SMA", series.Name);
            Assert.Equal(2, series.Points.Count);
            Assert.Equal(101m, series.Points[1].Value);
        }

        [Fact]
        public void RecordIndicator_OnPriceOverlay_DefaultsSeriesShapeToLine()
        {
            RecordingStrategy strategy = new(
                ("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal(IndicatorShape.Line, Assert.Single(Assert.Single(source.Indicators).Series).Shape);
        }

        [Fact]
        public void RecordIndicator_OnSeparatePane_DefaultsSeriesShapeToArea()
        {
            RecordingStrategy strategy = new(
                ("RSI", IndicatorPane.SeparatePane, new[] { new IndicatorPoint { Timestamp = T0, Value = 55m } }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal(IndicatorShape.Area, Assert.Single(Assert.Single(source.Indicators).Series).Shape);
        }

        [Fact]
        public void RecordIndicator_TwoIndicators_ExposesBothWithPanes()
        {
            RecordingStrategy strategy = new(
                ("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } }),
                ("RSI", IndicatorPane.SeparatePane, new[] { new IndicatorPoint { Timestamp = T0, Value = 55m } }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal(2, source.Indicators.Count);
            Assert.Equal("SMA", source.Indicators[0].Name);
            Assert.Equal(IndicatorPane.PriceOverlay, source.Indicators[0].Pane);
            Assert.Equal("RSI", source.Indicators[1].Name);
            Assert.Equal(IndicatorPane.SeparatePane, source.Indicators[1].Pane);
            Assert.Equal(55m, Assert.Single(source.Indicators[1].Series).Points[0].Value);
        }

        [Fact]
        public void RecordIndicator_WithSymbol_BindsIndicatorToSymbol()
        {
            SymbolRecordingStrategy strategy = new("MSFT");

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal("MSFT", Assert.Single(source.Indicators).Symbol);
        }

        [Fact]
        public void RecordNothing_ExposesEmptyCollection()
        {
            RecordingStrategy strategy = new();

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Empty(source.Indicators);
        }

        [Fact]
        public void RecordIndicator_Composite_ExposesMultiSeriesIndicatorIntact()
        {
            Indicator macd = new("MACD", IndicatorPane.SeparatePane, new[]
            {
                new IndicatorSeries("MACD", IndicatorShape.Line, new[] { new IndicatorPoint { Timestamp = T0, Value = 1m } }),
                new IndicatorSeries("Signal", IndicatorShape.Line, new[] { new IndicatorPoint { Timestamp = T0, Value = 0.8m } }),
                new IndicatorSeries("Histogram", IndicatorShape.Histogram, new[] { new IndicatorPoint { Timestamp = T0, Value = 0.2m } })
            });
            CompositeRecordingStrategy strategy = new(macd);

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Indicator exposed = Assert.Single(source.Indicators);
            Assert.Equal("MACD", exposed.Name);
            Assert.Equal(IndicatorPane.SeparatePane, exposed.Pane);
            Assert.Equal(new[] { "MACD", "Signal", "Histogram" }, exposed.Series.Select(series => series.Name));
            Assert.Equal(IndicatorShape.Histogram, exposed.Series[2].Shape);
        }

        [Fact]
        public void OnRoundTripClosed_DefaultImplementation_IsAnOverridableNoOp()
        {
            // A StrategyBase that does not override the seam is still an IRoundTripObserver, and its default
            // OnRoundTripClosed accepts a closed round trip without acting or throwing.
            IRoundTripObserver observer = new BareStrategy();

            observer.OnRoundTripClosed(new RoundTrip { Symbol = "AAPL", RealizedPnL = 100m });

            Assert.IsAssignableFrom<IRoundTripObserver>(observer);
        }

        [Fact]
        public void OnRoundTripClosed_Overridden_ReceivesTheRoundTrip()
        {
            ObservingStrategy strategy = new();
            RoundTrip trip = new() { Symbol = "AAPL", RealizedPnL = 100m };

            ((IRoundTripObserver)strategy).OnRoundTripClosed(trip);

            Assert.Same(trip, strategy.Last);
        }

        /// <summary>A StrategyBase that overrides nothing, leaving the seam defaults in place.</summary>
        private class BareStrategy : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>A StrategyBase that overrides OnRoundTripClosed to capture the round trip it observes.</summary>
        private class ObservingStrategy : StrategyBase
        {
            public RoundTrip Last { get; private set; }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }

            public override void OnRoundTripClosed(RoundTrip roundTrip)
            {
                Last = roundTrip;
            }
        }

        /// <summary>Records the supplied series during OnStart via the protected helper.</summary>
        private class RecordingStrategy : StrategyBase
        {
            private readonly (string Name, IndicatorPane Pane, IReadOnlyList<IndicatorPoint> Points)[] _toRecord;

            public RecordingStrategy(params (string Name, IndicatorPane Pane, IReadOnlyList<IndicatorPoint> Points)[] toRecord)
            {
                _toRecord = toRecord;
            }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                foreach ((string name, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points) in _toRecord)
                {
                    RecordIndicator(name, pane, points);
                }
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>Records a pre-built composite indicator during OnStart via the Indicator overload.</summary>
        private class CompositeRecordingStrategy : StrategyBase
        {
            private readonly Indicator _indicator;

            public CompositeRecordingStrategy(Indicator indicator)
            {
                _indicator = indicator;
            }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                RecordIndicator(_indicator);
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>Records a single symbol-tagged indicator during OnStart.</summary>
        private class SymbolRecordingStrategy : StrategyBase
        {
            private readonly string _symbol;

            public SymbolRecordingStrategy(string symbol)
            {
                _symbol = symbol;
            }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                RecordIndicator("SMA", _symbol, IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }
    }
}
