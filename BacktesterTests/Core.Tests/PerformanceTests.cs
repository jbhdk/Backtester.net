using System;
using System.Collections.Generic;
using Backtester.Core;
using Backtester.Data;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class PerformanceTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Trade Buy(string symbol, decimal price, int qty, DateTime ts) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Symbol = symbol,
            Side = OrderSide.Buy,
            Price = price,
            Quantity = qty,
            Timestamp = ts
        };

        private static Trade Sell(string symbol, decimal price, int qty, DateTime ts) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Symbol = symbol,
            Side = OrderSide.Sell,
            Price = price,
            Quantity = qty,
            Timestamp = ts
        };

        private static MarketSlice Slice(string symbol, decimal markPrice, DateTime ts) => new()
        {
            Timestamp = ts,
            BarsBySymbol = new Dictionary<string, Candle>
            {
                [symbol] = new Candle { Timestamp = ts, Open = markPrice, High = markPrice, Low = markPrice, Close = markPrice, Volume = 1 }
            }
        };

        [Fact]
        public void GetPerformanceStats_SingleWinningRoundTrip_NetProfitCorrect()
        {
            // Buy 10@$100, sell 10@$120 → realized PnL = (120-100)*10 = $200
            Portfolio portfolio = new Portfolio(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(1)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(200m, stats.NetProfit);
        }

        [Fact]
        public void GetPerformanceStats_SingleRoundTrip_BarsHeldCorrect()
        {
            // Entry at bar 0 (T0), one interim bar, exit at bar 2 (T0+2d) → BarsHeld = 2
            Portfolio portfolio = new Portfolio(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(1, stats.Trades);
            Assert.Equal(2, stats.RoundTrips[0].BarsHeld);
        }

        [Fact]
        public void GetPerformanceStats_OneWinOneLoss_WinRateProfitFactorExpectancy()
        {
            // Win:  buy 10@$100, sell 10@$110 → PnL = +$100
            // Loss: buy 10@$110, sell 10@$100 → PnL = -$100
            Portfolio portfolio = new Portfolio(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10, T0.AddDays(3)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(3)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(2, stats.Trades);
            Assert.Equal(0.5m, stats.WinRate);
            Assert.Equal(1m, stats.ProfitFactor);   // $100 gross profit / $100 gross loss
            Assert.Equal(0m, stats.NetProfit);
            Assert.Equal(0m, stats.Expectancy);     // 0.5*100 + 0.5*(-100) = 0
        }

        [Fact]
        public void GetPerformanceStats_MaxDrawdown_ComputedFromMarkedEquity()
        {
            // Start $30,000; buy 100@$100 → Cash=$20,000
            // Bar at $200: MarkedEquity = $20,000 + 100*$200 = $40,000 (peak)
            // Bar at $100: MarkedEquity = $20,000 + 100*$100 = $30,000 (trough)
            // MaxDrawdown = ($40,000 - $30,000) / $40,000 = 25%
            Portfolio portfolio = new Portfolio(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(1)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(0.25m, stats.MaxDrawdown);
        }

        [Fact]
        public void GetPerformanceStats_MaxConsecLosses_LongestLosingStreak()
        {
            // Three round trips: loss, loss, win → MaxConsecLosses = 2
            Portfolio portfolio = new Portfolio(50_000m);
            DateTime ts = T0;

            portfolio.ApplyTrade(Buy("AAPL", 100m, 1, ts));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, ts));
            ts = ts.AddDays(1);
            portfolio.ApplyTrade(Sell("AAPL", 90m, 1, ts));   // loss: -$10
            portfolio.RecordEquitySnapshot(Slice("AAPL", 90m, ts));
            ts = ts.AddDays(1);

            portfolio.ApplyTrade(Buy("AAPL", 90m, 1, ts));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 90m, ts));
            ts = ts.AddDays(1);
            portfolio.ApplyTrade(Sell("AAPL", 80m, 1, ts));   // loss: -$10
            portfolio.RecordEquitySnapshot(Slice("AAPL", 80m, ts));
            ts = ts.AddDays(1);

            portfolio.ApplyTrade(Buy("AAPL", 80m, 1, ts));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 80m, ts));
            ts = ts.AddDays(1);
            portfolio.ApplyTrade(Sell("AAPL", 90m, 1, ts));   // win: +$10
            portfolio.RecordEquitySnapshot(Slice("AAPL", 90m, ts));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(2, stats.MaxConsecLosses);
        }
    }
}
