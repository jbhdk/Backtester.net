using System;
using System.Collections.Generic;
using System.Linq;
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

        private static Trade BuyWithStop(string symbol, decimal price, int qty, DateTime ts, decimal stopPrice)
        {
            Trade trade = Buy(symbol, price, qty, ts);
            trade.EntryStopPrice = stopPrice;
            return trade;
        }

        private static Trade SellWithStop(string symbol, decimal price, int qty, DateTime ts, decimal stopPrice)
        {
            Trade trade = Sell(symbol, price, qty, ts);
            trade.EntryStopPrice = stopPrice;
            return trade;
        }

        private static Trade BuyWithLevels(string symbol, decimal price, int qty, DateTime ts, decimal? stopPrice, decimal? targetPrice)
        {
            Trade trade = Buy(symbol, price, qty, ts);
            trade.EntryStopPrice = stopPrice;
            trade.EntryTargetPrice = targetPrice;
            return trade;
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
        public void ApplyTrade_BuyThenSell_EmitsLongRoundTrip()
        {
            // Buy 10 @ 100, sell 10 @ 120 → one Long round trip realizing (120-100)*10 = $200.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(PositionDirection.Long, trip.Direction);
            Assert.Equal(100m, trip.EntryPrice);
            Assert.Equal(120m, trip.ExitPrice);
            Assert.Equal(200m, trip.RealizedPnL);
        }

        [Fact]
        public void ApplyTrade_SellThenBuy_EmitsShortRoundTripWithMirroredPnL()
        {
            // Short 10 @ 150, cover 10 @ 140 → realized = (150-140)*10 = 100, Direction = Short
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10, T0));
            portfolio.ApplyTrade(Buy("AAPL", 140m, 10, T0.AddDays(1)));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(PositionDirection.Short, trip.Direction);
            Assert.Equal(150m, trip.EntryPrice);
            Assert.Equal(140m, trip.ExitPrice);
            Assert.Equal(100m, trip.RealizedPnL);
        }

        [Fact]
        public void ApplyTrade_RoundTrip_CarriesMarkedEquityAtEntry()
        {
            // First round trip opens with $10,000 equity and nets +$200. The second opens afterward, when
            // equity has grown to $10,200 — so each round trip carries the marked equity at its own entry,
            // not the run's starting cash.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));   // +$200 → cash $10,200
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(2)));    // opens with equity $10,200
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(3)));

            Assert.Equal(10_000m, portfolio.RoundTrips[0].EntryEquity);
            Assert.Equal(10_200m, portfolio.RoundTrips[1].EntryEquity);
        }

        [Fact]
        public void ApplyTrade_EntryStopStamped_CarriesInitialRiskDistanceTimesQuantity()
        {
            // Buy 10 @ 100 with an entry stop at 90 → per-share distance 10, initial risk 10 * 10 = 100.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 10, T0, stopPrice: 90m));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            Assert.Equal(100m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_ScaleInAtDifferentStop_InitialRiskAnchorsOnOpeningStop()
        {
            // Open 10 @ 100 stop 90 (distance 10); add 10 @ 120 stop 118 (distance 2). The add must not
            // re-blend the frozen distance: initial risk = 10 * 20 shares = 200, not the added stop's 2.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 10, T0, stopPrice: 90m));
            portfolio.ApplyTrade(BuyWithStop("AAPL", 120m, 10, T0.AddDays(1), stopPrice: 118m));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 20, T0.AddDays(2)));

            Assert.Equal(200m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_PartialExit_InitialRiskScalesToEachExitedSlice()
        {
            // Open 20 @ 100 stop 90 (distance 10); exit 10 then 10. Each slice risks distance * its qty:
            // 10 * 10 = 100 apiece, on the same frozen per-share distance.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 20, T0, stopPrice: 90m));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(2)));

            Assert.Equal(2, portfolio.RoundTrips.Count);
            Assert.Equal(100m, portfolio.RoundTrips[0].InitialRisk);
            Assert.Equal(100m, portfolio.RoundTrips[1].InitialRisk);
        }

        [Fact]
        public void ApplyTrade_EntryWithoutStop_LeavesInitialRiskNull()
        {
            // An entry that declared no protective stop has no initial risk, so no R-multiple is defined.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            Assert.Null(Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_ShortEntryStop_InitialRiskIsPositiveDistanceTimesQuantity()
        {
            // Short 10 @ 150 with a stop above at 160 → distance |150 - 160| = 10, initial risk 10 * 10 = 100.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(SellWithStop("AAPL", 150m, 10, T0, stopPrice: 160m));
            portfolio.ApplyTrade(Buy("AAPL", 140m, 10, T0.AddDays(1)));

            Assert.Equal(100m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        // --- Initial risk in Account currency (ADR 0032) ---

        [Fact]
        public void ApplyTrade_CrossCurrencyEntryStop_InitialRiskIsTranslatedAtTheEntryRate()
        {
            // EUR_JPY quotes in JPY, the account in USD. Buy 1 unit @ 15,000 JPY with a stop 100 JPY below
            // while USD_JPY reads 100: the entry risked 100 JPY, which the account felt as $1.00 — what a
            // broker would have told the trader was at risk as the position opened.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_000m, 1, T0, stopPrice: 14_900m));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_200m, 1, T0.AddDays(1)));

            Assert.Equal(1m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_RateMovesBetweenEntryAndExit_InitialRiskKeepsTheEntryRate()
        {
            // Same entry as above at USD_JPY 100, but the rate moves to 125 before the exit. The stamped
            // risk stays $1.00: it is what was at risk at entry, not a figure retranslated at the exit rate.
            // The exit's own 200 JPY profit converts at 125 into $1.60, so R reads 1.6 — the rate move
            // shows up in R exactly as it showed up in the account.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_000m, 1, T0, stopPrice: 14_900m));
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 125m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_200m, 1, T0.AddDays(1)));

            Assert.Equal(1m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_CrossCurrencyScaleInAcrossARateMove_InitialRiskAnchorsOnTheOpeningRate()
        {
            // Open 1 @ 15,000 stop 14,900 at USD_JPY 100 ($1.00 at risk per unit). The rate moves to 125,
            // then a second unit is added at the same 100 JPY stop distance, worth only $0.80 at the new
            // rate. The add re-blends neither the distance nor the rate: 2 units at the opening $1.00.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_000m, 1, T0, stopPrice: 14_900m));
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 125m, T0.AddDays(1)));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_300m, 1, T0.AddDays(1), stopPrice: 15_200m));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_400m, 2, T0.AddDays(2)));

            Assert.Equal(2m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void ApplyTrade_CrossCurrencyPartialExit_InitialRiskScalesToEachExitedSlice()
        {
            // Open 4 @ 15,000 stop 14,900 at USD_JPY 100 ($1.00 per unit), then exit 3 and 1. Each slice
            // carries its own share of the converted per-unit risk: $3.00 then $1.00.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_000m, 4, T0, stopPrice: 14_900m));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_200m, 3, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_300m, 1, T0.AddDays(2)));

            Assert.Equal(2, portfolio.RoundTrips.Count);
            Assert.Equal(3m, portfolio.RoundTrips[0].InitialRisk);
            Assert.Equal(1m, portfolio.RoundTrips[1].InitialRisk);
        }

        [Fact]
        public void ApplyTrade_CrossCurrencyEntryWithoutStop_LeavesInitialRiskNull()
        {
            // Converting the risk never invents one: an entry with no protective stop still has no initial
            // risk and so no R, exactly as for an account-currency symbol.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 1, T0));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_200m, 1, T0.AddDays(1)));

            Assert.Null(Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void GetPerformanceStats_CrossCurrencyTripAcrossARateMove_AvgRDividesAccountCurrencyByAccountCurrency()
        {
            // The ADR 0032 worked example end to end through the stats: 100 JPY risked at USD_JPY 100 is
            // $1.00, 200 JPY made at USD_JPY 125 is $1.60, so Avg R is 1.6 — not the native price ratio of
            // 2.0 the mixed-unit division used to produce.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 100m, T0));
            portfolio.ApplyTrade(BuyWithStop("EUR_JPY", 15_000m, 1, T0, stopPrice: 14_900m));
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 125m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("EUR_JPY", 15_200m, 1, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("USD_JPY", 125m, T0.AddDays(2)));

            Assert.Equal(1.6m, portfolio.GetPerformanceStats().AvgRMultiple);
        }

        [Fact]
        public void ApplyTrade_EntryStopAndTargetStamped_RoundTripCarriesBothLevels()
        {
            // Buy 10 @ 100 with stop 90 and target 130: the round trip carries both entry-time levels verbatim.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithLevels("AAPL", 100m, 10, T0, stopPrice: 90m, targetPrice: 130m));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(90m, trip.EntryStopPrice);
            Assert.Equal(130m, trip.EntryTargetPrice);
        }

        [Fact]
        public void ApplyTrade_ScaleInAtDifferentLevels_LevelsAnchorOnOpeningEntry()
        {
            // Open 10 @ 100 stop 90 target 130; add 10 @ 120 stop 118 target 150. The add must not re-blend
            // the frozen entry levels: the round trip carries the opening entry's 90 / 130.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithLevels("AAPL", 100m, 10, T0, stopPrice: 90m, targetPrice: 130m));
            portfolio.ApplyTrade(BuyWithLevels("AAPL", 120m, 10, T0.AddDays(1), stopPrice: 118m, targetPrice: 150m));
            portfolio.ApplyTrade(Sell("AAPL", 140m, 20, T0.AddDays(2)));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(90m, trip.EntryStopPrice);
            Assert.Equal(130m, trip.EntryTargetPrice);
        }

        [Fact]
        public void ApplyTrade_PartialExit_EachSliceCarriesOpeningLevels()
        {
            // Open 20 @ 100 stop 90 target 130; exit 10 then 10. Each emitted slice carries the same frozen
            // entry levels, mirroring how initial risk scales per slice.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(BuyWithLevels("AAPL", 100m, 20, T0, stopPrice: 90m, targetPrice: 130m));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(2)));

            Assert.Equal(2, portfolio.RoundTrips.Count);
            Assert.All(portfolio.RoundTrips, trip =>
            {
                Assert.Equal(90m, trip.EntryStopPrice);
                Assert.Equal(130m, trip.EntryTargetPrice);
            });
        }

        [Fact]
        public void ApplyTrade_EntryWithoutStopOrTarget_LeavesBothLevelsNull()
        {
            // A plain entry declares neither leg, so the round trip carries no initial stop or target.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Null(trip.EntryStopPrice);
            Assert.Null(trip.EntryTargetPrice);
        }

        [Fact]
        public void ApplyTrade_ExitFromStopLeg_TagsRoundTripStopLoss()
        {
            Trade exit = Sell("AAPL", 90m, 10, T0.AddDays(1));
            exit.Leg = BracketLeg.StopLoss;
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(exit);

            Assert.Equal(ExitReason.StopLoss, Assert.Single(portfolio.RoundTrips).ExitReason);
        }

        [Fact]
        public void ApplyTrade_ExitFromTargetLeg_TagsRoundTripTakeProfit()
        {
            Trade exit = Sell("AAPL", 120m, 10, T0.AddDays(1));
            exit.Leg = BracketLeg.TakeProfit;
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(exit);

            Assert.Equal(ExitReason.TakeProfit, Assert.Single(portfolio.RoundTrips).ExitReason);
        }

        [Fact]
        public void ApplyTrade_ExitFromPlainOrder_TagsRoundTripSignal()
        {
            // A non-bracket exit (Leg None) is a deliberate strategy exit, reported as Signal.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));

            Assert.Equal(ExitReason.Signal, Assert.Single(portfolio.RoundTrips).ExitReason);
        }

        [Fact]
        public void ApplyTrade_BuyThenSell_CarriesEntryAndExitTimestamps()
        {
            // Buy at T0, sell one day later → EntryTime = T0, ExitTime = T0+1d
            DateTime entry = T0;
            DateTime exit = T0.AddDays(1);
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, entry));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, exit));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(entry, trip.EntryTime);
            Assert.Equal(exit, trip.ExitTime);
        }

        [Fact]
        public void ApplyTrade_TwoBuysAveragedThenSell_EntryTimeIsFirstBuyAndPriceAveraged()
        {
            // Two buys average into one position; the second buy must not overwrite the entry time,
            // and the round trip carries the volume-weighted entry (100*10 + 120*10) / 20 = 110.
            DateTime firstBuy = T0;
            DateTime secondBuy = T0.AddDays(1);
            DateTime exit = T0.AddDays(2);
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, firstBuy));
            portfolio.ApplyTrade(Buy("AAPL", 120m, 10, secondBuy));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 20, exit));

            RoundTrip trip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(firstBuy, trip.EntryTime);
            Assert.Equal(exit, trip.ExitTime);
            Assert.Equal(110m, trip.EntryPrice);
        }

        [Fact]
        public void ApplyTrade_PartialExit_EmitsRoundTripForClosedPortionWhilePositionLivesOn()
        {
            // Buy 20 @ 100, sell 10 @ 120: a round trip closes for the 10 sold while 10 remain open; the
            // later exit of the remainder emits a second round trip carrying the original entry time.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 20, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));

            RoundTrip first = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(10, first.Quantity);
            Assert.Equal(200m, first.RealizedPnL);
            Assert.Equal(T0, first.EntryTime);
            Assert.Equal(10, portfolio.Positions.Single(p => p.Symbol == "AAPL").Quantity);

            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(2)));

            Assert.Equal(2, portfolio.RoundTrips.Count);
            RoundTrip second = portfolio.RoundTrips[1];
            Assert.Equal(300m, second.RealizedPnL);
            Assert.Equal(T0, second.EntryTime);
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
        public void GetPerformanceStats_WithBreakEvenTrade_ExpectancyIsMeanOverAllTrades()
        {
            // Win:        buy 10@$100, sell 10@$110 → PnL = +$100
            // Loss:       buy 10@$110, sell 10@$105 → PnL = -$50
            // Break-even: buy 10@$100, sell 10@$100 → PnL =  $0
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 105m, 10, T0.AddDays(3)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 105m, T0.AddDays(3)));
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(4)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(4)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10, T0.AddDays(5)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(5)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(3, stats.Trades);
            Assert.Equal(1, stats.Winners);
            Assert.Equal(1, stats.Losers);
            Assert.Equal(1, stats.BreakEven);          // winners + break-even + losers = trades
            Assert.Equal(50m, stats.NetProfit);        // +100 - 50 + 0
            // Expectancy is the mean P&L over ALL trades, not WinRate*AvgWin + (1-WinRate)*AvgLoss
            // (which would give (1/3)*100 + (2/3)*(-50) = 0 because the break-even trade is mis-counted).
            Assert.Equal(50m / 3m, stats.Expectancy);
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
        public void GetPerformanceStats_AvgR_IsMeanOfPerTripRMultiples()
        {
            // Win:  buy 10@100 stop 90 (risk 10*10=100), sell 10@120 → +$200 → 2.0R
            // Loss: buy 10@110 stop 100 (risk 10*10=100), sell 10@105 → -$50 → -0.5R
            // Avg R is the plain mean of the two per-trip R's: (2.0 + -0.5) / 2 = 0.75.
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 10, T0, stopPrice: 90m));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(BuyWithStop("AAPL", 110m, 10, T0.AddDays(2), stopPrice: 100m));
            portfolio.ApplyTrade(Sell("AAPL", 105m, 10, T0.AddDays(3)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(0.75m, stats.AvgRMultiple);
        }

        [Fact]
        public void GetPerformanceStats_AvgR_ExcludesNoStopTripsFromSumAndDivisor()
        {
            // Stopped win: buy 10@100 stop 90 (risk 100), sell 10@110 → +$100 → 1.0R.
            // No-stop win:  buy 10@100 (no stop), sell 10@130 → +$300 → no R defined.
            // The no-stop trip is excluded entirely, so Avg R is the single 1.0R, not (1.0 + 0)/2 = 0.5.
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 10, T0, stopPrice: 90m));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 130m, 10, T0.AddDays(3)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(1.0m, stats.AvgRMultiple);
        }

        [Fact]
        public void GetPerformanceStats_AvgR_IsNullWhenNoTripHasDefinedRisk()
        {
            // No round trip declared an entry stop, so no R is defined anywhere: Avg R is a dash (null),
            // not zero.
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("AAPL", 120m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 110m, 10, T0.AddDays(3)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Null(stats.AvgRMultiple);
        }

        [Fact]
        public void GetPerformanceStatsBySymbol_AvgR_UsesPerTripDefinitionPerSymbol()
        {
            // AAPL: buy 10@100 stop 90 (risk 100), sell 10@120 → +$200 → 2.0R.
            // MSFT: buy 10@50 (no stop), sell 10@60 → no R defined, so MSFT's Avg R is a dash (null).
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(BuyWithStop("AAPL", 100m, 10, T0, stopPrice: 90m));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Buy("MSFT", 50m, 10, T0));
            portfolio.ApplyTrade(Sell("MSFT", 60m, 10, T0.AddDays(1)));

            IReadOnlyDictionary<string, PerformanceStats> bySymbol = portfolio.GetPerformanceStatsBySymbol();

            Assert.Equal(2.0m, bySymbol["AAPL"].AvgRMultiple);
            Assert.Null(bySymbol["MSFT"].AvgRMultiple);
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
        public void GetPerformanceStats_DirectionalNetProfit_PartitionsNetProfitByDirection()
        {
            // Longs: win +$200 (buy 10@100, sell 10@120) and loss -$100 (buy 10@110, sell 10@100) → +$100.
            // Short: win +$200 (sell 10@150, cover 10@130) → +$200. The two sum to NetProfit = $300.
            Portfolio portfolio = new(50_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, T0));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, T0.AddDays(1)));   // long win +200
            portfolio.ApplyTrade(Buy("AAPL", 110m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10, T0.AddDays(3)));   // long loss -100
            portfolio.ApplyTrade(Sell("MSFT", 150m, 10, T0.AddDays(4)));
            portfolio.ApplyTrade(Buy("MSFT", 130m, 10, T0.AddDays(5)));    // short win +200

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(100m, stats.NetProfitLong);
            Assert.Equal(200m, stats.NetProfitShort);
            Assert.Equal(stats.NetProfit, stats.NetProfitLong + stats.NetProfitShort);
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
        public void GetPerformanceStats_Leverage_GrossExposureOverEquityPeakAndExposedAverage()
        {
            // Start $10,000; buy 150@100 (notional $15,000, cash −$5,000).
            //   Bar @100: equity −5,000 + 15,000 = 10,000 → leverage 15,000/10,000 = 1.5x (peak).
            //   Bar @200: equity −5,000 + 30,000 = 25,000 → leverage 30,000/25,000 = 1.2x.
            //   Exit bar (flat): leverage 0, excluded from the average.
            // Avg over the two exposed bars = (1.5 + 1.2)/2 = 1.35; peak = 1.5.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 150, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 200m, 150, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(1.5m, stats.PeakLeverage);
            Assert.Equal(1.35m, stats.AvgLeverage);
        }

        [Fact]
        public void GetPerformanceStats_MarginUtilization_CommittedMarginOverEquityPeakAndExposedAverage()
        {
            // Start $10,000; buy 150@100 long (initial-margin rate 0.5).
            //   Bar @100: committed margin 0.5*15,000 = 7,500, equity 10,000 → 0.75 (peak).
            //   Bar @200: committed margin 0.5*30,000 = 15,000, equity 25,000 → 0.60.
            //   Exit bar (flat): 0, excluded from the average.
            // Avg over exposed bars = (0.75 + 0.60)/2 = 0.675; peak = 0.75.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 150, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 200m, 150, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(2)));

            PerformanceStats stats = portfolio.GetPerformanceStats();

            Assert.Equal(0.75m, stats.PeakMarginUtilization);
            Assert.Equal(0.675m, stats.AvgMarginUtilization);
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
        public void GetPerformanceStatsBySymbol_SingleSymbol_LeverageAndMarginMatchPortfolio()
        {
            // With one symbol, its isolated equity curve equals the portfolio's, so its per-symbol leverage
            // and margin utilization must match the whole-portfolio figures.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 150, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(1)));
            portfolio.ApplyTrade(Sell("AAPL", 200m, 150, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0.AddDays(2)));

            PerformanceStats portfolioStats = portfolio.GetPerformanceStats();
            PerformanceStats symbolStats = portfolio.GetPerformanceStatsBySymbol()["AAPL"];

            Assert.NotEqual(0m, portfolioStats.PeakLeverage);
            Assert.Equal(portfolioStats.PeakLeverage, symbolStats.PeakLeverage);
            Assert.Equal(portfolioStats.AvgLeverage, symbolStats.AvgLeverage);
            Assert.Equal(portfolioStats.PeakMarginUtilization, symbolStats.PeakMarginUtilization);
            Assert.Equal(portfolioStats.AvgMarginUtilization, symbolStats.AvgMarginUtilization);
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
