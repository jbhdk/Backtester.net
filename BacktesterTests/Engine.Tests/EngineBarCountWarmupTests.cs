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
    /// Behaviour of the bar-count warmup overload (ADR 0022, issue #92): a run's Test range plus a Warmup
    /// resolved as "N bars before the Test start" per symbol through the <see cref="IWarmupResolvingFetcher"/>
    /// seam. Exercises the internal <c>Warmup</c> value object only through the public
    /// <see cref="BacktestEngine"/> API, with a faked warmup-resolving fetcher.
    /// </summary>
    public class EngineBarCountWarmupTests
    {
        private static readonly DateTime TestFrom = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime TestTo = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new() { Timestamp = ts, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };
        }

        [Fact]
        public async Task StartAsync_BarCountWarmup_FetchesDataRangeFromResolvedWarmupStart()
        {
            DateTime resolvedStart = TestFrom.AddDays(-30);
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", TestFrom, 3, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(resolvedStart));
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, 3, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", resolvedStart, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public async Task StartAsync_BarCountWarmup_HandsFullDataRangeHistoryToOnStart()
        {
            // The resolver pins two bars of lead-in; the fetched series carries those two warmup bars plus two
            // Test-range bars, and OnStart must see all four.
            DateTime resolvedStart = TestFrom.AddDays(-20);
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-20), 80m),
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
            };
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", TestFrom, 2, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(resolvedStart));
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            HistoryCapturingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, 2, "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(4, strategy.ReceivedHistory["AAPL"].Count);
        }

        [Fact]
        public async Task StartAsync_BarCountWarmup_ResolvesEachSymbolIndependently()
        {
            // Bar density differs between symbols, so the same bar count resolves to a different Data start for
            // each: AAPL 30 days back, MSFT 10. Each symbol must be fetched from its own resolved start.
            DateTime aaplStart = TestFrom.AddDays(-30);
            DateTime msftStart = TestFrom.AddDays(-10);
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", TestFrom, 5, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(aaplStart));
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("MSFT", TestFrom, 5, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(msftStart));
            A.CallTo(() => fetcher.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(new[] { Bar(TestFrom, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, TestFrom, TestTo, 5, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", aaplStart, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
            A.CallTo(() => fetcher.FetchAsync("MSFT", msftStart, TestTo, "1d", A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public async Task StartAsync_BarCountWarmup_ResolverRefusal_PropagatesOutOfStart()
        {
            // A symbol short of the requested warmup makes the seam throw; the engine must surface it rather
            // than run on a short lead-in.
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", TestFrom, 200, "1d", A<CancellationToken>._))
                .Throws(new InsufficientWarmupBarsException("AAPL", 200, 3, "1d"));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, 200, "1d", new DoNothingStrategy(), broker, portfolio);

            await Assert.ThrowsAsync<InsufficientWarmupBarsException>(() => engine.StartAsync());
        }

        [Fact]
        public async Task StartAsync_BarCountWarmup_LoopStepsOnlyTestRangeBars()
        {
            // Two warmup bars and three Test bars in the fetched series: only the three Test bars are looped,
            // so exactly three equity snapshots are recorded — accounting stays confined to the Test range.
            Candle[] bars =
            {
                Bar(TestFrom.AddDays(-20), 80m),
                Bar(TestFrom.AddDays(-10), 90m),
                Bar(TestFrom, 100m),
                Bar(TestFrom.AddDays(1), 101m),
                Bar(TestFrom.AddDays(2), 102m),
            };
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", TestFrom, 2, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(TestFrom.AddDays(-20)));
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, TestFrom, TestTo, 2, "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(3, portfolio.EquityHistory.Count);
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
