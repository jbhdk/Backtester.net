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
        public async Task StartAsync_EntryOnSessionLastBar_StampedAtNextRealBar_NotForwardFilledSlot()
        {
            // AAPL trades a session that ends at T1, resuming only at T4. A 24/7 symbol (BTC) drives extra
            // timeline slots at T2/T3 where AAPL has no bar of its own. A buy queued on AAPL's last session
            // bar (T1) must fill at AAPL's next real bar (T4) — its open and timestamp — not against the
            // forward-filled stale T1 bar at the phantom T2 slot (issue #56).
            DateTime t0 = new(2024, 1, 5, 19, 0, 0, DateTimeKind.Utc); // Fri
            DateTime t1 = new(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc); // Fri, AAPL's last session bar
            DateTime t2 = new(2024, 1, 5, 21, 0, 0, DateTimeKind.Utc); // Fri post-close, BTC only
            DateTime t3 = new(2024, 1, 6, 0, 0, 0, DateTimeKind.Utc);  // weekend, BTC only
            DateTime t4 = new(2024, 1, 9, 14, 0, 0, DateTimeKind.Utc); // Tue, AAPL resumes

            Candle[] aaplBars = { Bar(t0, 100m), Bar(t1, 101m), Bar(t4, 110m) };
            Candle[] btcBars = { Bar(t0, 40_000m), Bar(t1, 40_100m), Bar(t2, 40_200m), Bar(t3, 40_300m), Bar(t4, 40_400m) };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", aaplBars), ("BTC", btcBars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "BTC" }, t0, t0.AddYears(1), "1h", new BuyAaplOnSpecificBar(t1), broker, portfolio);
            await engine.StartAsync();

            Position position = Assert.Single(portfolio.Positions);
            Assert.Equal("AAPL", position.Symbol);
            Assert.Equal(t4, position.EntryTime);     // a real AAPL bar, not the phantom T2 slot
            Assert.Equal(110m, position.AveragePrice); // T4's open, not the stale T1 open (101)
        }

        [Fact]
        public async Task StartAsync_ExitOnSessionLastBar_RoundTripExitStampedAtNextRealBar()
        {
            // The exit-marker analogue of issue #56 (issue #57): an exit queued on AAPL's last session bar
            // (T1) must close the round trip at AAPL's next real bar (T4), not against the forward-filled
            // stale T1 bar at the phantom T2 slot driven by the 24/7 symbol. A round trip whose ExitTime is
            // a real bar puts the chart's exit marker on an actual candle instead of one bar before it.
            DateTime t0 = new(2024, 1, 5, 18, 0, 0, DateTimeKind.Utc); // Fri, AAPL entry context
            DateTime t1 = new(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc); // Fri, AAPL's last session bar
            DateTime t2 = new(2024, 1, 5, 21, 0, 0, DateTimeKind.Utc); // Fri post-close, BTC only
            DateTime t3 = new(2024, 1, 6, 0, 0, 0, DateTimeKind.Utc);  // weekend, BTC only
            DateTime t4 = new(2024, 1, 9, 14, 0, 0, DateTimeKind.Utc); // Tue, AAPL resumes

            Candle[] aaplBars = { Bar(t0, 100m), Bar(t1, 101m), Bar(t4, 110m) };
            Candle[] btcBars = { Bar(t0, 40_000m), Bar(t1, 40_100m), Bar(t2, 40_200m), Bar(t3, 40_300m), Bar(t4, 40_400m) };
            IHistoricalDataFetcher fetcher = FetcherReturning(("AAPL", aaplBars), ("BTC", btcBars));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            // Buy on AAPL's entry bar (fills T1), then sell on the last session bar T1 to close the position.
            BacktestEngine engine = new(fetcher, new[] { "AAPL", "BTC" }, t0, t0.AddYears(1), "1h", new BuyThenSellAaplOnBar(t0, t1), broker, portfolio);
            await engine.StartAsync();

            RoundTrip roundTrip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(t4, roundTrip.ExitTime);     // a real AAPL bar, not the phantom T2 slot
            Assert.Equal(110m, roundTrip.ExitPrice);  // T4's open, not the stale T1 open (101)
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
        public async Task StartAsync_RoundTripClosesDuringRun_ObserverReceivesItWithRealizedPnL()
        {
            // Buy fills at bar 1's open (110), sell fills at bar 2's open (120) → +$100 round trip,
            // delivered to the observing strategy as it closes.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            RoundTripRecordingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            RoundTrip closed = Assert.Single(strategy.Closed);
            Assert.Equal(100m, closed.RealizedPnL);
        }

        [Fact]
        public async Task StartAsync_RoundTripClosedOnBar_DeliveredBeforeThatBarsOnBar()
        {
            // The round trip closes when the sell fills at bar 2's open. Its delivery must precede bar 2's
            // OnBar, so the event stream is: bar(0), bar(1), closed, bar(2).
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            RoundTripRecordingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(new[] { "bar", "bar", "closed", "bar" }, strategy.Events);
        }

        [Fact]
        public async Task StartAsync_TwoSymbolsCloseOnSameBar_BothDeliveredInCloseOrder()
        {
            // AAPL and MSFT both buy on bar 0 and sell on bar 1; both sells fill on bar 2, closing two
            // round trips on one bar. Each is delivered as its own call, in the order they closed.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }),
                ("MSFT", new[] { Bar(T0, 200m), Bar(T0.AddDays(1), 210m), Bar(T0.AddDays(2), 220m) }));
            Portfolio portfolio = new(50_000m);
            BrokerSimulator broker = new(portfolio);
            BuyThenSellEachSymbolStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(2, strategy.Closed.Count);
            Assert.Equal(portfolio.RoundTrips, strategy.Closed);
            Assert.Contains(strategy.Closed, trip => trip.Symbol == "AAPL");
            Assert.Contains(strategy.Closed, trip => trip.Symbol == "MSFT");
        }

        [Fact]
        public async Task StartAsync_PartialExitsOnOneBar_EachClosedPortionDelivered()
        {
            // Buy 20, then scale out with two sells of 10 on the same bar; both fill on the next bar,
            // closing two round trips of 10 each — each delivered as its own call.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));
            Portfolio portfolio = new(50_000m);
            BrokerSimulator broker = new(portfolio);
            PartialScaleOutStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(2, strategy.Closed.Count);
            Assert.All(strategy.Closed, trip => Assert.Equal(10, trip.Quantity));
            Assert.Equal(portfolio.RoundTrips, strategy.Closed);
        }

        [Fact]
        public async Task StartAsync_NonObserverStrategy_ClosesRoundTripButReceivesNothing()
        {
            // A raw IStrategy that does not implement IRoundTripObserver still closes its round trip and the
            // run is unaffected; the engine simply delivers nothing.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m), Bar(T0.AddDays(1), 110m), Bar(T0.AddDays(2), 120m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new RawBuySellStrategy(), broker, portfolio);
            await engine.StartAsync();

            RoundTrip closed = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(100m, closed.RealizedPnL);
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

        /// <summary>Submits a single AAPL market buy the first time it sees AAPL's bar at the given timestamp.</summary>
        private class BuyAaplOnSpecificBar : StrategyBase
        {
            private readonly DateTime _triggerBar;
            private bool _submitted;

            public BuyAaplOnSpecificBar(DateTime triggerBar)
            {
                _triggerBar = triggerBar;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_submitted && symbol == "AAPL" && bar.Timestamp == _triggerBar)
                {
                    _submitted = true;
                    broker.Submit(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
            }
        }

        /// <summary>Buys AAPL on its entry bar, then sells it on a later named bar, each submitted once.</summary>
        private class BuyThenSellAaplOnBar : StrategyBase
        {
            private readonly DateTime _entryBar;
            private readonly DateTime _sellBar;
            private bool _bought;
            private bool _sold;

            public BuyThenSellAaplOnBar(DateTime entryBar, DateTime sellBar)
            {
                _entryBar = entryBar;
                _sellBar = sellBar;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (symbol != "AAPL")
                {
                    return;
                }

                if (!_bought && bar.Timestamp == _entryBar)
                {
                    _bought = true;
                    broker.Submit(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
                else if (_bought && !_sold && bar.Timestamp == _sellBar)
                {
                    _sold = true;
                    broker.Submit(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 1 });
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

        /// <summary>
        /// Buys on its first bar, sells on its next, and records each round trip it observes. Also logs an
        /// event stream interleaving "bar" (each OnBar) and "closed" (each OnRoundTripClosed) so a test can
        /// assert the relative ordering of delivery and OnBar.
        /// </summary>
        private class RoundTripRecordingStrategy : StrategyBase, IRoundTripObserver
        {
            public List<RoundTrip> Closed { get; } = new();
            public List<string> Events { get; } = new();
            private bool _bought;
            private bool _sold;

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                Events.Add("bar");
                if (!_bought)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
                    _bought = true;
                }
                else if (!_sold)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
                    _sold = true;
                }
            }

            public override void OnRoundTripClosed(RoundTrip roundTrip)
            {
                Events.Add("closed");
                Closed.Add(roundTrip);
            }
        }

        /// <summary>Buys each symbol on its first bar and sells it on its next, recording observed round trips.</summary>
        private class BuyThenSellEachSymbolStrategy : StrategyBase, IRoundTripObserver
        {
            public List<RoundTrip> Closed { get; } = new();
            // Symbols already bought / already sold, so each is entered once and exited once.
            private readonly HashSet<string> _bought = new();
            private readonly HashSet<string> _sold = new();

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_bought.Add(symbol))
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
                }
                else if (_sold.Add(symbol))
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
                }
            }

            public override void OnRoundTripClosed(RoundTrip roundTrip)
            {
                Closed.Add(roundTrip);
            }
        }

        /// <summary>Buys 20 on its first bar, then scales out with two sells of 10 on the next bar.</summary>
        private class PartialScaleOutStrategy : StrategyBase, IRoundTripObserver
        {
            public List<RoundTrip> Closed { get; } = new();
            private bool _bought;
            private bool _sold;

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 20 });
                    _bought = true;
                }
                else if (!_sold)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
                    _sold = true;
                }
            }

            public override void OnRoundTripClosed(RoundTrip roundTrip)
            {
                Closed.Add(roundTrip);
            }
        }

        /// <summary>A raw IStrategy (no IRoundTripObserver seam) that buys on its first bar and sells on its next.</summary>
        private class RawBuySellStrategy : IStrategy
        {
            private bool _bought;
            private bool _sold;

            public void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history) { }

            public void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
                    _bought = true;
                }
                else if (!_sold)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
                    _sold = true;
                }
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
