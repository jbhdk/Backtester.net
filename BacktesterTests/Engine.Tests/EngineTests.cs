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
    public class EngineTests
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

        [Fact]
        public async Task StartAsync_ReturnsResult_CarryingTheRunPortfolio()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.NotNull(result);
            Assert.Same(portfolio, result.Portfolio);
        }

        [Fact]
        public async Task StartAsync_Result_CarriesExactCandleSeriesRunOn()
        {
            Candle[] bars = { Bar(T0, 100m), Bar(T0.AddDays(1), 101m) };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", bars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.True(result.CandleHistory.ContainsKey("AAPL"));
            Assert.Same(bars, result.CandleHistory["AAPL"]);
        }

        [Fact]
        public async Task StartAsync_Result_CarriesHistoryForEverySymbol()
        {
            Candle[] aaplBars = { Bar(T0, 100m) };
            Candle[] msftBars = { Bar(T0, 200m) };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", aaplBars), ("MSFT", msftBars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.Equal(2, result.CandleHistory.Count);
            Assert.Same(aaplBars, result.CandleHistory["AAPL"]);
            Assert.Same(msftBars, result.CandleHistory["MSFT"]);
        }

        [Fact]
        public async Task StartAsync_Result_HasEmptyIndicators()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.NotNull(result.Indicators);
            Assert.Empty(result.Indicators);
        }

        [Fact]
        public async Task StartAsync_CollectsExposedIndicators_OntoResult()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            ExposesOneIndicatorStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Indicator indicator = Assert.Single(result.Indicators);
            Assert.Equal("SMA", indicator.Name);
            Assert.Equal(IndicatorPane.PriceOverlay, indicator.Pane);
        }

        [Fact]
        public async Task StartAsync_NonIndicatorSourceStrategy_YieldsEmptyIndicators()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new RawStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.Empty(result.Indicators);
        }

        [Fact]
        public async Task StartAsync_SingleSymbol_RecordsOneEquitySnapshotPerBar()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 101m), Bar(T0.AddDays(2), 102m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(3, portfolio.EquityHistory.Count);
        }

        [Fact]
        public async Task StartAsync_InvokesOnStart_BeforeFirstOnBar()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 100m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            CallOrderTrackingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.True(strategy.OnStartWasCalled);
            Assert.True(strategy.OnStartCalledBeforeOnBar);
        }

        [Fact]
        public async Task StartAsync_PassesFullFetchedHistory_ToOnStart()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 101m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            CallOrderTrackingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.NotNull(strategy.ReceivedHistory);
            Assert.True(strategy.ReceivedHistory.ContainsKey("AAPL"));
            Assert.Equal(2, strategy.ReceivedHistory["AAPL"].Count);
        }

        [Fact]
        public async Task StartAsync_MarketOrderOnFirstBar_FillsAtNextBarOpen()
        {
            Candle bar1 = new() { Timestamp = T0, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000 };
            Candle bar2 = new() { Timestamp = T0.AddDays(1), Open = 120m, High = 130m, Low = 115m, Close = 125m, Volume = 1000 };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { bar1, bar2 }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new AlwaysBuyOneShare(), broker, portfolio);
            await engine.StartAsync();

            Assert.Single(portfolio.Positions);
            Assert.Equal(120m, portfolio.Positions[0].AveragePrice);
        }

        [Fact]
        public async Task StartAsync_OrderOnLastBar_NeverFills()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new AlwaysBuyOneShare(), broker, portfolio);
            await engine.StartAsync();

            Assert.Empty(portfolio.Positions);
        }

        [Fact]
        public async Task StartAsync_StrategyBuys_CreatesPositionAndReducesCash()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 150m), Bar(T0.AddDays(1), 155m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new AlwaysBuyOneShare(), broker, portfolio);
            await engine.StartAsync();

            Assert.Single(portfolio.Positions);
            Assert.Equal("AAPL", portfolio.Positions[0].Symbol);
            Assert.True(portfolio.Cash < 10_000m);
        }

        [Fact]
        public async Task StartAsync_StrategyDoesNothing_PortfolioUnchanged()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            Assert.Empty(portfolio.Positions);
            Assert.Equal(10_000m, portfolio.Cash);
        }

        [Fact]
        public async Task StartAsync_StrategyReceivesSnapshot_WithCurrentCash()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            SnapshotCapturingStrategy spy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", spy, broker, portfolio);
            await engine.StartAsync();

            Assert.NotNull(spy.LastSnapshot);
            Assert.Equal(10_000m, spy.LastSnapshot.Cash);
        }

        [Fact]
        public async Task StartAsync_TwoSymbolsFiveBars_BuyAaplOnFirstBar_FinalSnapshotReflectsPosition()
        {
            Candle[] aaplBars = BuildSeries(5, 100m);
            Candle[] msftBars = BuildSeries(5, 200m);
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", aaplBars), ("MSFT", msftBars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, T0, T0.AddYears(1), "1d", new BuyAaplOnFirstBarOnly(), broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(5, portfolio.EquityHistory.Count);

            EquitySnapshot final = portfolio.EquityHistory[4];
            Assert.True(final.Cash < 10_000m, "Cash should be reduced by the AAPL purchase");
            Assert.True(final.UnrealizedPnL > 0m, "Open AAPL position should have market value");
            Assert.Equal(final.Cash + final.UnrealizedPnL, final.MarkedEquity);
        }

        [Fact]
        public async Task StartAsync_StopCalledFromStrategy_HaltsLoopAfterCurrentBar()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 101m), Bar(T0.AddDays(2), 102m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            StopAfterOneBarStrategy stopAfterFirstBar = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", stopAfterFirstBar, broker, portfolio);
            stopAfterFirstBar.Engine = engine;

            await engine.StartAsync();

            Assert.Equal(1, portfolio.EquityHistory.Count);
        }

        [Fact]
        public async Task StartAsync_FetchesEverySymbol()
        {
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m) }),
                ("MSFT", new[] { Bar(T0, 200m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._)).MustHaveHappened();
            A.CallTo(() => fetcher.FetchAsync("MSFT", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._)).MustHaveHappened();
        }

        private static Candle[] BuildSeries(int count, decimal startClose)
        {
            Candle[] bars = new Candle[count];
            for (int i = 0; i < count; i++)
            {
                bars[i] = Bar(T0.AddDays(i), startClose + i);
            }

            return bars;
        }

        // --- Stub strategies ---

        private class DoNothingStrategy : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>A strategy that implements IStrategy directly, without the IIndicatorSource seam.</summary>
        private class RawStrategy : IStrategy
        {
            public void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history) { }

            public void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>Exposes a single price-overlay indicator series during OnStart.</summary>
        private class ExposesOneIndicatorStrategy : StrategyBase
        {
            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                RecordIndicator("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        private class BuyAaplOnFirstBarOnly : StrategyBase
        {
            private bool _bought;

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought && symbol == "AAPL")
                {
                    _bought = true;
                    broker.Submit(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
            }
        }

        private class StopAfterOneBarStrategy : StrategyBase
        {
            public Backtester.Engine.IEngine Engine { get; set; }
            private bool _stopped;

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_stopped)
                {
                    Engine.Stop();
                    _stopped = true;
                }
            }
        }

        private class SnapshotCapturingStrategy : StrategyBase
        {
            public PortfolioSnapshot LastSnapshot { get; private set; }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                LastSnapshot = snapshot;
            }
        }

        private class AlwaysBuyOneShare : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
            }
        }

        /// <summary>Tracks that OnStart is called before OnBar and captures the history it received.</summary>
        private class CallOrderTrackingStrategy : StrategyBase
        {
            public bool OnStartWasCalled { get; private set; }
            public bool OnStartCalledBeforeOnBar { get; private set; }
            public IReadOnlyDictionary<string, IReadOnlyList<Candle>> ReceivedHistory { get; private set; }
            private bool _onBarWasCalled;

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                OnStartWasCalled = true;
                OnStartCalledBeforeOnBar = !_onBarWasCalled;
                ReceivedHistory = history;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                _onBarWasCalled = true;
            }
        }
    }
}
