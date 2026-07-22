using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Optimization;
using Backtester.Strategies;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of the fluent <c>.Vary()</c> authoring path exercised through its public API: it names a
    /// Parameter by expression selector, adds its axis to the same <see cref="ParameterSpace"/> the
    /// attributes path builds, and terminates with <c>.Build()</c> into an <see cref="OptimizationSetup"/>.
    /// </summary>
    public class OptimizeVaryTests
    {
        [Fact]
        public void Vary_IntParameter_BuildsAxisOverFromToStep()
        {
            OptimizationSetup setup = Optimize
                .For(new FastParams(), (parameters, portfolio) => (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio)))
                .Vary(parameters => parameters.Fast, 5, 15, 5)
                .Build();

            Assert.Equal(new[] { 5, 10, 15 }, setup.Space.Expand().Select(set => set.Int("Fast")));
        }

        [Fact]
        public void Vary_DecimalParameter_BuildsSteppedInclusiveAxis()
        {
            OptimizationSetup setup = Optimize
                .For(new RiskParams(), (parameters, portfolio) => (new RiskStrategy(parameters.RiskFraction), new BrokerSimulator(portfolio)))
                .Vary(parameters => parameters.RiskFraction, 0.5m, 1.5m, 0.5m)
                .Build();

            Assert.Equal(new[] { 0.5m, 1.0m, 1.5m }, setup.Space.Expand().Select(set => set.Decimal("RiskFraction")));
        }

        [Fact]
        public void Vary_TwoParameters_ComposeIntoCartesianProductKeyedByPropertyName()
        {
            OptimizationSetup setup = Optimize
                .For(new TwoParams(), (parameters, portfolio) => (new TwoStrategy(parameters.Fast, parameters.Slow), new BrokerSimulator(portfolio)))
                .Vary(parameters => parameters.Fast, 1, 2, 1)
                .Vary(parameters => parameters.Slow, 10, 20, 10)
                .Build();

            IEnumerable<(int Fast, int Slow)> combinations = setup.Space.Expand().Select(set => (set.Int("Fast"), set.Int("Slow")));
            Assert.Equal(new[] { (1, 10), (1, 20), (2, 10), (2, 20) }, combinations);
        }

        [Fact]
        public void TrialFactory_SetsTheVariedInitOnlyPropertyOnTheClone()
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
                .Vary(parameters => parameters.Qty, 1, 3, 1)
                .Build();

            ParameterSet set = setup.Space.Expand().First(candidate => candidate.Int("Qty") == 2);
            setup.TrialFactory(set, new Portfolio(100_000m));

            Assert.Equal(2, received.Qty);
        }

        [Fact]
        public void TrialFactory_CarriesNonVariedPropertiesThroughFromTheInstance()
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
                .Vary(parameters => parameters.Qty, 1, 3, 1)
                .Build();

            setup.TrialFactory(setup.Space.Expand().First(), new Portfolio(100_000m));

            Assert.Equal("base", received.Label);
        }

        [Fact]
        public void Vary_ParameterTheFactoryNeverConsumes_IsRejected()
        {
            // The factory reads Fast but ignores the varied 'Slow' Parameter, so varying Slow would silently
            // produce identical Trials — Build must reject it instead.
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Optimize
                .For(new TwoParams(), (parameters, portfolio) => (new FastStrategy(parameters.Fast), new BrokerSimulator(portfolio)))
                .Vary(parameters => parameters.Fast, 1, 2, 1)
                .Vary(parameters => parameters.Slow, 10, 20, 10)
                .Build());

            Assert.Contains("Slow", error.Message);
        }

        /// <summary>An integer Parameter that is varied alongside a non-varied property that must carry through.</summary>
        private class MixedParams
        {
            public int Qty { get; init; }

            /// <summary>A non-varied Parameter; the clone must preserve its value from the bound instance.</summary>
            public string Label { get; init; }
        }

        /// <summary>A parameters class with two integer Parameters, both varied fluently.</summary>
        private class TwoParams
        {
            public int Fast { get; init; }

            public int Slow { get; init; }
        }

        [Fact]
        public async Task Optimizer_TwoVarySweep_RunsATrialPerCombinationAndScoresEach()
        {
            OptimizationSetup setup = Optimize
                .For(new SweepParams(), (parameters, portfolio) => (new ScaledBuySellStrategy(parameters.Qty, parameters.Multiple), new BrokerSimulator(portfolio)))
                .Vary(parameters => parameters.Qty, 1, 2, 1)
                .Vary(parameters => parameters.Multiple, 1, 3, 2)
                .Build();

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

            // The two axes compose into four Trials; each buys Qty * Multiple shares at bar 1 (110) and sells
            // them at bar 2 (120), realizing +10 per share, so net profit tracks the product of the two axes.
            IEnumerable<(int Qty, int Multiple, decimal NetProfit)> outcomes = result.Trials
                .Select(trial => (trial.Parameters.Int("Qty"), trial.Parameters.Int("Multiple"), trial.Stats.NetProfit))
                .OrderBy(outcome => outcome.Item1).ThenBy(outcome => outcome.Item2);
            Assert.Equal(new[] { (1, 1, 10m), (1, 3, 30m), (2, 1, 20m), (2, 3, 60m) }, outcomes);
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

        /// <summary>A parameters class with two integer Parameters swept fluently by two <c>.Vary()</c> calls.</summary>
        private class SweepParams
        {
            public int Qty { get; init; }

            public int Multiple { get; init; }
        }

        /// <summary>Buys <c>Qty * Multiple</c> shares of every symbol on its first bar, then sells the same quantity on its next.</summary>
        private class ScaledBuySellStrategy : StrategyBase
        {
            private readonly int _quantity;
            private bool _bought;
            private bool _sold;

            public ScaledBuySellStrategy(int qty, int multiple)
            {
                _quantity = qty * multiple;
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

        /// <summary>A parameters class with a single integer Parameter, varied fluently rather than by attribute.</summary>
        private class FastParams
        {
            public int Fast { get; init; }
        }

        /// <summary>A parameters class with a single decimal Parameter, varied fluently rather than by attribute.</summary>
        private class RiskParams
        {
            public decimal RiskFraction { get; init; }
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
    }
}
