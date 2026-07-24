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
    /// Behaviour of the absolute-date warmup overload (ADR 0022, issue #91): a run's Test range plus a
    /// Warmup whose Data-range start is pinned to an explicit <see cref="DateTime"/>. Exercises the internal
    /// <c>Warmup</c> value object only through the public <see cref="BacktestEngine"/> API, with a faked
    /// <see cref="IHistoricalDataFetcher"/> and assertions on <c>Portfolio</c>/<c>BacktestResult</c>.
    /// </summary>
    public class EngineAbsoluteWarmupTests
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
        public async Task StartAsync_AbsoluteWarmup_FetchesDataRangeFromWarmupStart()
        {
            DateTime warmupStart = TestFrom.AddDays(-45);
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, warmupStart, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", warmupStart, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public void Constructor_AbsoluteWarmupStartAfterTestFrom_IsRejected()
        {
            DateTime warmupStart = TestFrom.AddDays(1);
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BacktestEngine(fetcher, new[] { "AAPL" }, TestFrom, TestTo, warmupStart, "1d", new DoNothingStrategy(), broker, portfolio));
        }

        [Fact]
        public async Task StartAsync_AbsoluteWarmupStartEqualToTestFrom_FetchesExactlyTheTestRange()
        {
            // The boundary case: pinning the warmup start to testFrom leaves the Data range equal to the Test
            // range, so the fetch reaches back no further than testFrom.
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TestFrom, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", TestFrom, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public async Task StartAsync_AbsoluteWarmup_HandsFullDataRangeHistoryToOnStart()
        {
            // Two bars ahead of the Test range plus two inside it: OnStart sees all four.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-40), 80m),
                Bar(TestFrom.AddDays(-20), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            HistoryCapturingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TestFrom.AddDays(-45), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(4, strategy.ReceivedHistory["AAPL"].Count);
        }

        [Fact]
        public async Task StartAsync_AbsoluteWarmup_LoopStepsOnlyTestRangeBars()
        {
            // Two warmup bars ahead of the Test range, three inside it: only the three are looped, so exactly
            // three equity snapshots are recorded.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-40), 80m),
                Bar(TestFrom.AddDays(-20), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
                Bar(TestFrom.AddDays(2), 102m),
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, TestFrom.AddDays(-45), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(3, portfolio.EquityHistory.Count);
        }

        [Fact]
        public async Task StartAsync_AbsoluteWarmupStartBelowCoverageFloor_SurfacesDataCoverageException()
        {
            // Absolute warmup resolves to a concrete Data.Start fetched through the existing FetchAsync, so a
            // start below the symbol's Coverage floor surfaces the existing DataCoverageException for free —
            // the engine adds no seam and does not swallow it.
            DateTime warmupStart = TestFrom.AddDays(-45);
            IHistoricalDataFetcher fetcher = A.Fake<IHistoricalDataFetcher>();
            A.CallTo(() => fetcher.FetchAsync("AAPL", warmupStart, TestTo, "1d", A<CancellationToken>._))
                .Throws(new DataCoverageException("AAPL", warmupStart, TestFrom, "1d"));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, warmupStart, "1d", new DoNothingStrategy(), broker, portfolio);

            await Assert.ThrowsAsync<DataCoverageException>(() => engine.StartAsync());
        }

        // --- Stub strategies ---

        private class DoNothingStrategy : StrategyBase
        {
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
