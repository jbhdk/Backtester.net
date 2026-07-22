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
            bool retainAllBacktestResults = false)
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
                retainAllBacktestResults);
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
    }
}
