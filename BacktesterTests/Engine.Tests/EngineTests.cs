using System;
using System.Collections.Generic;
using System.Linq;
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
        public async Task StartAsync_PortfolioCarryingSameCurrencyInstrument_ProducesIdenticalResultToPlainSymbolPortfolio()
        {
            Candle[] bars = { Bar(T0, 100m), Bar(T0.AddDays(1), 101m), Bar(T0.AddDays(2), 102m) };
            IHistoricalDataFetcher fetcherForStrings = FetcherReturning(("AAPL", bars));
            IHistoricalDataFetcher fetcherForInstruments = FetcherReturning(("AAPL", bars));

            Portfolio stringPortfolio = new(10_000m);
            BrokerSimulator stringBroker = new(stringPortfolio);
            BacktestEngine stringEngine = new(fetcherForStrings, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new AlwaysBuyOneShare(), stringBroker, stringPortfolio);

            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD", ConversionSymbol = null, MarginRate = null } };
            Portfolio instrumentPortfolio = new(10_000m, "USD", instruments);
            BrokerSimulator instrumentBroker = new(instrumentPortfolio);
            BacktestEngine instrumentEngine = new(fetcherForInstruments, new[] { "AAPL" }, T0, T0.AddYears(1), "1d", new AlwaysBuyOneShare(), instrumentBroker, instrumentPortfolio);

            Backtester.Engine.BacktestResult stringResult = await stringEngine.StartAsync();
            Backtester.Engine.BacktestResult instrumentResult = await instrumentEngine.StartAsync();

            Assert.Equal(stringResult.Symbols, instrumentResult.Symbols);
            Assert.Equal(stringPortfolio.Cash, instrumentPortfolio.Cash);
            Assert.Equal(stringPortfolio.RealizedPnL, instrumentPortfolio.RealizedPnL);
            Assert.Equal(stringPortfolio.Positions.Single().Quantity, instrumentPortfolio.Positions.Single().Quantity);
            Assert.Equal(stringPortfolio.EquityHistory.Count, instrumentPortfolio.EquityHistory.Count);
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
        public async Task StartAsync_InvokesOnBarForASymbol_OnlyOnItsRealBars_NotForwardFilledSlots()
        {
            // AAPL prints at t0 and t3 only; a 24/7 symbol (BTC) drives the t1/t2 slots where AAPL has no bar
            // of its own and is forward-filled. OnBar must fire for AAPL only on its real bars (t0, t3), never
            // on the forward-filled t1/t2 slots — acting there would decide on AAPL at a time it never traded
            // and its orders cannot fill.
            DateTime t0 = new(2024, 1, 5, 19, 0, 0, DateTimeKind.Utc);
            DateTime t1 = new(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc);
            DateTime t2 = new(2024, 1, 5, 21, 0, 0, DateTimeKind.Utc);
            DateTime t3 = new(2024, 1, 8, 14, 0, 0, DateTimeKind.Utc);
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(t0, 100m), Bar(t3, 110m) }),
                ("BTC", new[] { Bar(t0, 40_000m), Bar(t1, 40_100m), Bar(t2, 40_200m), Bar(t3, 40_300m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            BarRecordingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "BTC" }, t0, t0.AddYears(1), "1h", strategy, broker, portfolio);
            await engine.StartAsync();

            DateTime[] aaplBars = strategy.Calls.Where(call => call.Symbol == "AAPL").Select(call => call.Timestamp).ToArray();
            Assert.Equal(new[] { t0, t3 }, aaplBars);
        }

        [Fact]
        public async Task StartAsync_FlatEntryAcrossForwardFilledSlots_DoesNotStackTheEntry()
        {
            // A strategy that buys whenever AAPL is flat submits on t0; the entry cannot fill until AAPL's
            // next real bar (t3), because t1/t2 are forward-filled slots driven by the 24/7 symbol. Were OnBar
            // to fire on those stale slots, the still-flat snapshot would make the strategy re-submit, and the
            // queued entries would all fill together at t3 — stacking the position. It must open exactly one lot.
            DateTime t0 = new(2024, 1, 5, 19, 0, 0, DateTimeKind.Utc);
            DateTime t1 = new(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc);
            DateTime t2 = new(2024, 1, 5, 21, 0, 0, DateTimeKind.Utc);
            DateTime t3 = new(2024, 1, 8, 14, 0, 0, DateTimeKind.Utc);
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(t0, 100m), Bar(t3, 110m) }),
                ("BTC", new[] { Bar(t0, 40_000m), Bar(t1, 40_100m), Bar(t2, 40_200m), Bar(t3, 40_300m) }));
            Portfolio portfolio = new(100_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "BTC" }, t0, t0.AddYears(1), "1h", new BuyAaplWhenFlat(), broker, portfolio);
            await engine.StartAsync();

            Position position = Assert.Single(portfolio.Positions);
            Assert.Equal("AAPL", position.Symbol);
            Assert.Equal(1, position.Quantity);
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

        // --- Multi-currency conversion (ADR 0029) ---

        [Fact]
        public async Task StartAsync_PortfolioDeclaringConversionSymbol_FetchesItAlongsideTradableSymbols()
        {
            // The Engine is handed only the tradable symbol; the conversion series it must additionally
            // fetch comes from the Portfolio alone, so the two can never disagree about what converts
            // through what.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("EUR_JPY", new[] { Bar(T0, 15_000m) }),
                ("USD_JPY", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync("USD_JPY", A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._)).MustHaveHappened();
        }

        [Fact]
        public async Task StartAsync_ConversionSymbol_NeverTriggersOnBar()
        {
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("EUR_JPY", new[] { Bar(T0, 15_000m), Bar(T0.AddDays(1), 15_100m) }),
                ("USD_JPY", new[] { Bar(T0, 150m), Bar(T0.AddDays(1), 150m) }));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);
            BarRecordingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.DoesNotContain(strategy.Calls, call => call.Symbol == "USD_JPY");
            Assert.Contains(strategy.Calls, call => call.Symbol == "EUR_JPY");
        }

        [Fact]
        public async Task StartAsync_ConversionSymbol_NeverAppearsInOnStartHistory()
        {
            // The conversion series is plumbing: it is fetched so positions can be valued, but a strategy
            // trading EUR_JPY never declared USD_JPY tradable and must not see it when precomputing.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("EUR_JPY", new[] { Bar(T0, 15_000m) }),
                ("USD_JPY", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);
            HistoryCapturingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            await engine.StartAsync();

            Assert.Equal(new[] { "EUR_JPY" }, strategy.ReceivedHistory.Keys);
        }

        [Fact]
        public async Task StartAsync_ConversionSymbol_NeverAppearsInResultSymbolsOrCandleHistory()
        {
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("EUR_JPY", new[] { Bar(T0, 15_000m) }),
                ("USD_JPY", new[] { Bar(T0, 150m) }));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.Equal(new[] { "EUR_JPY" }, result.Symbols);
            Assert.True(result.CandleHistory.ContainsKey("EUR_JPY"));
            Assert.False(result.CandleHistory.ContainsKey("USD_JPY"));
        }

        [Fact]
        public async Task StartAsync_JpyQuotedInstrumentInUsdAccount_ConvertsCashRealizedPnLAndMarkedEquity_WhileKeepingNativePrices()
        {
            // EUR_JPY: buy fills at bar1's open (15,300), sell fills at bar2's open (15,400), a constant
            // 100 JPY-per-USD conversion rate throughout. Native gain = (15,400-15,300)*10 = 1,000 JPY ->
            // converted = 10 USD. Cash = 10,000 - (15,300*10/100) + (15,400*10/100) = 10,000-1,530+1,540 = 10,010.
            Candle[] eurJpyBars = { Bar(T0, 15_000m), Bar(T0.AddDays(1), 15_300m), Bar(T0.AddDays(2), 15_400m) };
            Candle[] usdJpyBars = { Bar(T0, 100m), Bar(T0.AddDays(1), 100m), Bar(T0.AddDays(2), 100m) };
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(("EUR_JPY", eurJpyBars), ("USD_JPY", usdJpyBars));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);
            RoundTripRecordingStrategy strategy = new();

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", strategy, broker, portfolio);
            Backtester.Engine.BacktestResult result = await engine.StartAsync();

            Assert.Equal(10_010m, portfolio.Cash);
            Assert.Equal(10m, portfolio.RealizedPnL);
            Assert.Equal(10_010m, portfolio.MarkedEquity);

            RoundTrip roundTrip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(15_300m, roundTrip.EntryPrice);
            Assert.Equal(15_400m, roundTrip.ExitPrice);
            Assert.Equal(10m, roundTrip.RealizedPnL);

            Assert.Equal(new[] { "EUR_JPY" }, result.Symbols);
            Assert.DoesNotContain(portfolio.RoundTrips, trip => trip.Symbol == "USD_JPY");
        }

        // --- Currency converter fill-timing invariant (ADR 0029) ---

        /// <summary>
        /// Runs the scenario the Currency converter's fill-timing invariant is pinned on: a USD account
        /// trading JPY-quoted EUR_JPY through USD_JPY, whose rate moves from 100 to 125 on the very bar the
        /// entry fills. A ten-unit Buy stop at 15,100 is submitted on the first bar; the second bar's open
        /// (15,300) gapped past that trigger, so the fill is priced there. Returns the run's Portfolio.
        /// </summary>
        private static async Task<Portfolio> RunRateMovingOnFillBarAsync()
        {
            Candle[] eurJpyBars = { Bar(T0, 15_000m), Bar(T0.AddDays(1), 15_300m) };
            Candle[] usdJpyBars = { Bar(T0, 100m), Bar(T0.AddDays(1), 125m) };
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            IHistoricalDataFetcher fetcher = FetcherReturning(("EUR_JPY", eurJpyBars), ("USD_JPY", usdJpyBars));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d", new BuyStopOnFirstBar(15_100m), broker, portfolio);
            await engine.StartAsync();

            return portfolio;
        }

        [Fact]
        public async Task StartAsync_ConversionRateMovesOnTheFillBar_FillTranslatesAtThePreviousClose()
        {
            // The fill costs 15,300 * 10 = 153,000 JPY. USD_JPY's last completed close when that fill is
            // applied is the first bar's 100 — the fill bar's own 125 was not knowable while the bar was
            // trading — so cash pays 153,000/100 = 1,530 USD and lands at 8,470. Translating at the fill
            // bar's own close would pay 153,000/125 = 1,224 and leave 8,776: lookahead, and the exact
            // failure a reordering of the engine loop's fill and equity-snapshot statements would produce.
            Portfolio portfolio = await RunRateMovingOnFillBarAsync();

            Assert.Equal(8_470m, portfolio.Cash);
        }

        [Fact]
        public async Task StartAsync_ConversionRateMovesOnTheFillBar_EndOfBarMarkUsesTheCurrentClose()
        {
            // The same bar, the other half of the rule: the mark is taken once the bar has completed, so it
            // uses the freshest rate the bar printed. The position of 10 marks at EUR_JPY's close of 15,300
            // = 153,000 JPY, translated at USD_JPY's own close of 125 = 1,224 USD. The previous close (100)
            // would value it at 1,530 — stale by one bar for a figure nothing forbids being current.
            Portfolio portfolio = await RunRateMovingOnFillBarAsync();

            EquitySnapshot fillBar = portfolio.EquityHistory[1];
            Assert.Equal(1_224m, fillBar.PositionValueBySymbol["EUR_JPY"]);
        }

        [Fact]
        public async Task StartAsync_CrossCurrencyFill_RecordsTheNativeGapAwarePriceUnchangedByConversion()
        {
            // Currency translation moves money, never execution semantics: the recorded price is the
            // gap-aware fill in EUR_JPY's own quote currency (ADR 0024). The bar opened at 15,300, above the
            // 15,100 stop, so the fill is the open — not the trigger it gapped through, and not a figure
            // either conversion rate would produce (153 at 100, 122.4 at 125).
            Portfolio portfolio = await RunRateMovingOnFillBarAsync();

            Trade fill = Assert.Single(portfolio.Trades);
            Assert.Equal(15_300m, fill.Price);
        }

        [Fact]
        public async Task StartAsync_PortfolioDeclaringNoConversion_FetchesExactlyTheGivenSymbolsAndNoMore()
        {
            // A stock/ETF run never touches the conversion machinery: with no Instruments declared, the
            // Portfolio names no conversion series and the fetch set is exactly the symbol list.
            IHistoricalDataFetcher fetcher = FetcherReturning(
                ("AAPL", new[] { Bar(T0, 100m) }),
                ("MSFT", new[] { Bar(T0, 200m) }));
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(fetcher, new[] { "AAPL", "MSFT" }, T0, T0.AddYears(1), "1d", new DoNothingStrategy(), broker, portfolio);
            await engine.StartAsync();

            A.CallTo(() => fetcher.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappened(2, Times.Exactly);
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

        /// <summary>Captures the history handed to OnStart so a test can assert which symbols it spans.</summary>
        private sealed class HistoryCapturingStrategy : StrategyBase
        {
            public IReadOnlyDictionary<string, IReadOnlyList<Candle>> ReceivedHistory { get; private set; }

            public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
            {
                ReceivedHistory = history;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker) { }
        }

        /// <summary>Records every (symbol, bar timestamp) pair it is invoked with, so a test can assert the OnBar cadence.</summary>
        private sealed class BarRecordingStrategy : StrategyBase
        {
            public List<(string Symbol, DateTime Timestamp)> Calls { get; } = new();

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                Calls.Add((symbol, bar.Timestamp));
            }
        }

        /// <summary>Submits a one-share AAPL market buy on every bar it sees AAPL flat, exercising the stale-bar re-entry trap.</summary>
        private sealed class BuyAaplWhenFlat : StrategyBase
        {
            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (symbol != "AAPL")
                {
                    return;
                }

                bool flat = !snapshot.Positions.Any(position => position.Symbol == "AAPL" && position.Quantity != 0);
                if (flat)
                {
                    broker.Submit(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 });
                }
            }
        }

        /// <summary>
        /// Submits one ten-unit Buy stop at the given trigger the first time it sees any bar, then never
        /// trades again — so the run's cash and marks after that single fill are attributable to it alone.
        /// </summary>
        private sealed class BuyStopOnFirstBar : StrategyBase
        {
            private readonly decimal _trigger;
            private bool _submitted;

            public BuyStopOnFirstBar(decimal trigger)
            {
                _trigger = trigger;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_submitted)
                {
                    return;
                }

                _submitted = true;
                broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Stop, Price = _trigger, Quantity = 10 });
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
