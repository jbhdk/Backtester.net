using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Engine;
using Backtester.Report;
using Xunit;

namespace BacktesterTests.Report.Tests
{
    public class ReportModelBuilderTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private static IReadOnlyDictionary<string, IReadOnlyList<Candle>> NoCandles()
        {
            return new Dictionary<string, IReadOnlyList<Candle>>();
        }

        private static IReadOnlyList<IndicatorSeries> NoIndicators()
        {
            return Array.Empty<IndicatorSeries>();
        }

        /// <summary>Wraps a run's produced data in a result stamped with default run-config (AAPL, 1d, one year).</summary>
        private static BacktestResult Result(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> candleHistory,
            Portfolio portfolio,
            IReadOnlyList<IndicatorSeries> indicators,
            IReadOnlyList<RejectedOrder> rejectedOrders = null)
        {
            return new(candleHistory, portfolio, indicators, new[] { "AAPL" }, "1d", T0, T0.AddYears(1),
                rejectedOrders ?? Array.Empty<RejectedOrder>());
        }

        private static Trade Trade(string symbol, OrderSide side, decimal price, int qty, DateTime ts)
        {
            return new() { Id = Guid.NewGuid().ToString(), Symbol = symbol, Side = side, Price = price, Quantity = qty, Timestamp = ts };
        }

