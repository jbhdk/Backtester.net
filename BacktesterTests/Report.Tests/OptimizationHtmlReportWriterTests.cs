using System;
using System.Collections.Generic;
using System.IO;
using Backtester.Report;
using Xunit;

namespace BacktesterTests.Report.Tests
{
    /// <summary>
    /// Behaviour of <see cref="OptimizationHtmlReportWriter"/>: the thin writer that renders an
    /// <see cref="OptimizationReportModel"/> into one self-contained HTML file by token replacement,
    /// with the model serialized in and the chart library inlined. Tested lightly for "renders and
    /// embeds the model" — the leaderboard's visual design is settled in the preview-approval step.
    /// </summary>
    public class OptimizationHtmlReportWriterTests
    {
        /// <summary>A representative model: two Trials over a fast/slow grid, one best and one ineligible.</summary>
        private static OptimizationReportModel SampleModel()
        {
            return new OptimizationReportModel
            {
                ParameterNames = new[] { "fast", "slow" },
                Trials = new[]
                {
                    new OptimizationTrialRow
                    {
                        Rank = 1, ParameterValues = new[] { "10", "30" },
                        Score = 1.75m, Eligible = true, IsBest = true,
                        Trades = 42, NetProfit = 1234.56m, MaxDrawdown = 0.18m,
                        Sharpe = 1.7m, Sortino = 2.1m, Calmar = 0.9m, ProfitFactor = 1.4m, WinRate = 0.55m
                    },
                    new OptimizationTrialRow
                    {
                        Rank = 2, ParameterValues = new[] { "5", "50" },
                        Score = 2.10m, Eligible = false, IsBest = false,
                        Trades = 8, NetProfit = 2000m, MaxDrawdown = 0.42m,
                        Sharpe = 2.0m, Sortino = 2.4m, Calmar = 0.5m, ProfitFactor = 1.1m, WinRate = 0.62m
                    }
                }
            };
        }

        [Fact]
        public void BuildHtml_EmbedsHeatmapSurfaceWhenPresent()
        {
            OptimizationReportModel model = SampleModel();
            model.Heatmap = new OptimizationScoreHeatmap
            {
                XParameterName = "fast",
                YParameterName = "slow",
                XValues = new[] { "10", "12" },
                YValues = new[] { "30", "50" },
                Cells = new[] { new OptimizationHeatmapCell { XIndex = 1, YIndex = 0, Score = 3.14m } }
            };

            string html = new OptimizationHtmlReportWriter().BuildHtml(model);

            // The heatmap reaches the page as inlined data (camelCase), so the chart draws offline.
            Assert.Contains("\"heatmap\":", html);
            Assert.Contains("\"xIndex\":1", html);
            Assert.Contains("\"score\":3.14", html);
        }

        [Fact]
        public void BuildHtml_EmbedsMarginalsSurfaceWhenPresent()
        {
            OptimizationReportModel model = SampleModel();
            model.Marginals = new[]
            {
                new OptimizationParameterMarginal
                {
                    ParameterName = "fast",
                    Points = new[] { new OptimizationMarginalPoint { Value = "10", MaxScore = 2.4m, MeanScore = 1.5m } }
                }
            };

            string html = new OptimizationHtmlReportWriter().BuildHtml(model);

            // The marginals reach the page as inlined data (camelCase), so the line panes draw offline.
            Assert.Contains("\"marginals\":", html);
            Assert.Contains("\"maxScore\":2.4", html);
            Assert.Contains("\"meanScore\":1.5", html);
        }

        [Fact]
        public void BuildHtml_EmbedsSerializedModel()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("\"score\":1.75", html);
        }

        [Fact]
        public void BuildHtml_EmbedsEveryTrialRow()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            // Both Trials reach the page: the eligible best and the higher-scoring ineligible one.
            Assert.Contains("\"isBest\":true", html);
            Assert.Contains("\"eligible\":false", html);
            Assert.Contains("\"parameterNames\":[\"fast\",\"slow\"]", html);
        }

        [Fact]
        public void BuildHtml_ReplacesDataToken_NoneLeftOver()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            Assert.DoesNotContain("__OPTIMIZATION_DATA__", html);
        }

        [Fact]
        public void BuildHtml_InlinesLightweightChartsLibrary()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            // The vendored standalone build's banner and the global it defines: proves the chart library
            // is inlined (offline, no CDN) rather than referenced externally, and the token is consumed.
            Assert.Contains("Lightweight Charts", html);
            Assert.Contains("LightweightCharts", html);
            Assert.DoesNotContain("__CHART_LIB__", html);
        }

        [Fact]
        public void BuildHtml_ContainsLeaderboardContainer()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("id=\"leaderboard\"", html);
        }

        [Fact]
        public void BuildHtml_SerializesModelAsCamelCase()
        {
            string html = new OptimizationHtmlReportWriter().BuildHtml(SampleModel());

            Assert.Contains("\"parameterValues\"", html);
            Assert.Contains("\"netProfit\"", html);
            Assert.Contains("\"maxDrawdown\"", html);
        }

        [Fact]
        public void Write_ProducesFileWithSameContentAsBuildHtml()
        {
            OptimizationHtmlReportWriter writer = new();
            OptimizationReportModel model = SampleModel();
            string path = Path.Combine(Path.GetTempPath(), "bt_opt_report_test", Guid.NewGuid() + ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            writer.Write(model, path);

            Assert.True(File.Exists(path));
            Assert.Equal(writer.BuildHtml(model), File.ReadAllText(path));
        }
    }
}
