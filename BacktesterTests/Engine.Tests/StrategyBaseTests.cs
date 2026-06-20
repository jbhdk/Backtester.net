using System;
using System.Collections.Generic;
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
        public void RecordIndicator_InOnStart_ExposesSeriesViaIndicatorSource()
        {
            RecordingStrategy strategy = new(
                ("SMA", IndicatorPane.PriceOverlay, new[]
                {
                    new IndicatorPoint { Timestamp = T0, Value = 100m },
                    new IndicatorPoint { Timestamp = T0.AddDays(1), Value = 101m }
                }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            IndicatorSeries series = Assert.Single(source.IndicatorSeries);
            Assert.Equal("SMA", series.Name);
            Assert.Equal(IndicatorPane.PriceOverlay, series.Pane);
            Assert.Equal(2, series.Points.Count);
            Assert.Equal(101m, series.Points[1].Value);
        }

        [Fact]
        public void RecordIndicator_TwoSeries_ExposesBothWithPanes()
        {
            RecordingStrategy strategy = new(
                ("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } }),
                ("RSI", IndicatorPane.SeparatePane, new[] { new IndicatorPoint { Timestamp = T0, Value = 55m } }));

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal(2, source.IndicatorSeries.Count);
            Assert.Equal("SMA", source.IndicatorSeries[0].Name);
            Assert.Equal(IndicatorPane.PriceOverlay, source.IndicatorSeries[0].Pane);
            Assert.Equal("RSI", source.IndicatorSeries[1].Name);
            Assert.Equal(IndicatorPane.SeparatePane, source.IndicatorSeries[1].Pane);
            Assert.Equal(55m, source.IndicatorSeries[1].Points[0].Value);
        }

        [Fact]
        public void RecordIndicator_WithSymbol_BindsSeriesToSymbol()
        {
            SymbolRecordingStrategy strategy = new("MSFT");

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Equal("MSFT", Assert.Single(source.IndicatorSeries).Symbol);
        }

        [Fact]
        public void RecordNothing_ExposesEmptyCollection()
        {
            RecordingStrategy strategy = new();

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>>());

            IIndicatorSource source = strategy;
            Assert.Empty(source.IndicatorSeries);
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
