using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Strategies;
using FakeItEasy;
using BacktestEngine = Backtester.Engine.Engine;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    /// <summary>
    /// Behaviour of the two-window split (ADR 0022): a run's Test range plus an optional Warmup lead-in.
    /// Exercises the internal <c>Warmup</c> value object only through the public <see cref="BacktestEngine"/>
    /// API, with a faked <see cref="IHistoricalDataFetcher"/> and assertions on <c>Portfolio</c>/<c>BacktestResult</c>.
    /// </summary>
    public class EngineWarmupTests
    {
        private static readonly DateTime TestFrom = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime TestTo = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new() { Timestamp = ts, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };
        }

        /// <summary>Builds a fake fetcher that returns the given candle series for each named symbol.</summary>
        private static IHistoricalDataFetcher FetcherReturning(params (string Symbol, IReadOnlyList<Candle> Candles)[] series)
        {
            IHistoricalDataFetcher fetcher = A.Fake<IHistoricalDataFetcher>();
            foreach ((string symbol, IReadOnlyList<Candle> candles) in series)
            {
                A.CallTo(() => fetcher.FetchAsync(symbol, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                    .Returns(Task.FromResult(candles));
            }

            return fetcher;
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_FetchesDataRangeBackedOffByWarmup()
        {
            TimeSpan warmup = TimeSpan.FromDays(30);
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, warmup, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", TestFrom - warmup, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_HandsFullDataRangeHistoryToOnStart()
        {
            // One warmup bar ahead of the Test range plus two Test-range bars: OnStart sees all three.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            HistoryCapturingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(3, strategy.ReceivedHistory["AAPL"].Count);
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_LoopStepsOnlyTestRangeBars()
        {
            // Two warmup bars ahead of the Test range, three inside it: only the three are looped, so exactly
            // three equity snapshots are recorded.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-20), 80m),
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
                Bar(TestFrom.AddDays(2), 102m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(3, portfolio.EquityHistory.Count);
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_OnBarNeverFiresOnWarmupBar()
        {
            // The strategy would buy on the very first bar it is handed. If OnBar fired during warmup that
            // buy would fill on the next (still-warmup) bar; because warmup bars are never looped, the first
            // OnBar is the first Test bar and the resulting fill lands inside the Test range.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-20), 80m),
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            BuyOnFirstBarStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(TestFrom, strategy.FirstBarSeen);
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_StrategyTradingOnWarmupBar_ProducesNoWarmupPosition()
        {
            // A single warmup bar and a single Test bar. A strategy that buys on the first bar it sees would,
            // if warmup were looped, fill against the Test bar. Since warmup is never looped, the only bar it
            // trades on is the last (Test) bar, whose order can never fill — so no position is opened.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", new BuyOnFirstBarStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Empty(portfolio.Positions);
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_ResultCandleHistoryDropsWarmupBars()
        {
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-20), 80m),
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            IReadOnlyList<Candle> candles = result.CandleHistory["AAPL"];
            Assert.Equal(2, candles.Count);
            Assert.All(candles, candle => Assert.True(candle.Timestamp >= TestFrom));
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_ClipsExposedIndicatorToTestRange()
        {
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            // Exposes an SMA with one point per bar, including the warmup bar, computed over the full history.
            IndicatorOverFullHistoryStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", strategy, broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            IndicatorSeries series = Assert.Single(result.Indicators).Series[0];
            Assert.Equal(2, series.Points.Count);
            Assert.All(series.Points, point => Assert.True(point.Timestamp >= TestFrom));
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_ClippedIndicatorKeepsWarmValueOnFirstDrawnPoint()
        {
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IndicatorOverFullHistoryStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", strategy, broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            IndicatorPoint firstDrawn = Assert.Single(result.Indicators).Series[0].Points[0];
            Assert.Equal(TestFrom, firstDrawn.Timestamp);
            // The value was computed over the full history (warm), not recomputed from the clipped candles.
            Assert.Equal(95m, firstDrawn.Value);
        }

        [Fact]
        public async Task StartAsync_PeriodWarmup_ResultFromToAreTheTestRange()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(TestFrom.AddDays(-10), 90m), Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TimeSpan.FromDays(30), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.Equal(TestFrom, result.FromUtc);
            Assert.Equal(TestTo, result.ToUtc);
        }

        [Fact]
        public async Task StartAsync_NoWarmup_FetchesExactlyTheTestRange()
        {
            // The no-warmup overload (string interval directly after testTo) leaves the Data range equal to
            // the Test range: the fetch reaches back no further than testFrom.
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", TestFrom, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        // --- Stub strategies ---

        private class DoNothingStrategy : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>
        /// Buys once on the first bar it is handed and records that bar's timestamp, so a test can prove the
        /// first <c>OnBar</c> is a Test-range bar (never a warmup bar).
        /// </summary>
        private class BuyOnFirstBarStrategy : StrategyBase
        {
            public DateTime? FirstBarSeen { get; private set; }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (FirstBarSeen is null)
                {
                    FirstBarSeen = bar.Timestamp;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
            }
        }

        /// <summary>
        /// In OnStart, exposes a price-overlay "SMA" with one point per bar of the full Data-range history: a
        /// trailing two-bar average, so a Test-range point's value reflects the preceding warmup bar. This
        /// proves clipping drops the lead-in points while preserving the warm value on the first drawn point.
        /// </summary>
        private class IndicatorOverFullHistoryStrategy : StrategyBase
        {
            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                IReadOnlyList<Candle> candles = history["AAPL"];
                List<IndicatorPoint> points = new(candles.Count);
                for (int index = 0; index < candles.Count; index++)
                {
                    decimal value = index == 0
                        ? candles[index].Close
                        : (candles[index - 1].Close + candles[index].Close) / 2m;
                    points.Add(new IndicatorPoint { Timestamp = candles[index].Timestamp, Value = value });
                }

                RecordIndicator("SMA", IndicatorPane.PriceOverlay, points);
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>Captures the history handed to OnStart so a test can assert its span.</summary>
        private class HistoryCapturingStrategy : StrategyBase
        {
            public IReadOnlyDictionary<string, IReadOnlyList<Candle>> ReceivedHistory { get; private set; }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                ReceivedHistory = history;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }
    }
}
