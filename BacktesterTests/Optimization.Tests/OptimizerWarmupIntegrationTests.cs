using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using Backtester.Optimization;
using Backtester.Strategies;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Integration behaviour of warmup on the Optimizer (ADR 0022, issue #93): a Test range plus an optional
    /// Warmup lead-in, fetched once as the shared Data range so every Trial is warm and measured over the same
    /// Test range. Follows <see cref="OptimizerPrimingIntegrationTests"/>: a real cache-aware
    /// <see cref="HistoricalDataFetcher"/> over a temp folder, primed once.
    /// </summary>
    public class OptimizerWarmupIntegrationTests
    {
        [Fact]
        public async Task PeriodWarmup_TrialOnStartHistory_ReachesBackBeforeTestRange()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime wideFrom = now.AddYears(-2);
            DateTime testFrom = now.AddYears(-1);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, now, "1d");

            HistoryCapturingStrategy captured = new();
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                now,
                TimeSpan.FromDays(90),
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (captured, new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await optimizer.RunAsync();

            DateTime earliest = captured.ReceivedHistory["AAPL"].Min(candle => candle.Timestamp);
            Assert.True(earliest < testFrom, "OnStart history should include warmup bars before the Test range start.");
        }

        [Fact]
        public async Task PeriodWarmup_TrialResult_IsClippedToTestRange()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime wideFrom = now.AddYears(-2);
            DateTime testFrom = now.AddYears(-1);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, now, "1d");

            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                now,
                TimeSpan.FromDays(90),
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                retainAllBacktestResults: true,
                minimumTrades: 0);

            OptimizationResult result = await optimizer.RunAsync();

            BacktestResult backtest = result.Best.BacktestResult;
            Assert.Equal(testFrom, backtest.FromUtc);
            Assert.Equal(now, backtest.ToUtc);
            Assert.All(backtest.CandleHistory["AAPL"], candle => Assert.True(candle.Timestamp >= testFrom));
        }

        [Fact]
        public async Task AbsoluteWarmup_TrialOnStartHistory_ReachesBackToWarmupStart()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime wideFrom = now.AddYears(-2);
            DateTime testFrom = now.AddYears(-1);
            DateTime warmupStart = testFrom.AddDays(-120);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, now, "1d");

            HistoryCapturingStrategy captured = new();
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                now,
                warmupStart,
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (captured, new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await optimizer.RunAsync();

            DateTime earliest = captured.ReceivedHistory["AAPL"].Min(candle => candle.Timestamp);
            Assert.True(earliest < testFrom, "OnStart history should include the warmup lead-in.");
            Assert.True(earliest >= warmupStart, "OnStart history should not reach back before the pinned warmup start.");
        }

        [Fact]
        public void AbsoluteWarmup_WarmupStartAfterTestFrom_IsRejected()
        {
            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow));
            HistoricalDataFetcher fetcher = new(provider, NewTempFolder());
            DateTime testFrom = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.Throws<ArgumentOutOfRangeException>(() => new Optimizer(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                testFrom.AddMonths(3),
                testFrom.AddDays(1), // warmupStart after testFrom
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio))));
        }

        [Fact]
        public async Task BarCountWarmup_TrialOnStartHistory_CarriesExactlyNBarsBeforeTestRange()
        {
            string tmp = NewTempFolder();
            // Weekly bars pinned to a fixed start so the Test start lands exactly on a bar and the warmup count
            // is exact: 6 warmup bars means six weekly bars precede the Test range start.
            DateTime wideFrom = new(2022, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            DateTime to = wideFrom.AddDays(7 * 60);
            DateTime testFrom = wideFrom.AddDays(7 * 40);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, to));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, to, "1d");

            HistoryCapturingStrategy captured = new();
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                to,
                6,
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (captured, new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await optimizer.RunAsync();

            int warmupBarCount = captured.ReceivedHistory["AAPL"].Count(candle => candle.Timestamp < testFrom);
            Assert.Equal(6, warmupBarCount);
        }

        [Fact]
        public async Task BarCountWarmup_MoreBarsThanAvailable_ThrowsThroughFetchOnce()
        {
            string tmp = NewTempFolder();
            DateTime wideFrom = new(2022, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            DateTime to = wideFrom.AddDays(7 * 20);
            DateTime testFrom = wideFrom.AddDays(7 * 3); // only 3 weekly bars precede the Test start

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, to));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, to, "1d");

            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                to,
                50, // far more warmup bars than the 3 available
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 2, step: 1),
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await Assert.ThrowsAsync<InsufficientWarmupBarsException>(() => optimizer.RunAsync());
        }

        [Fact]
        public async Task BarCountWarmup_ResolvesOncePerSymbol_NotPerTrial()
        {
            // A three-point grid runs three Trials, but the shared Data range is fetched once — so the bar-count
            // resolution fires once per symbol in the fetch-once step, never per Trial.
            DateTime testFrom = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime testTo = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            Candle[] bars =
            {
                new() { Timestamp = testFrom.AddDays(-20), Open = 90, High = 92, Low = 88, Close = 90, Volume = 1000 },
                new() { Timestamp = testFrom, Open = 100, High = 102, Low = 98, Close = 100, Volume = 1000 },
                new() { Timestamp = testFrom.AddDays(1), Open = 101, High = 103, Low = 99, Close = 101, Volume = 1000 },
            };
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", testFrom, 5, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(testFrom.AddDays(-20)));
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(bars));

            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                testTo,
                5,
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1),
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await optimizer.RunAsync();

            A.CallTo(() => fetcher.ResolveWarmupStartAsync("AAPL", testFrom, 5, "1d", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task PeriodWarmup_OverPrimedRange_CallsProviderOnlyDuringPriming()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime wideFrom = now.AddYears(-2);
            DateTime testFrom = now.AddYears(-1);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, now, "1d");

            // The Data range (Test start minus 90-day warmup) stays inside the primed range, so the sweep reads
            // the warm Cache and never re-hits the Provider.
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                testFrom,
                now,
                TimeSpan.FromDays(90),
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 2, step: 1),
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);
            await optimizer.RunAsync();

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        /// <summary>A fake provider that returns the given series for any symbol and range.</summary>
        private static IHistoricalDataProvider ProviderReturning(IReadOnlyList<Candle> series)
        {
            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(series));
            return provider;
        }

        /// <summary>A rising weekly OHLCV series spanning the inclusive range.</summary>
        private static IReadOnlyList<Candle> WeeklySeries(DateTime fromUtc, DateTime toUtc)
        {
            List<Candle> bars = new();
            decimal price = 100m;
            for (DateTime ts = fromUtc; ts <= toUtc; ts = ts.AddDays(7))
            {
                bars.Add(new Candle { Timestamp = ts, Open = price, High = price + 2, Low = price - 2, Close = price, Volume = 1000 });
                price += 1m;
            }

            return bars;
        }

        private static string NewTempFolder()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_optimizer_warmup_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            return tmp;
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }

        /// <summary>Buys a fixed quantity on a symbol's first bar and sells it on the next, for one round trip per symbol.</summary>
        private sealed class BuyThenSellStrategy : StrategyBase
        {
            private readonly int _quantity;
            private bool _bought;
            private bool _sold;

            public BuyThenSellStrategy(int quantity)
            {
                _quantity = quantity;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought)
                {
                    _bought = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = _quantity });
                }
                else if (!_sold)
                {
                    _sold = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = _quantity });
                }
            }
        }

        /// <summary>Captures the history handed to OnStart so a test can assert its span; trades once for a round trip.</summary>
        private sealed class HistoryCapturingStrategy : StrategyBase
        {
            public IReadOnlyDictionary<string, IReadOnlyList<Candle>> ReceivedHistory { get; private set; }
            private bool _bought;
            private bool _sold;

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                ReceivedHistory = history;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought)
                {
                    _bought = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
                else if (!_sold)
                {
                    _sold = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 1 });
                }
            }
        }
    }
}
