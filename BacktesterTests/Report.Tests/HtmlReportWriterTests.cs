using System;
using System.Collections.Generic;
using System.IO;
using Backtester.Core;
using Backtester.Report;
using Xunit;

namespace BacktesterTests.Report.Tests
{
    public class HtmlReportWriterTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A representative report model with one winning AAPL round trip.</summary>
        private static ReportModel SampleModel()
        {
            return new ReportModel
            {
                Stats = new ReportStats
                {
                    NetProfit = 200m,
                    NetProfitPercent = 0.02m,
                    Trades = 1,
                    WinRate = 1m,
                    ProfitFactor = 0m,
                    AvgWin = 200m,
                    AvgLoss = 0m,
                    Expectancy = 200m,
                    MaxDrawdown = 0m,
                    Cagr = 0.05m,
                    Sharpe = 1.2m,
                    MaxConsecLosses = 0
                },
                RoundTrips = new[]
                {
                    new ReportRoundTrip
                    {
                        Number = 1, Symbol = "AAPL", EntryTime = T0, ExitTime = T0.AddDays(2),
                        EntryPrice = 100m, ExitPrice = 120m, Quantity = 10, RealizedPnL = 200m,
                        ReturnPercent = 0.2m, TimeHeld = "2d 0h"
                    }
                },
                Indicators = Array.Empty<IndicatorSeries>(),
                EquityCurve = new[] { new ReportEquityPoint { Timestamp = T0, Equity = 10_000m } },
                Chart = new ReportChart { Series = new Dictionary<string, IReadOnlyList<ChartCandle>>() },
                Run = new ReportRunInfo
                {
                    Symbols = new[] { "AAPL" }, Interval = "1d", FromUtc = T0, ToUtc = T0.AddYears(1),
                    StartingEquity = 10_000m, FinalEquity = 10_200m, TotalReturnPercent = 0.02m
                }
            };
        }

        [Fact]
        public void BuildHtml_EmbedsSerializedModel()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("\"netProfit\":200", html);
        }

        [Fact]
        public void BuildHtml_ReplacesPlaceholderToken_NoneLeftOver()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.DoesNotContain("__REPORT_DATA__", html);
        }

        [Fact]
        public void BuildHtml_ContainsStatsPanelContainer()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"stats-panel\"", html);
        }

        [Fact]
        public void BuildHtml_GroupsStatsIntoHeadlineTradeQualityAndRunContext()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"headline-stats\"", html);
            Assert.Contains("id=\"trade-quality-stats\"", html);
            Assert.Contains("id=\"run-context\"", html);
        }

        [Fact]
        public void BuildHtml_ContainsRoundTripsTable()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"round-trips\"", html);
        }

        [Fact]
        public void BuildHtml_ContainsGrossPnLFootnote()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("gross of commission and slippage", html);
        }

        [Fact]
        public void BuildHtml_InlinesLightweightChartsLibrary()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // The vendored standalone build's banner and the global it defines: proves the chart
            // library is inlined (offline, no CDN) rather than referenced externally.
            Assert.Contains("Lightweight Charts", html);
            Assert.Contains("LightweightCharts", html);
        }

        [Fact]
        public void BuildHtml_LeavesNoChartLibToken()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.DoesNotContain("__CHART_LIB__", html);
        }

        [Fact]
        public void BuildHtml_ContainsPriceChartContainer()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"price-chart\"", html);
        }

        [Fact]
        public void Write_ProducesFileWithSameContentAsBuildHtml()
        {
            HtmlReportWriter writer = new();
            ReportModel model = SampleModel();
            string path = Path.Combine(Path.GetTempPath(), "bt_report_test", Guid.NewGuid() + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            writer.Write(model, path);

            Assert.True(File.Exists(path));
            Assert.Equal(writer.BuildHtml(model), File.ReadAllText(path));
        }

        [Fact]
        public void BuildHtml_SerializesModelAsCamelCase()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("\"roundTrips\"", html);
            Assert.Contains("\"realizedPnL\"", html);
            Assert.Contains("\"totalReturnPercent\"", html);
        }
    }
}
