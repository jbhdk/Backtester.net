using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using BacktestEngine = Backtester.Engine.Engine;
using Backtester.Strategies;
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

        private static Candle Bar(DateTime ts, decimal close) =>
            new() { Timestamp = ts, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };

        [Fact]
        public void RunOnce_OrderEmittedOnBarN_DoesNotFillOnSameBar()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            BacktestEngine engine = new BacktestEngine(feed, new AlwaysBuyOneShare(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.Empty(portfolio.Positions);
        }

        [Fact]
        public void RunOnce_MarketOrderEmittedOnBar1_FillsAtBar2Open()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            Candle bar1 = new Candle { Timestamp = T0, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000 };
            Candle bar2 = new Candle { Timestamp = T0.AddDays(1), Open = 120m, High = 130m, Low = 115m, Close = 125m, Volume = 1000 };
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", bar1, bar2);
            BacktestEngine engine = new BacktestEngine(feed, new AlwaysBuyOneShare(), broker, portfolio);

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
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 150m),
                Bar(T0.AddDays(1), 155m));
            BacktestEngine engine = new BacktestEngine(feed, new AlwaysBuyOneShare(), broker, portfolio);

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
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            SnapshotCapturingStrategy spy = new SnapshotCapturingStrategy();
            BacktestEngine engine = new BacktestEngine(feed, spy, broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.NotNull(spy.LastSnapshot);
            Assert.Equal(10_000m, spy.LastSnapshot.Cash);
        }

        [Fact]
        public void RunOnce_StrategyReturnsNoOrders_PortfolioUnchanged()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL", Bar(T0, 150m));
            BacktestEngine engine = new BacktestEngine(feed, new DoNothingStrategy(), broker, portfolio);

            feed.Advance();
            engine.RunOnce();

            Assert.Empty(portfolio.Positions);
            Assert.Equal(10_000m, portfolio.Cash);
        }

        [Fact]
        public void Start_TwoSymbolFiveBars_BuyOnBar1_FinalSnapshotReflectsPosition()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);

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

            BacktestEngine engine = new BacktestEngine(feed, new BuyAaplOnFirstBarOnly(), broker, portfolio);

            engine.Start();

            Assert.Equal(5, portfolio.EquityHistory.Count);

            EquitySnapshot final = portfolio.EquityHistory[4];
            Assert.True(final.Cash < 10_000m, "Cash should be reduced by the AAPL purchase");
            Assert.True(final.UnrealizedPnL > 0m, "Open AAPL position should have market value");
            Assert.Equal(final.Cash + final.UnrealizedPnL, final.TotalEquity);
        }

        [Fact]
        public void Start_ThreeBarFeed_EquityHistoryHasThreeEntries()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 100m),
                Bar(T0.AddDays(1), 101m),
                Bar(T0.AddDays(2), 102m));
            BacktestEngine engine = new BacktestEngine(feed, new DoNothingStrategy(), broker, portfolio);

            engine.Start();

            Assert.Equal(3, portfolio.EquityHistory.Count);
        }

        [Fact]
        public void Stop_HaltsLoopAfterCurrentTick()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            IMarketDataFeed feed = SingleSymbolFeed("AAPL",
                Bar(T0, 100m),
                Bar(T0.AddDays(1), 101m),
                Bar(T0.AddDays(2), 102m));

            StopAfterOneBarStrategy stopAfterFirstBar = new StopAfterOneBarStrategy();
            BacktestEngine engine = new BacktestEngine(feed, stopAfterFirstBar, broker, portfolio);
            stopAfterFirstBar.Engine = engine;

            engine.Start();

            Assert.Equal(1, portfolio.EquityHistory.Count);
        }

        // --- Stub strategies ---

        private class AlwaysBuyOneShare : IStrategy
        {
            public IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
            {
                yield return new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 };
            }
        }

        private class DoNothingStrategy : IStrategy
        {
            public IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
            {
                yield break;
            }
        }

        private class SnapshotCapturingStrategy : IStrategy
        {
            public PortfolioSnapshot LastSnapshot { get; private set; }

            public IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
            {
                LastSnapshot = snapshot;
                yield break;
            }
        }

        private class StopAfterOneBarStrategy : IStrategy
        {
            public IEngine Engine { get; set; }
            private bool _stopped;

            public IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
            {
                if (!_stopped) { Engine.Stop(); _stopped = true; }
                yield break;
            }
        }

        private class BuyAaplOnFirstBarOnly : IStrategy
        {
            private bool _bought;

            public IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
            {
                if (!_bought && symbol == "AAPL")
                {
                    _bought = true;
                    yield return new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 };
                }
            }
        }
    }
}
