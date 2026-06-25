using System;
using System.Collections.Generic;
using System.IO;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Engine;
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
                Indicators = Array.Empty<ChartIndicator>(),
                EquityCurve = new[] { new ReportEquityPoint { Trade = 0, Equity = 10_000m } },
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
        public void BuildHtml_GroupsExpandedStatsIntoDrawdownDurationExposureAndWinLossCards()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"drawdown-stats\"", html);
            Assert.Contains("id=\"win-loss-stats\"", html);
            Assert.Contains("id=\"duration-stats\"", html);
            Assert.Contains("id=\"exposure-stats\"", html);
        }

        [Fact]
        public void BuildHtml_StatCards_HaveAllAndSelectedSymbolColumns()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // The Performance and Trade quality cards render two value columns the page can fill.
            Assert.Contains("All Symbols", html);
            Assert.Contains("Selected Symbol", html);
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
        public void BuildHtml_InlinesIndicatorWithNestedSeriesShape()
        {
            ReportModel model = SampleModel();
            model.Indicators = new[]
            {
                new ChartIndicator
                {
                    Name = "ATR(14)",
                    Pane = "separatePane",
                    Series = new[]
                    {
                        new ChartIndicatorSeries
                        {
                            Name = "ATR(14)",
                            Shape = "area",
                            Points = new[] { new ChartLinePoint { Time = 1_704_153_600, Value = 2.5m } }
                        }
                    }
                }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // The chart-ready indicator reaches the page: its container name and pane, and its nested
            // series with shape and time-aligned point, are inlined in the serialized model to draw.
            Assert.Contains("\"name\":\"ATR(14)\"", html);
            Assert.Contains("\"pane\":\"separatePane\"", html);
            Assert.Contains("\"shape\":\"area\"", html);
            Assert.Contains("\"time\":1704153600", html);
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

        /// <summary>A minimal backtest result: one AAPL bar, a 10,000 portfolio, no trades or indicators.</summary>
        private static BacktestResult SampleResult()
        {
            Dictionary<string, IReadOnlyList<Candle>> history = new()
            {
                ["AAPL"] = new[] { new Candle { Timestamp = T0, Open = 100m, High = 101m, Low = 99m, Close = 100.5m, Volume = 1000 } }
            };
            return new BacktestResult(history, new Portfolio(10_000m), Array.Empty<Indicator>(),
                new[] { "AAPL" }, "1d", T0, T0.AddYears(1), Array.Empty<RejectedOrder>());
        }

        [Fact]
        public void BuildHtml_FromResult_ProjectsAndEmbedsModel()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleResult());

            // The result's starting equity (10,000) reaches the run section, proving the writer ran the
            // builder internally rather than requiring a pre-built model.
            Assert.Contains("\"startingEquity\":10000", html);
        }

        [Fact]
        public void BuildHtml_FromResult_MatchesExplicitBuilderPath()
        {
            HtmlReportWriter writer = new();
            BacktestResult result = SampleResult();

            string fromResult = writer.BuildHtml(result);
            string fromModel = writer.BuildHtml(new ReportModelBuilder().Build(result));

            Assert.Equal(fromModel, fromResult);
        }

        [Fact]
        public void Write_FromResult_ProducesSameContentAsBuildHtml()
        {
            HtmlReportWriter writer = new();
            BacktestResult result = SampleResult();
            string path = Path.Combine(Path.GetTempPath(), "bt_report_test", Guid.NewGuid() + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            writer.Write(result, path);

            Assert.Equal(writer.BuildHtml(result), File.ReadAllText(path));
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
