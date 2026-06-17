using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using Backtester.Strategies;
using BacktestEngine = Backtester.Engine.Engine;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    public class EngineTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private static IMarketDataFeed SingleSymbolFeed(string symbol, params Candle[] candles)
        {
            return MarketDataSynchronizer.CreateFromSeries(
                new Dictionary<string, IReadOnlyList<Candle>> { [symbol] = candles });
        }

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new() { Timestamp = ts, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };
        }


        [Fact]
        public void Start_InvokesOnStart_BeforeFirstOnBar()
        {
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 100m));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            CallOrderTrackingStrategy strategy = new();

            BacktestEngine engine = new(feed, strategy, broker, portfolio);
            engine.Start();

            Assert.True(strategy.OnStartWasCalled);
            Assert.True(strategy.OnStartCalledBeforeOnBar);
        }

        [Fact]
        public void Start_PassesFullFeedHistory_ToOnStart()
        {
            Candle bar1 = Bar(T0, 100m);
            Candle bar2 = Bar(T0.AddDays(1), 101m);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", bar1, bar2);
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            CallOrderTrackingStrategy strategy = new();

            BacktestEngine engine = new(feed, strategy, broker, portfolio);
            engine.Start();

            Assert.NotNull(strategy.ReceivedHistory);
            Assert.True(strategy.ReceivedHistory.ContainsKey("AAPL"));
            Assert.Equal(2, strategy.ReceivedHistory["AAPL"].Count);
        }

        [Fact]
        public void RunOnce_OrderEmittedOnBarN_DoesNotFillOnSameBar()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            BacktestEngine engine = new(feed, new AlwaysBuyOneShare(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.Empty(portfolio.Positions);
        }

        [Fact]
        public void RunOnce_MarketOrderEmittedOnBar1_FillsAtBar2Open()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            Candle bar1 = new() { Timestamp = T0, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000 };
            Candle bar2 = new() { Timestamp = T0.AddDays(1), Open = 120m, High = 130m, Low = 115m, Close = 125m, Volume = 1000 };
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", bar1, bar2);
            BacktestEngine engine = new(feed, new AlwaysBuyOneShare(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();
            feed.Advance();
            engine.RunOnce();

            Assert.Single(portfolio.Positions);
            Assert.Equal(120m, portfolio.Positions[0].AveragePrice);
        }

        [Fact]
        public void RunOnce_StubStrategyBuys_CreatesPositionAndReducesCash()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 150m),
                Bar(T0.AddDays(1), 155m));
            BacktestEngine engine = new(feed, new AlwaysBuyOneShare(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();
            feed.Advance();
            engine.RunOnce();

            Assert.Single(portfolio.Positions);
            Assert.Equal("AAPL", portfolio.Positions[0].Symbol);
            Assert.True(portfolio.Cash < 10_000m);
        }

        [Fact]
        public void RunOnce_StrategyReceivesSnapshot_WithCurrentCash()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            SnapshotCapturingStrategy spy = new();
            BacktestEngine engine = new(feed, spy, broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.NotNull(spy.LastSnapshot);
            Assert.Equal(10_000m, spy.LastSnapshot.Cash);
        }

        [Fact]
        public void RunOnce_StrategyReturnsNoOrders_PortfolioUnchanged()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            BacktestEngine engine = new(feed, new DoNothingStrategy(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.Empty(portfolio.Positions);
            Assert.Equal(10_000m, portfolio.Cash);
        }

        [Fact]
        public void Start_TwoSymbolFiveBars_BuyOnBar1_FinalSnapshotReflectsPosition()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            Candle[] aaplBars = Enumerable.Range(0, 5)
                .Select(i => Bar(T0.AddDays(i), 100m + i))
                .ToArray();
            Candle[] msftBars = Enumerable.Range(0, 5)
                .Select(i => Bar(T0.AddDays(i), 200m + i))
                .ToArray();

            IMarketDataFeed feed = MarketDataSynchronizer.CreateFromSeries(
                new Dictionary<string, IReadOnlyList<Candle>>
                {
                    ["AAPL"] = aaplBars,
                    ["MSFT"] = msftBars
                });

            BacktestEngine engine = new(feed, new BuyAaplOnFirstBarOnly(), broker, portfolio);

            engine.Start();

            Assert.Equal(5, portfolio.EquityHistory.Count);

            EquitySnapshot final = portfolio.EquityHistory[4];
            Assert.True(final.Cash < 10_000m, "Cash should be reduced by the AAPL purchase");
            Assert.True(final.UnrealizedPnL > 0m, "Open AAPL position should have market value");
            Assert.Equal(final.Cash + final.UnrealizedPnL, final.MarkedEquity);
        }

        [Fact]
        public void Start_ThreeBarFeed_EquityHistoryHasThreeEntries()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 100m),
                Bar(T0.AddDays(1), 101m),
                Bar(T0.AddDays(2), 102m));
            BacktestEngine engine = new(feed, new DoNothingStrategy(), broker, portfolio);

            engine.Start();

            Assert.Equal(3, portfolio.EquityHistory.Count);
        }

        [Fact]
        public void Stop_HaltsLoopAfterCurrentTick()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 100m),
                Bar(T0.AddDays(1), 101m),
                Bar(T0.AddDays(2), 102m));

            StopAfterOneBarStrategy stopAfterFirstBar = new();
            BacktestEngine engine = new(feed, stopAfterFirstBar, broker, portfolio);
            stopAfterFirstBar.Engine = engine;

            engine.Start();

            Assert.Equal(1, portfolio.EquityHistory.Count);
        }

        // --- Stub strategies ---

        /// <summary>Tracks that OnStart is called before OnBar.</summary>
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

        private class AlwaysBuyOneShare : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
            }
        }

        private class DoNothingStrategy : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        private class SnapshotCapturingStrategy : StrategyBase
        {
            public PortfolioSnapshot LastSnapshot { get; private set; }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                LastSnapshot = snapshot;
            }
        }

        private class StopAfterOneBarStrategy : StrategyBase
        {
            public IEngine Engine { get; set; }
            private bool _stopped;

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_stopped) { Engine.Stop(); _stopped = true; }
            }
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
    }
}
