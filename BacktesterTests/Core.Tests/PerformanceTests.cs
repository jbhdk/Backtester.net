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
        public void BuildRoundTrips_SellThenBuy_PairsShortRoundTripWithMirroredPnL()
        {
            // Short 10 @ 150, cover 10 @ 140 → realized = (150-140)*10 = 100, Direction = Short
            DateTime entry = T0;
            DateTime exit = T0.AddDays(1);
            List<Trade> trades = new() { Sell("AAPL", 150m, 10, entry), Buy("AAPL", 140m, 10, exit) };
            List<EquitySnapshot> history = new() { Snapshot(entry), Snapshot(exit) };

            IReadOnlyList<RoundTrip> trips = PerformanceCalculator.BuildRoundTrips(trades, history);

            Assert.Single(trips);
            Assert.Equal(PositionDirection.Short, trips[0].Direction);
            Assert.Equal(150m, trips[0].EntryPrice);
            Assert.Equal(140m, trips[0].ExitPrice);
            Assert.Equal(100m, trips[0].RealizedPnL);
        }

        [Fact]
        public void BuildRoundTrips_BuyThenSell_TagsRoundTripLong()
        {
            List<Trade> trades = new() { Buy("AAPL", 100m, 10, T0), Sell("AAPL", 120m, 10, T0.AddDays(1)) };
            List<EquitySnapshot> history = new() { Snapshot(T0), Snapshot(T0.AddDays(1)) };

            IReadOnlyList<RoundTrip> trips = PerformanceCalculator.BuildRoundTrips(trades, history);

            Assert.Equal(PositionDirection.Long, trips[0].Direction);
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
        public void GetPerformanceStats_ProfitableShort_CountsAsWin()
        {
            // Short 10 @ 150, cover 10 @ 140 → +$100 → one winning round trip
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0));
            portfolio.ApplyTrade(Buy("AAPL", 140m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 140m, T0.AddDays(1)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(1, stats.Trades);
            Assert.Equal(1m, stats.WinRate);
            Assert.Equal(100m, stats.NetProfit);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_IncludesShortRoundTrip()
        {
            // AAPL traded only short: short 10 @ 150, cover 10 @ 140 → +$100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0));
            portfolio.ApplyTrade(Buy("AAPL", 140m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 140m, T0.AddDays(1)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(1, bySymbol["AAPL"].Trades);
            Assert.Equal(100m, bySymbol["AAPL"].NetProfit);
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

        [Fact]
        public void GetPerformanceStats_MaxConsecWins_LongestWinningStreak()
        {
            // Three round trips: win, win, loss → MaxConsecWins = 2.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 1, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 1, T0.AddDays(1)));   // win
            portfolio.ApplyTrade(Buy("AAPL", 110m, 1, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 1, T0.AddDays(3)));   // win
            portfolio.ApplyTrade(Buy("AAPL", 120m, 1, T0.AddDays(4)));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 1, T0.AddDays(5)));   // loss

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(2, stats.MaxConsecWins);
        }

        [Fact]
        public void GetPerformanceStats_MedianTrade_MiddleRealizedPnL()
        {
            // Three round trips with P&L -100, +100, +300 → median +100.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 90m, 10, T0.AddDays(1)));    // -100
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(3)));   // +100
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(4)));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(5)));   // +300

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(100m, stats.MedianTrade);
        }

        [Fact]
        public void GetPerformanceStats_LargestWinAndLoss_ExtremeRealizedPnL()
        {
            // P&L -100, +100, +300 → largest win +300, largest loss -100.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 90m, 10, T0.AddDays(1)));    // -100
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(3)));   // +100
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(4)));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(5)));   // +300

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(300m, stats.LargestWin);
            Assert.Equal(-100m, stats.LargestLoss);
        }

        [Fact]
        public void GetPerformanceStats_DirectionalWinRates_SplitLongAndShort()
        {
            // Longs: one win (+$100), one loss (-$100) → 0.5. Shorts: one win (+$100) → 1.0.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));   // long win
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10, T0.AddDays(3)));   // long loss
            portfolio.ApplyTrade(Sell("MSFT", 150m, 10, T0.AddDays(4)));
            portfolio.ApplyTrade(Buy("MSFT", 140m, 10, T0.AddDays(5)));    // short win

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(0.5m, stats.LongWinRate);
            Assert.Equal(1m, stats.ShortWinRate);
        }

        [Fact]
        public void GetPerformanceStats_TradeDurations_MeanMedianLongestShortest()
        {
            // Two round trips held 1 day and 3 days → avg 2d, median 2d, longest 3d, shortest 1d.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(5)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(TimeSpan.FromDays(2), stats.AvgTradeDuration);
            Assert.Equal(TimeSpan.FromDays(2), stats.MedianTradeDuration);
            Assert.Equal(TimeSpan.FromDays(3), stats.LongestTradeDuration);
            Assert.Equal(TimeSpan.FromDays(1), stats.ShortestTradeDuration);
        }

        [Fact]
        public void GetPerformanceStats_MarketExposure_FractionOfBarsHoldingAPosition()
        {
            // Position open over the first two bars, flat on the third (the exit bar) → exposure 2/3.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(2m / 3m, stats.MarketExposure);
        }

        [Fact]
        public void GetPerformanceStats_CapitalInvested_TimeWeightedAverageAndPeak()
        {
            // Position values: $1,000 (10@100), $1,100 (10@110), $0 (flat at exit) → avg 700, peak 1,100.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(700m, stats.AvgCapitalInvested);
            Assert.Equal(1_100m, stats.MaxCapitalInvested);
        }

        [Fact]
        public void GetPerformanceStats_ShortPosition_CapitalInvestedCountsGrossValue()
        {
            // A short carries a negative market value; capital invested counts its gross (absolute) value.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0));   // position value -$1,500 → gross $1,500

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(1_500m, stats.MaxCapitalInvested);
            Assert.Equal(1m, stats.MarketExposure);
        }

        [Fact]
        public void GetPerformanceStats_RecoveryFactorAndAvgDrawdown_FromDeepestEpisode()
        {
            // Equity peaks at $40,000, troughs at $35,000 (12.5% / $5,000) and never recovers; the round
            // trip nets +$5,000 → recovery factor = 5,000 / 5,000 = 1, average drawdown = 0.125.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));            // equity $40,000 (peak)
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0.AddDays(1))); // equity $35,000 (trough)
            portfolio.ApplyTrade(Sell("AAPL", 150m, 100, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0.AddDays(2))); // flat, equity $35,000

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(5_000m, stats.NetProfit);
            Assert.Equal(0.125m, stats.AvgDrawdown);
            Assert.Equal(1m, stats.RecoveryFactor);
        }

        [Fact]
        public void GetPerformanceStats_MaxDrawdownDuration_SpansPeakToRunEndWhenNeverRecovered()
        {
            // Peak at T0, underwater through to the final bar at T0+2d → longest drawdown duration = 2 days.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));            // peak
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(TimeSpan.FromDays(2), stats.MaxDrawdownDuration);
        }

        [Fact]
        public void GetPerformanceStats_Calmar_IsCagrOverMaxDrawdown()
        {
            // Calmar relates the two equity-derived metrics; assert the relationship on a drawdown run.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 150m, T0.AddYears(1)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.NotEqual(0m, stats.MaxDrawdown);
            Assert.Equal(stats.Cagr / stats.MaxDrawdown, stats.Calmar);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_SingleSymbol_SortinoMatchesPortfolio()
        {
            // One symbol's isolated equity curve is the portfolio curve, so Sortino must match.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 50, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 90m, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 130m, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 50, T0.AddDays(3)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(3)));

            PerformanceStats portfolioStats = portfolio.GetPerformanceStats();
            PerformanceStats symbolStats = portfolio.GetPerformanceStatsBySymbol()["AAPL"];

            Assert.NotEqual(0m, portfolioStats.Sortino);
            Assert.Equal(portfolioStats.Sortino, symbolStats.Sortino);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_IsolatesMarketExposurePerSymbol()
        {
            // AAPL holds over two of three bars, flat on its exit bar (exposure 2/3); MSFT never trades, so
            // it has no round trip and is absent from the per-symbol stats.
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 100m, "MSFT", 50m, T0));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 110m, "MSFT", 50m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice2("AAPL", 120m, "MSFT", 50m, T0.AddDays(2)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(2m / 3m, bySymbol["AAPL"].MarketExposure);
            Assert.False(bySymbol.ContainsKey("MSFT"));
        }
    }
}