        private static MarketSlice Slice(string symbol, decimal close, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbol] = new Candle { Timestamp = ts, Open = close, High = close, Low = close, Close = close, Volume = 1000 }
                }
            };
        }

        /// <summary>A portfolio with one winning AAPL round trip (buy 10@100, sell 10@120 two bars later).</summary>
        private static Portfolio WinningPortfolio()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, 100m, 10, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 110m, T0.AddDays(1)));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, 120m, 10, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 120m, T0.AddDays(2)));
            return portfolio;
        }

        /// <summary>A result with a single AAPL round trip over the given times, prices, and quantity.</summary>
        private static BacktestResult ResultWithRoundTrip(DateTime entry, DateTime exit, decimal entryPrice, decimal exitPrice, int qty)
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, entryPrice, qty, entry));
            portfolio.RecordEquitySnapshot(Slice("AAPL", entryPrice, entry));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, exitPrice, qty, exit));
            portfolio.RecordEquitySnapshot(Slice("AAPL", exitPrice, exit));
            return Result(NoCandles(), portfolio, NoIndicators());
        }

        /// <summary>A result with a single AAPL short round trip (sell entry, buy cover) over the given times.</summary>
        private static BacktestResult ResultWithShortRoundTrip(DateTime entry, DateTime exit, decimal entryPrice, decimal exitPrice, int qty)
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, entryPrice, qty, entry));
            portfolio.RecordEquitySnapshot(Slice("AAPL", entryPrice, entry));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, exitPrice, qty, exit));
            portfolio.RecordEquitySnapshot(Slice("AAPL", exitPrice, exit));
            return Result(NoCandles(), portfolio, NoIndicators());
        }

        private static long Unix(DateTime ts)
        {
            return new DateTimeOffset(ts, TimeSpan.Zero).ToUnixTimeSeconds();
        }

        [Fact]
        public void Build_RoundTrip_MapsCoreFields()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            ReportRoundTrip rt = Assert.Single(model.RoundTrips);
            Assert.Equal(1, rt.Number);
            Assert.Equal("AAPL", rt.Symbol);
            Assert.Equal(T0, rt.EntryTime);
            Assert.Equal(T0.AddDays(2), rt.ExitTime);
            Assert.Equal(100m, rt.EntryPrice);
            Assert.Equal(120m, rt.ExitPrice);
            Assert.Equal(10, rt.Quantity);
            Assert.Equal(200m, rt.RealizedPnL);
        }

        [Fact]
        public void Build_RoundTrip_CarriesLongDirection()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal("Long", Assert.Single(model.RoundTrips).Direction);
        }

        [Fact]
        public void Build_RoundTrip_CarriesShortDirection()
        {
            BacktestResult result = ResultWithShortRoundTrip(T0, T0.AddDays(2), 150m, 140m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal("Short", Assert.Single(model.RoundTrips).Direction);
        }

        [Fact]
        public void Build_RoundTrip_DerivesReturnPercent()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            // (120 - 100) / 100 = 0.2
            Assert.Equal(0.2m, Assert.Single(model.RoundTrips).ReturnPercent);
        }

        [Theory]
        [InlineData(5, 6, 0, "5d 6h")]
        [InlineData(0, 3, 30, "3h 30m")]
        [InlineData(0, 0, 45, "45m")]
        public void Build_RoundTrip_FormatsTimeHeldCompactly(int days, int hours, int minutes, string expected)
        {
            DateTime exit = T0.AddDays(days).AddHours(hours).AddMinutes(minutes);
            // Flat prices: this test isolates holding-time formatting and avoids CAGR over a sub-day span.
            BacktestResult result = ResultWithRoundTrip(T0, exit, 100m, 100m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(expected, Assert.Single(model.RoundTrips).TimeHeld);
        }

        [Fact]
        public void Build_RejectedOrders_MapsAttemptDetailAndSideToDirection()
        {
            RejectedOrder[] rejected =
            {
                new() { Symbol = "IEF", Side = OrderSide.Buy, Quantity = 210, Price = 95.10m, Timestamp = T0, Reason = "Not enough funds" },
                new() { Symbol = "QQQ", Side = OrderSide.Sell, Quantity = 40, Price = 480m, Timestamp = T0.AddHours(1), Reason = "Not enough funds" }
            };
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), NoIndicators(), rejected);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(2, model.RejectedOrders.Count);
            ReportRejectedOrder buy = model.RejectedOrders[0];
            Assert.Equal("IEF", buy.Symbol);
            Assert.Equal("Long", buy.Direction);
            Assert.Equal(T0, buy.Time);
            Assert.Equal(95.10m, buy.Price);
            Assert.Equal(210, buy.Quantity);
            Assert.Equal("Not enough funds", buy.Reason);
            Assert.Equal("Short", model.RejectedOrders[1].Direction);
        }

        [Fact]
        public void Build_RejectedOrders_NoneRejected_YieldsEmptyList()
        {
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Empty(model.RejectedOrders);
        }

        [Fact]
        public void Build_Chart_EntryMarker_BelowBarArrowUpAtEntryTime()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartMarker entry = Assert.Single(model.Chart.Markers, marker => marker.Shape == "arrowUp");
            Assert.Equal("AAPL", entry.Symbol);
            Assert.Equal(new DateTimeOffset(T0, TimeSpan.Zero).ToUnixTimeSeconds(), entry.Time);
            Assert.Equal("belowBar", entry.Position);
        }

        [Fact]
        public void Build_Chart_ExitMarker_AboveBarArrowDownAtExitTime()
        {
            DateTime exit = T0.AddDays(2);
            BacktestResult result = ResultWithRoundTrip(T0, exit, 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartMarker exitMarker = Assert.Single(model.Chart.Markers, marker => marker.Shape == "arrowDown");
            Assert.Equal("AAPL", exitMarker.Symbol);
            Assert.Equal(new DateTimeOffset(exit, TimeSpan.Zero).ToUnixTimeSeconds(), exitMarker.Time);
            Assert.Equal("aboveBar", exitMarker.Position);
        }

        [Fact]
        public void Build_Chart_ShortEntryMarker_AboveBarArrowDownAtEntryTime()
        {
            BacktestResult result = ResultWithShortRoundTrip(T0, T0.AddDays(2), 150m, 140m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartMarker entry = Assert.Single(model.Chart.Markers, marker => marker.Time == Unix(T0));
            Assert.Equal("arrowDown", entry.Shape);
            Assert.Equal("aboveBar", entry.Position);
        }

        [Fact]
        public void Build_Chart_ShortExitMarker_BelowBarArrowUpAtExitTime()
        {
            DateTime exit = T0.AddDays(2);
            BacktestResult result = ResultWithShortRoundTrip(T0, exit, 150m, 140m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartMarker cover = Assert.Single(model.Chart.Markers, marker => marker.Time == Unix(exit));
            Assert.Equal("arrowUp", cover.Shape);
            Assert.Equal("belowBar", cover.Position);
        }

        [Theory]
        [InlineData(120, "#2ecc71")] // winning trip -> green markers
        [InlineData(80, "#e74c3c")]  // losing trip -> red markers
        public void Build_Chart_MarkersColouredByWinLoss(double exitPrice, string expectedColor)
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, (decimal)exitPrice, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.All(model.Chart.Markers, marker => Assert.Equal(expectedColor, marker.Color));
        }

        [Fact]
        public void Build_Chart_Markers_CarryOwningRoundTripNumber()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            // The single round trip is number 1; both its entry and exit markers carry that number so
            // the page can highlight the matching table row when a marker is hovered.
            Assert.All(model.Chart.Markers, marker => Assert.Equal(1, marker.RoundTripNumber));
        }

        [Theory]
        [InlineData(120, "+$200.00")] // winning trip P&L label
        [InlineData(80, "-$200.00")]  // losing trip P&L label
        public void Build_Chart_MarkerText_CarriesSignedPnL(double exitPrice, string expectedText)
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, (decimal)exitPrice, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.All(model.Chart.Markers, marker => Assert.Equal(expectedText, marker.Text));
        }

        [Fact]
        public void Build_Indicators_ProjectsExposedSeriesPreservingName()
        {
            IndicatorSeries sma = new("SMA(20)", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), new[] { sma });

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartIndicator indicator = Assert.Single(model.Indicators);
            Assert.Equal("SMA(20)", indicator.Name);
        }

        [Fact]
        public void Build_Indicators_CarrySymbolForPerSymbolScoping()
        {
            IndicatorSeries sma = new("SMA(20)", "AAPL", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), new[] { sma });

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal("AAPL", Assert.Single(model.Indicators).Symbol);
        }

        [Fact]
        public void Build_Indicators_EncodesPointTimesAsUtcSeconds()
        {
            IndicatorSeries sma = new("SMA(20)", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 123.5m } });
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), new[] { sma });

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartLinePoint point = Assert.Single(Assert.Single(model.Indicators).Points);
            Assert.Equal(new DateTimeOffset(T0, TimeSpan.Zero).ToUnixTimeSeconds(), point.Time);
            Assert.Equal(123.5m, point.Value);
        }

        [Theory]
        [InlineData(IndicatorPane.PriceOverlay, "priceOverlay")]
        [InlineData(IndicatorPane.SeparatePane, "separatePane")]
        public void Build_Indicators_MapsPaneDesignationToPageString(IndicatorPane pane, string expected)
        {
            IndicatorSeries series = new("ATR", pane, new[] { new IndicatorPoint { Timestamp = T0, Value = 2m } });
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), new[] { series });

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(expected, Assert.Single(model.Indicators).Pane);
        }

        [Fact]
        public void Build_Indicators_DrawsExactlyThoseExposedInOrder()
        {
            IndicatorSeries sma = new("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            IndicatorSeries atr = new("ATR", IndicatorPane.SeparatePane, new[] { new IndicatorPoint { Timestamp = T0, Value = 2m } });
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), new[] { sma, atr });

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(new[] { "SMA", "ATR" }, model.Indicators.Select(indicator => indicator.Name));
        }

        [Fact]
        public void Build_Indicators_NoneExposed_YieldsEmptyList()
        {
            BacktestResult result = Result(NoCandles(), new Portfolio(10_000m), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Empty(model.Indicators);
        }

        [Fact]
        public void Build_EquityCurve_StartsAtStartingEquityAsTradeZero()
        {
            BacktestResult result = Result(NoCandles(), WinningPortfolio(), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            ReportEquityPoint start = model.EquityCurve[0];
            Assert.Equal(0, start.Trade);
            Assert.Equal(10_000m, start.Equity);
        }

        [Fact]
        public void Build_EquityCurve_AccumulatesRealizedPnLPerClosedTrade()
        {
            Portfolio portfolio = new(10_000m);
            // Trade 1: +200 (buy 10@100, sell 10@120); Trade 2: -50 (buy 10@100, sell 10@95).
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, 100m, 10, T0));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, 120m, 10, T0.AddDays(2)));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, 100m, 10, T0.AddDays(3)));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, 95m, 10, T0.AddDays(5)));
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            // Trade-indexed cumulative realized equity: 10,000 → 10,200 → 10,150.
            Assert.Equal(new[] { 0, 1, 2 }, model.EquityCurve.Select(point => point.Trade));
            Assert.Equal(new[] { 10_000m, 10_200m, 10_150m }, model.EquityCurve.Select(point => point.Equity));
        }

        [Fact]
        public void Build_Chart_EncodesCandleTimesAsUtcSeconds()
        {
            Candle aapl = new() { Timestamp = T0, Open = 100m, High = 101m, Low = 99m, Close = 100.5m, Volume = 1000 };
            Dictionary<string, IReadOnlyList<Candle>> history = new() { ["AAPL"] = new[] { aapl } };
            BacktestResult result = Result(history, new Portfolio(10_000m), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            ChartCandle bar = Assert.Single(model.Chart.Series["AAPL"]);
            Assert.Equal(new DateTimeOffset(T0, TimeSpan.Zero).ToUnixTimeSeconds(), bar.Time);
            Assert.Equal(100m, bar.Open);
            Assert.Equal(101m, bar.High);
            Assert.Equal(99m, bar.Low);
            Assert.Equal(100.5m, bar.Close);
        }

        [Fact]
        public void Build_Model_SerializesWithSystemTextJson()
        {
            IndicatorSeries sma = new("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            Dictionary<string, IReadOnlyList<Candle>> history = new()
            {
                ["AAPL"] = new[] { new Candle { Timestamp = T0, Open = 100m, High = 101m, Low = 99m, Close = 100.5m, Volume = 1000 } }
            };
            BacktestResult result = Result(history, WinningPortfolio(), new[] { sma });

            ReportModel model = new ReportModelBuilder().Build(result);
            string json = JsonSerializer.Serialize(model);

            Assert.Contains("\"Stats\"", json);
            Assert.Contains("\"RoundTrips\"", json);
            Assert.Contains("\"EquityCurve\"", json);
            Assert.Contains("\"Run\"", json);
        }

        [Fact]
        public void Build_Stats_FaithfullyMapsPerformanceStats()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());
            PerformanceStats expected = portfolio.GetPerformanceStats();

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(expected.NetProfit, model.Stats.NetProfit);
            Assert.Equal(expected.Trades, model.Stats.Trades);
            Assert.Equal(expected.WinRate, model.Stats.WinRate);
            Assert.Equal(expected.ProfitFactor, model.Stats.ProfitFactor);
            Assert.Equal(expected.AvgWin, model.Stats.AvgWin);
            Assert.Equal(expected.AvgLoss, model.Stats.AvgLoss);
            Assert.Equal(expected.Expectancy, model.Stats.Expectancy);
            Assert.Equal(expected.MaxDrawdown, model.Stats.MaxDrawdown);
            Assert.Equal(expected.Cagr, model.Stats.Cagr);
            Assert.Equal(expected.Sharpe, model.Stats.Sharpe);
            Assert.Equal(expected.MaxConsecLosses, model.Stats.MaxConsecLosses);
        }

        [Fact]
        public void Build_StatsBySymbol_KeyedBySymbolWithPerSymbolNetProfit()
        {
            // AAPL: +$200 (buy 10@100, sell 10@120). MSFT: -$50 (buy 10@50, sell 10@45).
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, 100m, 10, T0));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, 120m, 10, T0.AddDays(1)));
            portfolio.ApplyTrade(Trade("MSFT", OrderSide.Buy, 50m, 10, T0));
            portfolio.ApplyTrade(Trade("MSFT", OrderSide.Sell, 45m, 10, T0.AddDays(1)));
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(200m, model.StatsBySymbol["AAPL"].NetProfit);
            Assert.Equal(-50m, model.StatsBySymbol["MSFT"].NetProfit);
        }

        [Fact]
        public void Build_StatsBySymbol_PopulatesPerSymbolMaxDrawdown()
        {
            // AAPL isolated equity peaks at $40,000 then troughs at $30,000 = 25% drawdown.
            Portfolio portfolio = new(30_000m);
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Buy, 100m, 100, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 200m, T0));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(1)));
            portfolio.ApplyTrade(Trade("AAPL", OrderSide.Sell, 100m, 100, T0.AddDays(2)));
            portfolio.RecordEquitySnapshot(Slice("AAPL", 100m, T0.AddDays(2)));
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(0.25m, model.StatsBySymbol["AAPL"].MaxDrawdown);
        }

        [Fact]
        public void Build_Stats_DerivesNetProfitPercentFromStartingEquity()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            // Net profit 200 on 10,000 starting equity = 0.02
            Assert.Equal(0.02m, model.Stats.NetProfitPercent);
        }

        [Fact]
        public void Build_RunInfo_DerivesFinalEquityAndTotalReturn()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            // Final marked equity after the winning exit is 10,200 → total return (10200-10000)/10000 = 0.02
            Assert.Equal(10_200m, model.Run.FinalEquity);
            Assert.Equal(0.02m, model.Run.TotalReturnPercent);
        }

        [Fact]
        public void Build_RunInfo_EchoesRunInputs()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators(), new[] { "AAPL", "MSFT" }, "1h", T0, T0.AddDays(30), Array.Empty<RejectedOrder>());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(new[] { "AAPL", "MSFT" }, model.Run.Symbols);
            Assert.Equal("1h", model.Run.Interval);
            Assert.Equal(T0, model.Run.FromUtc);
            Assert.Equal(T0.AddDays(30), model.Run.ToUtc);
        }

        [Fact]
        public void Build_RunContext_CarriesStartingEquity()
        {
            Portfolio portfolio = new(10_000m);
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(10_000m, model.Run.StartingEquity);
        }

        /// <summary>An AAPL candle series running 100 → 120 (a +20% buy-and-hold return).</summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<Candle>> AaplCandles()
        {
            return new Dictionary<string, IReadOnlyList<Candle>>
            {
                ["AAPL"] = new[]
                {
                    new Candle { Timestamp = T0, Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000 },
                    new Candle { Timestamp = T0.AddDays(2), Open = 120m, High = 120m, Low = 120m, Close = 120m, Volume = 1000 }
                }
            };
        }

        /// <summary>AAPL (100 → 120, +20%) plus a flat second symbol, giving a two-symbol benchmark.</summary>
        private static IReadOnlyDictionary<string, IReadOnlyList<Candle>> TwoSymbolCandles()
        {
            return new Dictionary<string, IReadOnlyList<Candle>>
            {
                ["AAPL"] = new[]
                {
                    new Candle { Timestamp = T0, Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000 },
                    new Candle { Timestamp = T0.AddDays(2), Open = 120m, High = 120m, Low = 120m, Close = 120m, Volume = 1000 }
                },
                ["MSFT"] = new[]
                {
                    new Candle { Timestamp = T0, Open = 200m, High = 200m, Low = 200m, Close = 200m, Volume = 1000 },
                    new Candle { Timestamp = T0.AddDays(2), Open = 200m, High = 200m, Low = 200m, Close = 200m, Volume = 1000 }
                }
            };
        }

        [Fact]
        public void Build_Stats_DerivesBuyHoldReturnFromCandleHistory()
        {
            // AAPL closes 100 → 120 over the run → buy-and-hold return = 0.2.
            BacktestResult result = Result(AaplCandles(), WinningPortfolio(), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(0.2m, model.Stats.BuyHoldReturnPercent);
        }

        [Fact]
        public void Build_StatsBySymbol_ScalesBuyHoldToEqualWeightContribution()
        {
            // AAPL +20% buy-and-hold alongside one other benchmark symbol → its contribution to an
            // equal-weight two-symbol benchmark is 0.2 / 2 = 0.1, the same capital base as net profit %.
            BacktestResult result = Result(TwoSymbolCandles(), WinningPortfolio(), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(0.1m, model.StatsBySymbol["AAPL"].BuyHoldReturnPercent);
        }

        [Fact]
        public void Build_Stats_FormatsTradeDurationsAsCompactStrings()
        {
            // A single round trip held exactly two days → every duration metric formats to "2d 0h".
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal("2d 0h", model.Stats.AvgTradeDuration);
            Assert.Equal("2d 0h", model.Stats.LongestTradeDuration);
            Assert.Equal("2d 0h", model.Stats.ShortestTradeDuration);
        }

        [Fact]
        public void Build_Stats_MapsExpandedScalarMetrics()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = Result(NoCandles(), portfolio, NoIndicators());
            PerformanceStats expected = portfolio.GetPerformanceStats();

            ReportModel model = new ReportModelBuilder().Build(result);

            Assert.Equal(expected.MaxConsecWins, model.Stats.MaxConsecWins);
            Assert.Equal(expected.Sortino, model.Stats.Sortino);
            Assert.Equal(expected.Calmar, model.Stats.Calmar);
            Assert.Equal(expected.MarketExposure, model.Stats.MarketExposure);
            Assert.Equal(expected.MaxCapitalInvested, model.Stats.MaxCapitalInvested);
            Assert.Equal(expected.LargestWin, model.Stats.LargestWin);
        }
    }
}
