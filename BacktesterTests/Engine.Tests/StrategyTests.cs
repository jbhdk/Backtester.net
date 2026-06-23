using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    public class StrategyTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new()
            {
                Timestamp = ts,
                Open = close,
                High = close + 2,
                Low = close - 2,
                Close = close,
                Volume = 1000
            };
        }

        private static PortfolioSnapshot EmptySnapshot()
        {
            return new()
            {
                Timestamp = T0,
                Cash = 10_000m,
                CostBasisEquity = 10_000m,
                Positions = new List<Position>()
            };
        }

        private static PortfolioSnapshot SnapshotWithPosition(string symbol)
        {
            return new()
            {
                Timestamp = T0,
                Cash = 9_000m,
                CostBasisEquity = 10_000m,
                Positions = new List<Position> { new() { Symbol = symbol, Quantity = 10, AveragePrice = 100m } }
            };
        }

        private static PortfolioSnapshot SnapshotWithShortPosition(string symbol)
        {
            return new()
            {
                Timestamp = T0,
                Cash = 11_000m,
                CostBasisEquity = 10_000m,
                Positions = new List<Position> { new() { Symbol = symbol, Quantity = -10, AveragePrice = 100m } }
            };
        }


        /// <summary>
        /// Seeds the strategy via OnStart with the given price series, then calls OnBar for each bar.
        /// Returns the fake broker for assertion.
        /// </summary>
        private static IBroker RunBars(
            MovingAverageCrossStrategy strategy, string symbol, params decimal[] closes)
        {
            List<Candle> bars = closes.Select((c, i) => Bar(T0.AddDays(i), c)).ToList();
            IBroker broker = A.Fake<IBroker>();
            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>> { [symbol] = bars });
            for (int i = 0; i < bars.Count; i++)
            {
                strategy.OnBar(symbol, bars[i], EmptySnapshot(), broker);
            }


            return broker;
        }

        [Fact]
        public void NoOrder_WhenFewerThanSlowPeriodBars()
        {
            // fast=3, slow=5 — only 4 bars supplied (one short of slow period)
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);

            IBroker broker = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m);

            A.CallTo(() => broker.Submit(A<OrderRequest>.Ignored)).MustNotHaveHappened();
        }

        [Fact]
        public void NoOrder_OnFirstBarWithBothSMAsCalculable()
        {
            // 5th bar is the first with a slow MA — no prior direction to compare, so no order
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);

            IBroker broker = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m, 60m);

            A.CallTo(() => broker.Submit(A<OrderRequest>.Ignored)).MustNotHaveHappened();
        }

        [Fact]
        public void NoOrder_WhenFastContinuesAboveSlow_NoCross()
        {
            // Steadily rising prices: fast always above slow after warmup, never crosses
            // bar 5 (price=5): fast=4 slow=3 → direction=true (first time, no order)
            // bar 6 (price=6): fast=5 slow=4 → still true → no cross
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);

            IBroker broker = RunBars(strategy, "AAPL", 1m, 2m, 3m, 4m, 5m, 6m);

            A.CallTo(() => broker.Submit(A<OrderRequest>.Ignored)).MustNotHaveHappened();
        }

        [Fact]
        public void EmitsBuy_OnGoldenCross()
        {
            // Prices fall then recover, producing a fast-crosses-above-slow event on bar 8 (index 7)
            // [100,90,80,70,60,70,80,90]: on bar 8 fastMA=80 > slowMA=74 (was below on bar 7)
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);

            IBroker broker = RunBars(strategy, "AAPL", 100m, 90m, 80m, 70m, 60m, 70m, 80m, 90m);

            A.CallTo(() => broker.Submit(A<OrderRequest>.That.Matches(r =>
                r.Side == OrderSide.Buy && r.Type == OrderType.Market && r.Symbol == "AAPL")))
             .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public void ReversesToLong_OnGoldenCross_WhenShort_EmitsTwoBuys()
        {
            // Golden-cross series [100,90,80,70,60,70,80,90] crosses up on bar 8 (index 7). Holding a short
            // at the signal bar, the golden cross reverses: cover the short, then open a long.
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);
            decimal[] prices = new[] { 100m, 90m, 80m, 70m, 60m, 70m, 80m, 90m };
            List<Candle> bars = prices.Select((c, i) => Bar(T0.AddDays(i), c)).ToList();
            IBroker broker = A.Fake<IBroker>();

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>> { ["AAPL"] = bars });
            for (int i = 0; i < bars.Count; i++)
            {
                PortfolioSnapshot snapshot = i >= 6 ? SnapshotWithShortPosition("AAPL") : EmptySnapshot();
                strategy.OnBar("AAPL", bars[i], snapshot, broker);
            }

            A.CallTo(() => broker.Submit(A<OrderRequest>.That.Matches(r => r.Side == OrderSide.Buy)))
             .MustHaveHappenedTwiceExactly();
        }

        [Fact]
        public void ReversesToShort_OnDeathCross_WhenLong_EmitsTwoSells()
        {
            // Rising then falling: fast crosses below slow → death cross on bar 8 (index 7)
            // [60,70,80,90,100,90,80,70]: on bar 8 fastMA=80 < slowMA=86 (was above on bar 7).
            // Holding a long at the signal bar, the death cross reverses: flatten the long, then open a short.
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);
            decimal[] prices = new[] { 60m, 70m, 80m, 90m, 100m, 90m, 80m, 70m };
            List<Candle> bars = prices.Select((c, i) => Bar(T0.AddDays(i), c)).ToList();
            IBroker broker = A.Fake<IBroker>();

            strategy.OnStart(new Dictionary<string, IReadOnlyList<Candle>> { ["AAPL"] = bars });
            for (int i = 0; i < bars.Count; i++)
            {
                // Hold a long at the signal bar so the death cross triggers a reversal rather than an entry
                PortfolioSnapshot snapshot = i >= 6 ? SnapshotWithPosition("AAPL") : EmptySnapshot();
                strategy.OnBar("AAPL", bars[i], snapshot, broker);
            }

            A.CallTo(() => broker.Submit(A<OrderRequest>.That.Matches(r => r.Side == OrderSide.Sell)))
             .MustHaveHappenedTwiceExactly();
        }

        [Fact]
        public void EmitsSell_OnDeathCross_FromFlat_OpensShort()
        {
            // Death-cross series with no open position → a single Sell opens a short
            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);

            IBroker broker = RunBars(strategy, "AAPL", 60m, 70m, 80m, 90m, 100m, 90m, 80m, 70m);

            A.CallTo(() => broker.Submit(A<OrderRequest>.That.Matches(r =>
                r.Side == OrderSide.Sell && r.Type == OrderType.Market && r.Symbol == "AAPL")))
             .MustHaveHappenedOnceExactly();
        }
    }
}
