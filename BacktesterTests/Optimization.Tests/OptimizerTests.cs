using System;
using System.Collections.Generic;
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
    /// Behaviour of the <see cref="Optimizer"/> exercised through its public API: a grid over a
    /// <see cref="ParameterSpace"/> is run one backtest per Parameter set (a Trial) against a faked
    /// data fetcher, a real broker, and a small real strategy whose behaviour comes from a Parameter.
    /// </summary>
    public class OptimizerTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

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

        /// <summary>A three-bar rising AAPL series: a buy filled at bar 1 and sold at bar 2 realizes +10/share.</summary>
        private static IHistoricalDataFetcher RisingAaplFetcher()
        {
            return FetcherReturning(("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));
        }

        /// <summary>Builds an Optimizer over a single-symbol "qty" grid whose Trials each realize qty*10.</summary>
        private static Optimizer QtyOptimizer(
            IHistoricalDataFetcher fetcher,
            ParameterSpace space,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 0)
        {
            return new Optimizer(
                fetcher,
                new[] { "AAPL" },
                T0,
                T0.AddYears(1),
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                retainAllBacktestResults,
                objective,
                minimumTrades);
        }

        private static Trial TrialForQty(OptimizationResult result, int qty)
        {
            return result.Trials.First(trial => trial.Parameters.Int("qty") == qty);
        }

        [Fact]
        public async Task RunAsync_RunsOneTrialPerParameterSet()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            Assert.Equal(3, result.Trials.Count);
        }

        [Fact]
        public async Task RunAsync_EachTrialCarriesTheParameterSetItRan()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            Assert.Equal(new[] { 1, 2, 3 }, result.Trials.Select(trial => trial.Parameters.Int("qty")).OrderBy(qty => qty));
        }

        [Fact]
        public async Task RunAsync_EachTrialCarriesPerformanceStatsFromItsOwnRun()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            // Buy fills at bar 1 (110), sell fills at bar 2 (120): realized +10 per share, so qty scales it.
            Assert.Equal(10m, TrialForQty(result, 1).Stats.NetProfit);
            Assert.Equal(30m, TrialForQty(result, 3).Stats.NetProfit);
        }

        [Fact]
        public async Task RunAsync_RanksTrialsByScoreBestFirst()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            for (int index = 0; index < result.Trials.Count - 1; index++)
            {
                Assert.True(result.Trials[index].Score >= result.Trials[index + 1].Score);
            }

            Assert.Same(result.Trials[0], result.Best);
            Assert.Equal(result.Trials.Max(trial => trial.Score), result.Best.Score);
        }

        [Fact]
        public async Task RunAsync_WithMaximizeObjective_RanksHighestMetricTrialFirst()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);
            Objective maximizeNetProfit = Objective.Maximize(stats => stats.NetProfit);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space, objective: maximizeNetProfit).RunAsync();

            // qty scales realized net profit: 1->10, 2->20, 3->30, so maximising NetProfit puts qty 3 first.
            Assert.Equal(new[] { 3, 2, 1 }, result.Trials.Select(trial => trial.Parameters.Int("qty")));
            Assert.Equal(30m, result.Best.Score);
        }

        [Fact]
        public async Task RunAsync_WithMinimizeObjective_RanksLowestMetricTrialFirst()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);
            Objective minimizeNetProfit = Objective.Minimize(stats => stats.NetProfit);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space, objective: minimizeNetProfit).RunAsync();

            // The same grid ranked in the opposite direction: qty 1 (net profit 10) now wins.
            Assert.Equal(new[] { 1, 2, 3 }, result.Trials.Select(trial => trial.Parameters.Int("qty")));
            Assert.Equal(10m, result.Best.Score);
        }

        [Fact]
        public async Task RunAsync_ByDefault_ScoresEachTrialByItsSharpe()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            Assert.All(result.Trials, trial => Assert.Equal(trial.Stats.Sharpe, trial.Score));
        }

        [Fact]
        public async Task RunAsync_WithNamedPreset_DrivesRankingEndToEnd()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space, objective: Objectives.NetProfit).RunAsync();

            Assert.Equal(3, result.Best.Parameters.Int("qty"));
            Assert.Equal(30m, result.Best.Score);
        }

        [Fact]
        public async Task RunAsync_BestTrialCarriesItsBacktestResult()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            Assert.NotNull(result.Best.BacktestResult);
        }

        [Fact]
        public async Task RunAsync_ByDefault_NonBestTrialsCarryNoBacktestResult()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space).RunAsync();

            Assert.All(result.Trials.Skip(1), trial => Assert.Null(trial.BacktestResult));
        }

        [Fact]
        public async Task RunAsync_WhenRetainingAllResults_EveryTrialCarriesItsBacktestResult()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space, retainAllBacktestResults: true).RunAsync();

            Assert.All(result.Trials, trial => Assert.NotNull(trial.BacktestResult));
        }

        [Fact]
        public async Task RunAsync_FetchesEachSymbolOnce_AcrossAllTrials()
        {
            IHistoricalDataFetcher fetcher = RisingAaplFetcher();
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);

            await QtyOptimizer(fetcher, space).RunAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        /// <summary>A rising series of <paramref name="bars"/> bars from 100 in +10 steps, so consecutive fills differ by 10.</summary>
        private static IHistoricalDataFetcher RisingSeriesFetcher(int bars)
        {
            List<Candle> candles = new();
            for (int index = 0; index < bars; index++)
            {
                candles.Add(Bar(T0.AddDays(index), 100m + 10m * index));
            }

            return FetcherReturning(("AAPL", candles));
        }

        /// <summary>
        /// Builds an Optimizer whose "case" axis maps to two hand-picked Trials: case 1 does few Round trips
        /// at a large quantity (high Score, low trade count) and case 2 does many Round trips at quantity one
        /// (lower Score, high trade count), so Eligibility decides the winner.
        /// </summary>
        private static Optimizer TradeCountOptimizer(IHistoricalDataFetcher fetcher, int minimumTrades)
        {
            ParameterSpace space = new ParameterSpace().AddInt("case", from: 1, to: 2, step: 1);
            return new Optimizer(
                fetcher,
                new[] { "AAPL" },
                T0,
                T0.AddYears(1),
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) =>
                {
                    (int roundTrips, int quantity) = parameters.Int("case") == 1 ? (2, 100) : (10, 1);
                    return (new NRoundTripStrategy(roundTrips, quantity), new BrokerSimulator(portfolio));
                },
                objective: Objectives.NetProfit,
                minimumTrades: minimumTrades);
        }

        private static Trial TrialForCase(OptimizationResult result, int caseNumber)
        {
            return result.Trials.First(trial => trial.Parameters.Int("case") == caseNumber);
        }

        [Fact]
        public async Task RunAsync_BestIsHighestScoringEligibleTrial_NotAHigherScoringIneligibleOne()
        {
            // Case 1 scores 2000 net profit over 2 Round trips (ineligible); case 2 scores 100 over 10 (eligible).
            OptimizationResult result = await TradeCountOptimizer(RisingSeriesFetcher(bars: 22), minimumTrades: 5).RunAsync();

            Assert.Equal(2, result.Best.Parameters.Int("case"));
        }

        [Fact]
        public async Task RunAsync_KeepsHigherScoringIneligibleTrialInTheRankedListFlagged()
        {
            OptimizationResult result = await TradeCountOptimizer(RisingSeriesFetcher(bars: 22), minimumTrades: 5).RunAsync();

            Trial ineligible = TrialForCase(result, 1);
            Assert.Contains(ineligible, result.Trials);
            Assert.False(ineligible.Eligible);
        }

        /// <summary>Runs a single Trial of exactly <paramref name="roundTrips"/> Round trips with no minimum passed, so the constructor default governs Eligibility.</summary>
        private static async Task<Trial> DefaultMinimumTrialAsync(int roundTrips)
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1);
            Optimizer optimizer = new Optimizer(
                RisingSeriesFetcher(bars: roundTrips * 2 + 2),
                new[] { "AAPL" },
                T0,
                T0.AddYears(1),
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) => (new NRoundTripStrategy(roundTrips, quantity: 1), new BrokerSimulator(portfolio)));

            OptimizationResult result = await optimizer.RunAsync();
            return result.Trials.Single();
        }

        [Fact]
        public async Task RunAsync_ByDefault_MarksTrialWithTwentyNineRoundTripsIneligible()
        {
            Trial trial = await DefaultMinimumTrialAsync(roundTrips: 29);

            Assert.False(trial.Eligible);
        }

        [Fact]
        public async Task RunAsync_ByDefault_MarksTrialWithThirtyRoundTripsEligible()
        {
            Trial trial = await DefaultMinimumTrialAsync(roundTrips: 30);

            Assert.True(trial.Eligible);
        }

        [Fact]
        public async Task RunAsync_BestCarriesItsBacktestResult_EvenWhenAHigherScoringTrialIsIneligible()
        {
            OptimizationResult result = await TradeCountOptimizer(RisingSeriesFetcher(bars: 22), minimumTrades: 5).RunAsync();

            Assert.NotNull(result.Best.BacktestResult);
        }

        [Fact]
        public async Task RunAsync_WhenNoTrialMeetsTheMinimum_HasNoBest()
        {
            // Both Trials do at most 10 Round trips, so a minimum of 100 leaves none eligible.
            OptimizationResult result = await TradeCountOptimizer(RisingSeriesFetcher(bars: 22), minimumTrades: 100).RunAsync();

            Assert.Null(result.Best);
        }

        [Fact]
        public async Task RunAsync_FlagsTrialWithFewerRoundTripsThanMinimumIneligible()
        {
            // BuySellQtyStrategy completes a single round trip, so one Trade is below a minimum of five.
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1);

            OptimizationResult result = await QtyOptimizer(RisingAaplFetcher(), space, minimumTrades: 5).RunAsync();

            Assert.False(result.Trials.Single().Eligible);
        }

        [Fact]
        public async Task RunAsync_ReportsProgressForEveryTrialEndingAtTheTotal()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 5, step: 1);
            RecordingProgress progress = new();

            await QtyOptimizer(RisingAaplFetcher(), space).RunAsync(progress);

            // One report per completed Trial, every report carries the full total, and the last Trial to
            // complete drives Completed up to that total.
            Assert.Equal(5, progress.Reports.Count);
            Assert.All(progress.Reports, report => Assert.Equal(5, report.Total));
            Assert.Equal(5, progress.Reports.Max(report => report.Completed));
        }

        [Fact]
        public async Task RunAsync_WithTiedScores_RanksTrialsInParameterSpaceOrderDeterministically()
        {
            // A constant Objective ties every Trial's Score, so ranking order is decided purely by the order
            // results are collected in. Parallel execution must still reproduce the sequential result: the
            // Parameter-space (Expand) order, identically on every run.
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 12, step: 1);
            Objective allTied = Objective.Maximize(stats => 0m);

            OptimizationResult first = await QtyOptimizer(RisingAaplFetcher(), space, objective: allTied).RunAsync();
            OptimizationResult second = await QtyOptimizer(RisingAaplFetcher(), space, objective: allTied).RunAsync();

            int[] expected = Enumerable.Range(1, 12).ToArray();
            Assert.Equal(expected, first.Trials.Select(trial => trial.Parameters.Int("qty")));
            Assert.Equal(expected, second.Trials.Select(trial => trial.Parameters.Int("qty")));
        }

        [Fact]
        public async Task RunAsync_WithCancelledToken_PropagatesCancellation()
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1);
            using CancellationTokenSource cts = new();
            cts.Cancel();

            // A cancelled token stops the sweep and surfaces as an OperationCanceledException, not a completed run.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => QtyOptimizer(RisingAaplFetcher(), space).RunAsync(progress: null, ct: cts.Token));
        }

        /// <summary>Records every progress report synchronously and thread-safely, avoiding Progress&lt;T&gt;'s async posting to a captured context.</summary>
        private sealed class RecordingProgress : IProgress<OptimizationProgress>
        {
            private readonly object _gate = new();
            private readonly List<OptimizationProgress> _reports = new();

            /// <summary>Gets a snapshot of the reports received so far.</summary>
            public IReadOnlyList<OptimizationProgress> Reports
            {
                get
                {
                    lock (_gate)
                    {
                        return _reports.ToList();
                    }
                }
            }

            /// <summary>Records one progress report; called synchronously from the sweep, possibly on many threads.</summary>
            public void Report(OptimizationProgress value)
            {
                lock (_gate)
                {
                    _reports.Add(value);
                }
            }
        }

        /// <summary>Buys a fixed quantity of every symbol on its first bar, then sells the same quantity on its next.</summary>
        private class BuySellQtyStrategy : StrategyBase
        {
            private readonly int _quantity;
            private bool _bought;
            private bool _sold;

            public BuySellQtyStrategy(int quantity)
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

        /// <summary>
        /// Performs exactly <c>roundTrips</c> buy-then-sell cycles at a fixed quantity, submitting one order
        /// per bar (buy on even orders, sell on odd), so a rising series yields that many completed Round trips.
        /// </summary>
        private class NRoundTripStrategy : StrategyBase
        {
            private readonly int _roundTrips;
            private readonly int _quantity;
            private int _ordersSubmitted;

            public NRoundTripStrategy(int roundTrips, int quantity)
            {
                _roundTrips = roundTrips;
                _quantity = quantity;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_ordersSubmitted >= _roundTrips * 2)
                {
                    return;
                }

                OrderSide side = _ordersSubmitted % 2 == 0 ? OrderSide.Buy : OrderSide.Sell;
                broker.Submit(new OrderRequest { Symbol = symbol, Side = side, Type = OrderType.Market, Quantity = _quantity });
                _ordersSubmitted++;
            }
        }
    }
}
