using System;
using System.Collections.Generic;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class PerformanceTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Trade Buy(string symbol, decimal price, int qty, DateTime ts)
        {
            return new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = OrderSide.Buy,
                Price = price,
                Quantity = qty,
                Timestamp = ts
            };
        }

        private static Trade Sell(string symbol, decimal price, int qty, DateTime ts)
        {
            return new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = OrderSide.Sell,
                Price = price,
                Quantity = qty,
                Timestamp = ts
            };
        }

        private static MarketSlice Slice(string symbol, decimal markPrice, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbol] = new Candle { Timestamp = ts, Open = markPrice, High = markPrice, Low = markPrice, Close = markPrice, Volume = 1 }
                }
            };
        }


        private static EquitySnapshot Snapshot(DateTime ts)
        {
            return new() { Timestamp = ts };
        }

        private static MarketSlice Slice2(string symbolA, decimal markA, string symbolB, decimal markB, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbolA] = new Candle { Timestamp = ts, Open = markA, High = markA, Low = markA, Close = markA, Volume = 1 },
                    [symbolB] = new Candle { Timestamp = ts, Open = markB, High = markB, Low = markB, Close = markB, Volume = 1 }
                }
            };
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_IsolatesNetProfitPerSymbol()
        {
            // AAPL: buy 10@100, sell 10@120 → +$200. MSFT: buy 5@50, sell 5@40 → -$50.
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Buy("MSFT", 50m, 5, T0));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 100m, "MSFT", 50m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("MSFT", 40m, 5, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 120m, "MSFT", 40m, T0.AddDays(1)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(200m, bySymbol["AAPL"].NetProfit);
            Assert.Equal(-50m, bySymbol["MSFT"].NetProfit);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_TradeMetricsCountOnlyOwnRoundTrips()
        {
            // AAPL: one win (+$100) and one loss (-$100) → 2 trades, 0.5 win rate.
            // MSFT: one win (+$50) → 1 trade, 1.0 win rate.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10, T0.AddDays(3)));
            portfolio.ApplyTrade(Buy("MSFT", 50m, 10, T0));
            portfolio.ApplyTrade(Sell("MSFT", 55m, 10, T0.AddDays(1)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(2, bySymbol["AAPL"].Trades);
            Assert.Equal(0.5m, bySymbol["AAPL"].WinRate);
            Assert.Equal(1, bySymbol["MSFT"].Trades);
            Assert.Equal(1m, bySymbol["MSFT"].WinRate);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_SingleSymbol_TradeMetricsMatchPortfolio()
        {
            // With one symbol, its isolated trade metrics must equal the whole portfolio's.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(1)));

            PerformanceStats portfolioStats = portfolio.GetPerformanceStats();
            PerformanceStats symbolStats = portfolio.GetPerformanceStatsBySymbol()["AAPL"];

            Assert.Equal(portfolioStats.NetProfit, symbolStats.NetProfit);
            Assert.Equal(portfolioStats.Trades, symbolStats.Trades);
            Assert.Equal(portfolioStats.Expectancy, symbolStats.Expectancy);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_SingleSymbol_MaxDrawdownMatchesPortfolio()
        {
            // Buy 100@$100; mark to $200 (peak $40,000) then $100 (trough $30,000) = 25% drawdown.
            // With one symbol, its isolated equity curve equals the portfolio's, so the drawdowns match.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 100, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(2)));

            PerformanceStats portfolioStats = portfolio.GetPerformanceStats();
            PerformanceStats symbolStats = portfolio.GetPerformanceStatsBySymbol()["AAPL"];

            Assert.Equal(0.25m, portfolioStats.MaxDrawdown);
            Assert.Equal(portfolioStats.MaxDrawdown, symbolStats.MaxDrawdown);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_DrawdownIsolatedPerSymbol()
        {
            // AAPL swings (isolated equity peak $50,000 → trough $40,000 = 20%); MSFT stays flat at $50.
            Portfolio portfolio = new(40_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.ApplyTrade(Buy("MSFT", 50m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 200m, "MSFT", 50m, T0));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 100m, "MSFT", 50m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 100, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("MSFT", 50m, 100, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 100m, "MSFT", 50m, T0.AddDays(2)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(0.2m, bySymbol["AAPL"].MaxDrawdown);
            Assert.Equal(0m, bySymbol["MSFT"].MaxDrawdown);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_SingleSymbol_SharpeAndCagrMatchPortfolio()
        {
            // One symbol's isolated equity curve is the portfolio curve, so Sharpe and CAGR must match.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 50, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 130m, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 50, T0.AddDays(3)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(3)));

            PerformanceStats portfolioStats = portfolio.GetPerformanceStats();
            PerformanceStats symbolStats = portfolio.GetPerformanceStatsBySymbol()["AAPL"];

            Assert.NotEqual(0m, portfolioStats.Sharpe);
            Assert.Equal(portfolioStats.Sharpe, symbolStats.Sharpe);
            Assert.Equal(portfolioStats.Cagr, symbolStats.Cagr);
        }

        [Fact]
        public void BuildRoundTrips_BuyThenSell_CarriesEntryAndExitTimestamps()
        {
            // Buy at T0, sell one day later → EntryTime = T0, ExitTime = T0+1d
            DateTime entry = T0;
            DateTime exit = T0.AddDays(1);
            List<Trade> trades = new() { Buy("AAPL", 100m, 10, entry), Sell("AAPL", 120m, 10, exit) };
            List<EquitySnapshot> history = new() { Snapshot(entry), Snapshot(exit) };

            IReadOnlyList<RoundTrip> trips = PerformanceCalculator.BuildRoundTrips(trades, history);

            Assert.Equal(entry, trips[0].EntryTime);
            Assert.Equal(exit, trips[0].ExitTime);
        }

        [Fact]
        public void BuildRoundTrips_TwoBuysAveragedThenSell_EntryTimeIsFirstBuy()
        {
            // Two buys average into one position; the second buy must not overwrite the entry time.
            DateTime firstBuy = T0;
            DateTime secondBuy = T0.AddDays(1);
            DateTime exit = T0.AddDays(2);
            List<Trade> trades = new()
            {
                Buy("AAPL", 100m, 10, firstBuy),
                Buy("AAPL", 120m, 10, secondBuy),
                Sell("AAPL", 130m, 20, exit)
            };
            List<EquitySnapshot> history = new() { Snapshot(firstBuy), Snapshot(secondBuy), Snapshot(exit) };

            IReadOnlyList<RoundTrip> trips = PerformanceCalculator.BuildRoundTrips(trades, history);

            Assert.Equal(firstBuy, trips[0].EntryTime);
            Assert.Equal(exit, trips[0].ExitTime);
        }

        [Fact]
        public void GetPerformanceStats_SingleWinningRoundTrip_NetProfitCorrect()
        {
            // Buy 10@$100, sell 10@$120 → realized PnL = (120-100)*10 = $200
            Portfolio portfolio = new(10_000m);
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
            Portfolio portfolio = new(10_000m);
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
            Portfolio portfolio = new(20_000m);
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
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(1)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(0.25m, stats.MaxDrawdown);
        }

        [Fact]
        public void GetPerformanceStats_SubDayWinningRoundTrip_CagrFiniteAndDoesNotThrow()
        {
            // A ~45-minute round trip ending in profit produces a tiny annualisation span,
            // making the CAGR exponent enormous. The result must be finite, not an overflow.
            DateTime exit = T0.AddMinutes(45);
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, exit));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, exit));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.True(decimal.MinValue <= stats.Cagr && stats.Cagr <= decimal.MaxValue);
        }

        [Fact]
        public void GetPerformanceStats_MaxConsecLosses_LongestLosingStreak()
        {
            // Three round trips: loss, loss, win → MaxConsecLosses = 2
            Portfolio portfolio = new(50_000m);
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
