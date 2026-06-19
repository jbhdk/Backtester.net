using System;
using System.Collections.Generic;
using System.Text.Json;
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

        private static ReportRunContext Context(decimal startingEquity = 10_000m)
        {
            return new ReportRunContext(new[] { "AAPL" }, "1d", T0, T0.AddYears(1), startingEquity);
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
            return new(NoCandles(), portfolio, NoIndicators());
        }

        [Fact]
        public void Build_RoundTrip_MapsCoreFields()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result, Context());

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
        public void Build_RoundTrip_DerivesReturnPercent()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result, Context());

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

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            Assert.Equal(expected, Assert.Single(model.RoundTrips).TimeHeld);
        }

        [Fact]
        public void Build_Chart_EntryMarker_BelowBarArrowUpAtEntryTime()
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, 120m, 10);

            ReportModel model = new ReportModelBuilder().Build(result, Context());

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

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            ChartMarker exitMarker = Assert.Single(model.Chart.Markers, marker => marker.Shape == "arrowDown");
            Assert.Equal("AAPL", exitMarker.Symbol);
            Assert.Equal(new DateTimeOffset(exit, TimeSpan.Zero).ToUnixTimeSeconds(), exitMarker.Time);
            Assert.Equal("aboveBar", exitMarker.Position);
        }

        [Theory]
        [InlineData(120, "#2ecc71")] // winning trip -> green markers
        [InlineData(80, "#e74c3c")]  // losing trip -> red markers
        public void Build_Chart_MarkersColouredByWinLoss(double exitPrice, string expectedColor)
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, (decimal)exitPrice, 10);

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            Assert.All(model.Chart.Markers, marker => Assert.Equal(expectedColor, marker.Color));
        }

        [Theory]
        [InlineData(120, "+$200.00")] // winning trip P&L label
        [InlineData(80, "-$200.00")]  // losing trip P&L label
        public void Build_Chart_MarkerText_CarriesSignedPnL(double exitPrice, string expectedText)
        {
            BacktestResult result = ResultWithRoundTrip(T0, T0.AddDays(2), 100m, (decimal)exitPrice, 10);

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            Assert.All(model.Chart.Markers, marker => Assert.Equal(expectedText, marker.Text));
        }

        [Fact]
        public void Build_Indicators_PresentWithPanePlacement()
        {
            IndicatorSeries sma = new("SMA", IndicatorPane.PriceOverlay, new[] { new IndicatorPoint { Timestamp = T0, Value = 100m } });
            IndicatorSeries rsi = new("RSI", IndicatorPane.SeparatePane, new[] { new IndicatorPoint { Timestamp = T0, Value = 55m } });
            BacktestResult result = new(NoCandles(), new Portfolio(10_000m), new[] { sma, rsi });

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            Assert.Equal(2, model.Indicators.Count);
            Assert.Equal("SMA", model.Indicators[0].Name);
            Assert.Equal(IndicatorPane.PriceOverlay, model.Indicators[0].Pane);
            Assert.Equal(IndicatorPane.SeparatePane, model.Indicators[1].Pane);
        }

        [Fact]
        public void Build_EquityCurve_MapsMarkedEquityPerSnapshot()
        {
            BacktestResult result = new(NoCandles(), WinningPortfolio(), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result, Context());

            // Marked equity across the three recorded bars: 10,000 → 10,100 → 10,200
            Assert.Equal(3, model.EquityCurve.Count);
            Assert.Equal(T0, model.EquityCurve[0].Timestamp);
            Assert.Equal(10_000m, model.EquityCurve[0].Equity);
            Assert.Equal(10_100m, model.EquityCurve[1].Equity);
            Assert.Equal(10_200m, model.EquityCurve[2].Equity);
        }

        [Fact]
        public void Build_Chart_EncodesCandleTimesAsUtcSeconds()
        {
            Candle aapl = new() { Timestamp = T0, Open = 100m, High = 101m, Low = 99m, Close = 100.5m, Volume = 1000 };
            Dictionary<string, IReadOnlyList<Candle>> history = new() { ["AAPL"] = new[] { aapl } };
            BacktestResult result = new(history, new Portfolio(10_000m), NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result, Context());

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
            BacktestResult result = new(history, WinningPortfolio(), new[] { sma });

            ReportModel model = new ReportModelBuilder().Build(result, Context());
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
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators());
            PerformanceStats expected = portfolio.GetPerformanceStats();

            ReportModel model = new ReportModelBuilder().Build(result, Context());

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
        public void Build_Stats_DerivesNetProfitPercentFromStartingEquity()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result, Context(startingEquity: 10_000m));

            // Net profit 200 on 10,000 starting equity = 0.02
            Assert.Equal(0.02m, model.Stats.NetProfitPercent);
        }

        [Fact]
        public void Build_RunInfo_DerivesFinalEquityAndTotalReturn()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result, Context(startingEquity: 10_000m));

            // Final marked equity after the winning exit is 10,200 → total return (10200-10000)/10000 = 0.02
            Assert.Equal(10_200m, model.Run.FinalEquity);
            Assert.Equal(0.02m, model.Run.TotalReturnPercent);
        }

        [Fact]
        public void Build_RunInfo_EchoesRunInputs()
        {
            Portfolio portfolio = WinningPortfolio();
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators());
            ReportRunContext context = new(new[] { "AAPL", "MSFT" }, "1h", T0, T0.AddDays(30), 10_000m);

            ReportModel model = new ReportModelBuilder().Build(result, context);

            Assert.Equal(new[] { "AAPL", "MSFT" }, model.Run.Symbols);
            Assert.Equal("1h", model.Run.Interval);
            Assert.Equal(T0, model.Run.FromUtc);
            Assert.Equal(T0.AddDays(30), model.Run.ToUtc);
        }

        [Fact]
        public void Build_RunContext_CarriesStartingEquity()
        {
            Portfolio portfolio = new(10_000m);
            BacktestResult result = new(NoCandles(), portfolio, NoIndicators());

            ReportModel model = new ReportModelBuilder().Build(result, Context(startingEquity: 10_000m));

            Assert.Equal(10_000m, model.Run.StartingEquity);
        }
    }
}
