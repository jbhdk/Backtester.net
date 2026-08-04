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
using BacktestEngine = Backtester.Engine.Engine;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of the <see cref="Optimizer"/> over a cross-currency Portfolio (ADR 0032, issue #127): the
    /// sweep pre-fetches the Conversion symbols its portfolio factory's own Portfolio declares alongside the
    /// tradable symbols, so every Trial reads the rate series an equivalent single run would read — while the
    /// Conversion series itself stays plumbing, invisible to the strategy and to the Trial's reported symbols.
    /// </summary>
    public class OptimizerConversionSeriesTests
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

        /// <summary>
        /// A JPY-quoted EUR_JPY series rising 100 JPY a bar, paired with a flat USD_JPY rate of 150, so a
        /// one-unit round trip entered at bar 1 and exited at bar 2 realizes 100 JPY — $0.6666… in the account.
        /// </summary>
        private static IHistoricalDataFetcher CrossCurrencyFetcher()
        {
            return FetcherReturning(
                ("EUR_JPY", new[] { Bar(T0, 15_000m), Bar(T0.AddDays(1), 15_100m), Bar(T0.AddDays(2), 15_200m) }),
                ("USD_JPY", new[] { Bar(T0, 150m), Bar(T0.AddDays(1), 150m), Bar(T0.AddDays(2), 150m) }));
        }

        /// <summary>A USD account whose only Instrument is JPY-quoted, converting through USD_JPY.</summary>
        private static Portfolio CrossCurrencyPortfolio()
        {
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            return new Portfolio(100_000m, "USD", instruments);
        }

        /// <summary>
        /// Builds an Optimizer over a single JPY-quoted tradable symbol and a "qty" grid, handed only that
        /// tradable symbol: the Conversion series must come from the portfolio factory alone.
        /// </summary>
        private static Optimizer CrossCurrencyOptimizer(IHistoricalDataFetcher fetcher, ParameterSpace space = null)
        {
            return new Optimizer(
                fetcher,
                new[] { "EUR_JPY" },
                T0,
                T0.AddYears(1),
                "1d",
                CrossCurrencyPortfolio,
                space ?? new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1),
                (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);
        }

        [Fact]
        public async Task RunAsync_PortfolioFactoryDeclaringConversionSymbol_PreFetchesThatSeries()
        {
            IHistoricalDataFetcher fetcher = CrossCurrencyFetcher();

            await CrossCurrencyOptimizer(fetcher).RunAsync();

            A.CallTo(() => fetcher.FetchAsync("USD_JPY", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappened();
        }

        [Fact]
        public async Task RunAsync_PortfolioFactoryDeclaringConversionSymbol_CompletesWithEveryTrialScored()
        {
            OptimizationResult result = await CrossCurrencyOptimizer(CrossCurrencyFetcher()).RunAsync();

            // Every Parameter set is scored on its own converted stats: none is rejected, and the sweep
            // reaches a Best rather than ending on the first conversion.
            Assert.Equal(3, result.Trials.Count);
            Assert.All(result.Trials, trial => Assert.False(trial.IsRejected));
            Assert.All(result.Trials, trial => Assert.Equal(1, trial.Stats.Trades));
            Assert.NotNull(result.Best);
        }

        [Fact]
        public async Task RunAsync_WinningTrialStats_MatchASingleRunOfTheSameParameters()
        {
            ParameterSpace singlePoint = new ParameterSpace().AddInt("qty", from: 3, to: 3, step: 1);

            OptimizationResult result = await CrossCurrencyOptimizer(CrossCurrencyFetcher(), singlePoint).RunAsync();

            Portfolio portfolio = CrossCurrencyPortfolio();
            BrokerSimulator broker = new(portfolio);
            BacktestEngine engine = new(
                CrossCurrencyFetcher(), new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d",
                new BuySellQtyStrategy(3), broker, portfolio);
            await engine.StartAsync();
            PerformanceStats singleRun = portfolio.GetPerformanceStats();

            // Three units entered at 15100 and exited at 15200 realize 300 JPY, converted at USD_JPY 150 —
            // pinned so the equality below cannot be satisfied by two runs that both did nothing.
            Assert.Equal(2m, singleRun.NetProfit);
            Assert.Equal(singleRun.NetProfit, result.Best.Stats.NetProfit);
            Assert.Equal(singleRun.Trades, result.Best.Stats.Trades);
            Assert.Equal(singleRun.Sharpe, result.Best.Stats.Sharpe);
        }

        [Fact]
        public async Task RunAsync_WithBarCountWarmup_ResolvesTheConversionSeriesDataStartLikeATradableSymbol()
        {
            // A Conversion series is not exempt from warmup (ADR 0032): it is resolved through the same seam,
            // once, and fetched from the date that resolution returns — so a Trial reads precisely the bars an
            // equivalent single run reads.
            DateTime testFrom = T0.AddDays(2);
            DateTime dataStart = T0;
            IWarmupResolvingFetcher fetcher = A.Fake<IWarmupResolvingFetcher>();
            A.CallTo(() => fetcher.ResolveWarmupStartAsync(A<string>._, testFrom, 2, "1d", A<CancellationToken>._))
                .Returns(Task.FromResult(dataStart));
            A.CallTo(() => fetcher.FetchAsync("EUR_JPY", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(DailySeries(15_000m, 100m)));
            A.CallTo(() => fetcher.FetchAsync("USD_JPY", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(DailySeries(150m, 0m)));

            Optimizer optimizer = new(
                fetcher,
                new[] { "EUR_JPY" },
                testFrom,
                T0.AddYears(1),
                2,
                "1d",
                CrossCurrencyPortfolio,
                new ParameterSpace().AddInt("qty", from: 1, to: 2, step: 1),
                (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);
            await optimizer.RunAsync();

            A.CallTo(() => fetcher.ResolveWarmupStartAsync("USD_JPY", testFrom, 2, "1d", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => fetcher.FetchAsync("USD_JPY", dataStart, A<DateTime>._, "1d", A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task RunAsync_PortfolioDeclaringNoInstruments_FetchesExactlyItsSymbolsAndNoMore()
        {
            // The common (non-forex) path is untouched: a Portfolio declaring no conversion adds no series to
            // the shared fetch, so the sweep still reaches the fetcher once per tradable symbol and never else.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));

            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                T0,
                T0.AddYears(1),
                "1d",
                () => new Portfolio(100_000m),
                new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1),
                (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);
            await optimizer.RunAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => fetcher.FetchAsync(A<string>.That.Not.IsEqualTo("AAPL"), A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task RunAsync_ConversionSeries_NeverReachesATrialsOnStartHistory()
        {
            // The Conversion series is plumbing the sweep fetches so positions can be valued; a strategy that
            // never declared USD_JPY tradable must not see it when precomputing.
            HistoryCapturingStrategy captured = new();
            Optimizer optimizer = new(
                CrossCurrencyFetcher(),
                new[] { "EUR_JPY" },
                T0,
                T0.AddYears(1),
                "1d",
                CrossCurrencyPortfolio,
                new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1),
                (parameters, portfolio) => (captured, new BrokerSimulator(portfolio)),
                minimumTrades: 0);

            await optimizer.RunAsync();

            Assert.Equal(new[] { "EUR_JPY" }, captured.ReceivedHistory.Keys);
        }

        [Fact]
        public async Task RunAsync_ConversionSeries_NeverAppearsInATrialsReportedSymbols()
        {
            ParameterSpace singlePoint = new ParameterSpace().AddInt("qty", from: 1, to: 1, step: 1);
            Optimizer optimizer = new(
                CrossCurrencyFetcher(),
                new[] { "EUR_JPY" },
                T0,
                T0.AddYears(1),
                "1d",
                CrossCurrencyPortfolio,
                singlePoint,
                (parameters, portfolio) => (new BuySellQtyStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                retainAllBacktestResults: true,
                minimumTrades: 0);

            OptimizationResult result = await optimizer.RunAsync();

            BacktestResult backtest = result.Trials.Single().BacktestResult;
            Assert.Equal(new[] { "EUR_JPY" }, backtest.Symbols);
            Assert.Equal(new[] { "EUR_JPY" }, backtest.CandleHistory.Keys);
        }

        /// <summary>Five daily bars from <paramref name="first"/>, each <paramref name="step"/> above the last.</summary>
        private static Candle[] DailySeries(decimal first, decimal step)
        {
            return Enumerable.Range(0, 5).Select(index => Bar(T0.AddDays(index), first + step * index)).ToArray();
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

        /// <summary>Captures the history handed to OnStart so a test can assert which symbols reached it.</summary>
        private sealed class HistoryCapturingStrategy : StrategyBase
        {
            /// <summary>Gets the per-symbol history the engine handed this strategy at OnStart.</summary>
            public IReadOnlyDictionary<string, IReadOnlyList<Candle>> ReceivedHistory { get; private set; }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                ReceivedHistory = history;
            }

            /// <summary>Trades nothing: this strategy exists only to capture the history it was started with.</summary>
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
            }
        }
    }
}
