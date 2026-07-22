using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Optimization;
using Backtester.Report;
using Backtester.Report.Toolkit;
using Backtester.Strategies;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of the attributes-first authoring path exercised through its public API:
    /// <see cref="Optimize.For{TParameters}"/> reflects <c>[Optimize]</c>-decorated Parameters into a
    /// <see cref="ParameterSpace"/> and an adapting Trial factory, bundled as an
    /// <see cref="OptimizationSetup"/>.
    /// </summary>
    public class OptimizeTests
    {
        [Fact]
        public void FromAttributes_IntParameter_BuildsAxisOverMinMaxStep()
        {
            OptimizationSetup setup = Optimize
                .For(new FastParams(), (parameters, portfolio) => (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio)))
                .FromAttributes();

            Assert.Equal(new[] { 5, 10, 15 }, setup.Space.Expand().Select(set => set.Int("Fast")));
        }

        [Fact]
        public void FromAttributes_DecimalParameter_BuildsSteppedInclusiveAxis()
        {
            OptimizationSetup setup = Optimize
                .For(new RiskParams(), (parameters, portfolio) => (new RiskStrategy(parameters.RiskFraction), new BrokerSimulator(portfolio)))
                .FromAttributes();

            Assert.Equal(new[] { 0.5m, 1.0m, 1.5m }, setup.Space.Expand().Select(set => set.Decimal("RiskFraction")));
        }

        [Fact]
        public void FromAttributes_ParameterlessOptimizeOnBool_ExpandsToFalseAndTrue()
        {
            OptimizationSetup setup = Optimize
                .For(new FlagParams(), (parameters, portfolio) => (new FlagStrategy(parameters.UseTrailing), new BrokerSimulator(portfolio)))
                .FromAttributes();

            Assert.Equal(new[] { false, true }, setup.Space.Expand().Select(set => set.Bool("UseTrailing")));
        }

        [Fact]
        public void FromAttributes_TwoParameters_BuildsCartesianProductKeyedByPropertyName()
        {
            OptimizationSetup setup = Optimize
                .For(new TwoParams(), (parameters, portfolio) => (new TwoStrategy(parameters.Fast, parameters.Slow), new BrokerSimulator(portfolio)))
                .FromAttributes();

            IEnumerable<(int Fast, int Slow)> combinations = setup.Space.Expand().Select(set => (set.Int("Fast"), set.Int("Slow")));
            Assert.Equal(new[] { (1, 10), (1, 20), (2, 10), (2, 20) }, combinations);
        }

        [Fact]
        public void TrialFactory_SetsTheSweptInitOnlyPropertyOnTheClone()
        {
            MixedParams received = null;
            OptimizationSetup setup = Optimize
                .For(
                    new MixedParams { Label = "base" },
                    (parameters, portfolio) =>
                    {
                        received = parameters;
                        return (new FastStrategy(parameters.Qty), new BrokerSimulator(portfolio));
                    })
                .FromAttributes();

            ParameterSet set = setup.Space.Expand().First(candidate => candidate.Int("Qty") == 2);
            setup.TrialFactory(set, new Portfolio(100_000m));

            Assert.Equal(2, received.Qty);
        }

        [Fact]
        public void TrialFactory_CarriesNonOptimizedPropertiesThroughFromTheInstance()
        {
            MixedParams received = null;
            OptimizationSetup setup = Optimize
                .For(
                    new MixedParams { Label = "base" },
                    (parameters, portfolio) =>
                    {
                        received = parameters;
                        return (new FastStrategy(parameters.Qty), new BrokerSimulator(portfolio));
                    })
                .FromAttributes();

            setup.TrialFactory(setup.Space.Expand().First(), new Portfolio(100_000m));

            Assert.Equal("base", received.Label);
        }

        /// <summary>A parameters class with two integer Parameters, one carried through and one swept.</summary>
        private class TwoParams
        {
            [Optimize(1, 2, 1)]
            public int Fast { get; init; }

            [Optimize(10, 20, 10)]
            public int Slow { get; init; }
        }

        /// <summary>An integer Parameter that is swept alongside a non-optimized property that must carry through.</summary>
        private class MixedParams
        {
            [Optimize(1, 3, 1)]
            public int Qty { get; init; }

            /// <summary>A non-optimized Parameter; the clone must preserve its value from the bound instance.</summary>
            public string Label { get; init; }
        }

        /// <summary>A minimal strategy that captures two integer arguments so a probe sees both consumed.</summary>
        private class TwoStrategy : StrategyBase
        {
            private readonly int _fast;
            private readonly int _slow;

            public TwoStrategy(int fast, int slow)
            {
                _fast = fast;
                _slow = slow;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
            }
        }

        [Fact]
        public void FromAttributes_PropertyCarryingReportSetting_StillBuildsItsAxis()
        {
            OptimizationSetup setup = Optimize
                .For(new CoDecoratedParams(), (parameters, portfolio) => (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio)))
                .FromAttributes();

            Assert.Equal(new[] { 5, 10, 15 }, setup.Space.Expand().Select(set => set.Int("Fast")));
        }

        [Fact]
        public void TrialFactory_CoDecoratedClone_RemainsRenderableByTheReportToolkit()
        {
            CoDecoratedParams received = null;
            OptimizationSetup setup = Optimize
                .For(
                    new CoDecoratedParams(),
                    (parameters, portfolio) =>
                    {
                        received = parameters;
                        return (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio));
                    })
                .FromAttributes();

            setup.TrialFactory(setup.Space.Expand().First(candidate => candidate.Int("Fast") == 10), new Portfolio(100_000m));
            IReadOnlyList<ReportCard> cards = new ConfigurationCardBuilder().Build(received);

            // The [ReportSetting] still renders the property — with the value [Optimize] swept onto the clone.
            Assert.Contains(cards.SelectMany(card => card.Rows), row => row[0] == "Fast period" && row[1] == "10");
        }

        /// <summary>A property carrying both [Optimize] and [ReportSetting] to prove the two do not interact.</summary>
        private class CoDecoratedParams
        {
            [Optimize(5, 15, 5)]
            [ReportSetting("Fast period", "Strategy")]
            public int Fast { get; init; }
        }

        [Fact]
        public void FromAttributes_ParameterTheFactoryNeverConsumes_IsRejected()
        {
            // The factory reads Fast but ignores the [Optimize] 'Unused' Parameter, so varying Unused would
            // silently produce identical Trials — the builder must reject it instead.
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Optimize
                .For(new UnconsumedParams(), (parameters, portfolio) => (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio)))
                .FromAttributes());

            Assert.Contains("Unused", error.Message);
        }

        /// <summary>Two integer Parameters where the factory consumes only <c>Fast</c>, leaving <c>Unused</c> inert.</summary>
        private class UnconsumedParams
        {
            [Optimize(5, 15, 5)]
            public int Fast { get; init; }

            [Optimize(1, 3, 1)]
            public int Unused { get; init; }
        }

        [Fact]
        public async Task Optimizer_FromAttributesSetup_RunsATrialPerParameterSetAndScalesResults()
        {
            OptimizationSetup setup = Optimize
                .For(new QtyParams(), (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Qty), new BrokerSimulator(portfolio)))
                .FromAttributes();

            Optimizer optimizer = new(
                RisingAaplFetcher(),
                new[] { "AAPL" },
                T0,
                T0.AddYears(1),
                "1d",
                () => new Portfolio(100_000m),
                setup,
                minimumTrades: 0);

            OptimizationResult result = await optimizer.RunAsync();

            // Attribute-driven expansion yields one Trial per swept Qty, each scoring its own backtest:
            // a buy at bar 1 (110) sold at bar 2 (120) realizes +10 per share, so Qty scales net profit.
            Assert.Equal(new[] { 1, 2, 3 }, result.Trials.Select(trial => trial.Parameters.Int("Qty")).OrderBy(qty => qty));
            Assert.Equal(10m, result.Trials.First(trial => trial.Parameters.Int("Qty") == 1).Stats.NetProfit);
            Assert.Equal(30m, result.Trials.First(trial => trial.Parameters.Int("Qty") == 3).Stats.NetProfit);
        }

        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime timestamp, decimal close)
        {
            return new() { Timestamp = timestamp, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };
        }

        /// <summary>A three-bar rising AAPL series: a buy filled at bar 1 and sold at bar 2 realizes +10/share.</summary>
        private static IHistoricalDataFetcher RisingAaplFetcher()
        {
            IReadOnlyList<Candle> candles = new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) };
            IHistoricalDataFetcher fetcher = A.Fake<IHistoricalDataFetcher>();
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult(candles));
            return fetcher;
        }

        /// <summary>A parameters class with a single integer Parameter swept from 1 to 3 in steps of 1.</summary>
        private class QtyParams
        {
            [Optimize(1, 3, 1)]
            public int Qty { get; init; }
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

        /// <summary>A parameters class with a single integer Parameter swept from 5 to 15 in steps of 5.</summary>
        private class FastParams
        {
            [Optimize(5, 15, 5)]
            public int Fast { get; init; }
        }

        /// <summary>A parameters class with a single decimal Parameter swept from 0.5 to 1.5 in steps of 0.5.</summary>
        private class RiskParams
        {
            [Optimize(0.5, 1.5, 0.5)]
            public decimal RiskFraction { get; init; }
        }

        /// <summary>A parameters class with a single boolean Parameter carrying the parameterless [Optimize].</summary>
        private class FlagParams
        {
            [Optimize]
            public bool UseTrailing { get; init; }
        }

        /// <summary>A minimal strategy that captures its constructor argument so a probe sees it consumed.</summary>
        private class FastStrategy : StrategyBase
        {
            private readonly int _fast;

            public FastStrategy(int fast)
            {
                _fast = fast;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
            }
        }

        /// <summary>A minimal strategy that captures its decimal constructor argument so a probe sees it consumed.</summary>
        private class RiskStrategy : StrategyBase
        {
            private readonly decimal _riskFraction;

            public RiskStrategy(decimal riskFraction)
            {
                _riskFraction = riskFraction;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
            }
        }

        /// <summary>A minimal strategy that captures its boolean constructor argument so a probe sees it consumed.</summary>
        private class FlagStrategy : StrategyBase
        {
            private readonly bool _useTrailing;

            public FlagStrategy(bool useTrailing)
            {
                _useTrailing = useTrailing;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
            }
        }
    }
}
