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
        public void BuildHtml_ContainsConfigurationSectionContainer()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // The configuration section is a static container the page's script fills from the model.
            Assert.Contains("id=\"configuration\"", html);
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
        public void BuildHtml_InlinesMultiSeriesIndicatorInOnePane()
        {
            ReportModel model = SampleModel();
            model.Indicators = new[]
            {
                new ChartIndicator
                {
                    Name = "MACD",
                    Pane = "separatePane",
                    Series = new[]
                    {
                        new ChartIndicatorSeries { Name = "MACD", Shape = "line", Points = new[] { new ChartLinePoint { Time = 1_704_153_600, Value = 1m } } },
                        new ChartIndicatorSeries { Name = "Signal", Shape = "line", Points = new[] { new ChartLinePoint { Time = 1_704_153_600, Value = 0.8m } } },
                        new ChartIndicatorSeries { Name = "Histogram", Shape = "histogram", Points = new[] { new ChartLinePoint { Time = 1_704_153_600, Value = 0.2m } } }
                    }
                }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // The whole MACD-shaped indicator reaches the page: its container name and pane, and all
            // three series with their names, shapes, and points, inlined for the page to draw in one pane.
            Assert.Contains("\"name\":\"MACD\"", html);
            Assert.Contains("\"pane\":\"separatePane\"", html);
            Assert.Contains("\"name\":\"Signal\"", html);
            Assert.Contains("\"name\":\"Histogram\"", html);
            Assert.Contains("\"shape\":\"histogram\"", html);
            Assert.Contains("\"value\":0.8", html);
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
                new[] { "AAPL" }, "1d", T0, T0.AddYears(1), Array.Empty<RejectedOrder>(), Array.Empty<BracketLevelChange>());
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
        public void BuildHtml_FromResult_EmbedsConfigurationCardTitle()
        {
            IReadOnlyList<ReportCard> configuration = new[]
            {
                new ReportCard { Title = "Strategy settings" }
            };

            string html = new HtmlReportWriter().BuildHtml(SampleResult(), configuration);

            // The caller-supplied card's title reaches the serialized model inlined in the page.
            Assert.Contains("\"title\":\"Strategy settings\"", html);
        }

        [Fact]
        public void BuildHtml_FromResult_EmbedsConfigurationCardRowCells()
        {
            IReadOnlyList<ReportCard> configuration = new[]
            {
                new ReportCard
                {
                    Title = "Strategy settings",
                    Rows = new[] { new[] { "Fast period", "10" } }
                }
            };

            string html = new HtmlReportWriter().BuildHtml(SampleResult(), configuration);

            // Each row's cells reach the serialized model as plain strings for the page to lay out.
            Assert.Contains("\"Fast period\"", html);
            Assert.Contains("\"10\"", html);
        }

        [Fact]
        public void BuildHtml_FromResult_EmbedsConfigurationCardHeaders()
        {
            IReadOnlyList<ReportCard> configuration = new[]
            {
                new ReportCard
                {
                    Title = "Execution models",
                    Headers = new[] { "Setting", "Value" },
                    Rows = new[] { new[] { "Commission", "0.5%" } }
                }
            };

            string html = new HtmlReportWriter().BuildHtml(SampleResult(), configuration);

            // A card's column headers reach the serialized model for the page to render as a header row.
            Assert.Contains("\"Setting\"", html);
            Assert.Contains("\"Value\"", html);
        }

        [Fact]
        public void BuildHtml_FromResult_NullConfigurationMatchesZeroCardOverload()
        {
            HtmlReportWriter writer = new();
            BacktestResult result = SampleResult();

            string withNull = writer.BuildHtml(result, null);
            string zeroCard = writer.BuildHtml(result);

            // Supplying no configuration leaves the report byte-for-byte identical to the existing path.
            Assert.Equal(zeroCard, withNull);
        }

        [Fact]
        public void Write_FromResult_WithConfiguration_MatchesBuildHtml()
        {
            HtmlReportWriter writer = new();
            BacktestResult result = SampleResult();
            IReadOnlyList<ReportCard> configuration = new[]
            {
                new ReportCard { Title = "Strategy settings", Rows = new[] { new[] { "Fast period", "10" } } }
            };
            string path = Path.Combine(Path.GetTempPath(), "bt_report_test", Guid.NewGuid() + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            writer.Write(result, path, configuration);

            // Writing with configuration produces exactly the HTML the build overload returns.
            Assert.Equal(writer.BuildHtml(result, configuration), File.ReadAllText(path));
        }

        [Fact]
        public void BuildHtml_EmbedsAnalysisSummary()
        {
            ReportModel model = SampleModel();
            model.Analysis = new ReportAnalysis { Summary = "The run is profitable but thinly traded." };

            string html = new HtmlReportWriter().BuildHtml(model);

            // The attached Analysis's summary reaches the serialized model inlined in the page.
            Assert.Contains("\"summary\":\"The run is profitable but thinly traded.\"", html);
        }

        [Fact]
        public void BuildHtml_EmbedsFindingObservationAndRecommendationSeparately()
        {
            ReportModel model = SampleModel();
            model.Analysis = new ReportAnalysis
            {
                Findings = new[]
                {
                    new ReportFinding
                    {
                        Title = "Single trade",
                        Observation = "The run closed one round trip.",
                        Recommendation = "Widen the date range before drawing conclusions."
                    }
                }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // Evidence and prescription reach the page as separate members, never merged into one field.
            Assert.Contains("\"title\":\"Single trade\"", html);
            Assert.Contains("\"observation\":\"The run closed one round trip.\"", html);
            Assert.Contains("\"recommendation\":\"Widen the date range before drawing conclusions.\"", html);
        }

        [Fact]
        public void BuildHtml_SerializesFindingSeverityAsStringNotOrdinal()
        {
            ReportModel model = SampleModel();
            model.Analysis = new ReportAnalysis
            {
                Findings = new[] { new ReportFinding { Title = "Thin sample", Severity = FindingSeverity.High } }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // The page styles Findings by severity name, so the enum must reach it as its name.
            Assert.Contains("\"severity\":\"High\"", html);
        }

        [Fact]
        public void BuildHtml_SerializesFindingCategoryAsStringNotOrdinal()
        {
            ReportModel model = SampleModel();
            model.Analysis = new ReportAnalysis
            {
                Findings = new[] { new ReportFinding { Title = "Stale bars", Category = FindingCategory.DataQuality } }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // The page labels a Finding with the area of the run it concerns, so the category reaches it by name.
            Assert.Contains("\"category\":\"DataQuality\"", html);
        }

        [Fact]
        public void BuildHtml_EmbedsAnalysisProvenance()
        {
            ReportModel model = SampleModel();
            model.Analysis = new ReportAnalysis
            {
                Summary = "Thinly traded.",
                Provenance = new AnalysisProvenance
                {
                    Service = "Ollama",
                    Model = "qwen2.5:7b",
                    GeneratedAtUtc = T0,
                    PackageVersion = "1.0.42"
                }
            };

            string html = new HtmlReportWriter().BuildHtml(model);

            // A reader six months on must be able to tell what produced the Analysis, so every part of
            // the provenance reaches the page for the section's subtitle.
            Assert.Contains("\"service\":\"Ollama\"", html);
            Assert.Contains("\"model\":\"qwen2.5:7b\"", html);
            Assert.Contains("\"generatedAtUtc\":\"2024-01-02T00:00:00Z\"", html);
            Assert.Contains("\"packageVersion\":\"1.0.42\"", html);
        }

        [Fact]
        public void BuildHtml_ContainsAnalysisSectionContainer()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // The Analysis section is a static container the page's script fills from the model.
            Assert.Contains("id=\"analysis\"", html);
        }

        [Fact]
        public void BuildHtml_PlacesAnalysisSectionBetweenStatsPanelAndPriceChart()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // The critique is read after the numbers it interprets and before the chart.
            Assert.InRange(html.IndexOf("id=\"analysis\"", StringComparison.Ordinal),
                html.IndexOf("id=\"stats-panel\"", StringComparison.Ordinal),
                html.IndexOf("id=\"price-chart\"", StringComparison.Ordinal));
        }

        [Fact]
        public void BuildHtml_NullAnalysis_LeavesSectionHiddenAndInlinesNoAnalysis()
        {
            string html = new HtmlReportWriter().BuildHtml(SampleModel());

            // No Analysis reaches the page and the section ships hidden, so the report renders exactly
            // as it did before — the same pattern an absent configuration follows.
            Assert.Contains("\"analysis\":null", html);
            Assert.Contains("<section id=\"analysis\" hidden>", html);
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
