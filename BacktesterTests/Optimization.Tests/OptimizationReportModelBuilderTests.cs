using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Backtester.Optimization;
using Backtester.Report;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of <see cref="OptimizationReportModelBuilder"/>: a pure projection of an
    /// <see cref="OptimizationResult"/> (the ranked Trials and the best one) into the serializable
    /// <see cref="OptimizationReportModel"/> the leaderboard renders.
    /// </summary>
    public class OptimizationReportModelBuilderTests
    {
        /// <summary>A Parameter set over the given name→value assignment.</summary>
        private static ParameterSet Params(params (string Name, object Value)[] values)
        {
            // Key: Parameter name -> the value chosen for this point in the space.
            Dictionary<string, object> assignment = new();
            foreach ((string name, object value) in values)
            {
                assignment[name] = value;
            }

            return new ParameterSet(assignment);
        }

        /// <summary>A Trial with the given score; parameters, stats, and eligibility default to neutral values.</summary>
        private static Trial Trial(decimal score, bool eligible = true, PerformanceStats stats = null, ParameterSet parameters = null)
        {
            return new Trial(parameters ?? Params(), stats ?? new PerformanceStats(), score, eligible, null);
        }

        [Fact]
        public void Build_ProjectsEachTrialToARowCarryingItsScore()
        {
            Trial trial = Trial(score: 1.5m);
            OptimizationResult result = new(new[] { trial }, trial);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            OptimizationTrialRow row = Assert.Single(model.Trials);
            Assert.Equal(1.5m, row.Score);
        }

        [Fact]
        public void Build_PreservesRankedOrderWithOneBasedRank()
        {
            // The Optimizer hands Trials already ranked best-first; the builder must not reorder them.
            Trial best = Trial(score: 2.0m);
            Trial runnerUp = Trial(score: 1.0m);
            OptimizationResult result = new(new[] { best, runnerUp }, best);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Equal(new[] { 1, 2 }, model.Trials.Select(row => row.Rank));
            Assert.Equal(new[] { 2.0m, 1.0m }, model.Trials.Select(row => row.Score));
        }

        [Fact]
        public void Build_FlagsTheBestTrialsRowAndNoOther()
        {
            Trial best = Trial(score: 2.0m);
            Trial other = Trial(score: 1.0m);
            OptimizationResult result = new(new[] { best, other }, best);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            // The best row is the one the page highlights; every other row is a plain competitor.
            Assert.True(model.Trials[0].IsBest);
            Assert.False(model.Trials[1].IsBest);
        }

        [Fact]
        public void Build_CarriesEachTrialsEligibility()
        {
            // The higher-scoring Trial is ineligible (too few Round trips), so it is ranked first but the
            // eligible runner-up is the winner — the page flags the ineligible row.
            Trial ineligible = Trial(score: 3.0m, eligible: false);
            Trial eligible = Trial(score: 2.0m, eligible: true);
            OptimizationResult result = new(new[] { ineligible, eligible }, eligible);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.False(model.Trials[0].Eligible);
            Assert.True(model.Trials[1].Eligible);
        }

        [Fact]
        public void Build_MapsTheCompactRiskAdjustedMetricsFromStats()
        {
            PerformanceStats stats = new()
            {
                Trades = 42,
                NetProfit = 1234.56m,
                MaxDrawdown = 0.18m,
                Sharpe = 1.7m,
                Sortino = 2.1m,
                Calmar = 0.9m,
                ProfitFactor = 1.4m,
                WinRate = 0.55m
            };
            Trial trial = Trial(score: 1m, stats: stats);
            OptimizationResult result = new(new[] { trial }, trial);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            OptimizationTrialRow row = Assert.Single(model.Trials);
            Assert.Equal(42, row.Trades);
            Assert.Equal(1234.56m, row.NetProfit);
            Assert.Equal(0.18m, row.MaxDrawdown);
            Assert.Equal(1.7m, row.Sharpe);
            Assert.Equal(2.1m, row.Sortino);
            Assert.Equal(0.9m, row.Calmar);
            Assert.Equal(1.4m, row.ProfitFactor);
            Assert.Equal(0.55m, row.WinRate);
        }

        [Fact]
        public void Build_ProjectsSharedParameterNamesWithPerRowValuesAligned()
        {
            // Every Trial in a sweep shares the same axes; the model carries the names once as the column
            // headers, and each row carries its values in that same column order.
            Trial first = Trial(score: 2m, parameters: Params(("fast", 10), ("slow", 30m)));
            Trial second = Trial(score: 1m, parameters: Params(("fast", 12), ("slow", 26m)));
            OptimizationResult result = new(new[] { first, second }, first);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Equal(new[] { "fast", "slow" }, model.ParameterNames);
            Assert.Equal(new[] { "10", "30" }, model.Trials[0].ParameterValues);
            Assert.Equal(new[] { "12", "26" }, model.Trials[1].ParameterValues);
        }

        [Fact]
        public void Build_SweepWithNoVariedParameters_YieldsEmptyNamesAndEmptyRowValues()
        {
            // A grid with no axes still evaluates a single (empty) Parameter set; the leaderboard then has
            // no Parameter columns and its one row has no Parameter cells — never a null collection.
            Trial only = Trial(score: 1m, parameters: Params());
            OptimizationResult result = new(new[] { only }, only);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Empty(model.ParameterNames);
            Assert.Empty(Assert.Single(model.Trials).ParameterValues);
        }

        [Fact]
        public void Build_TwoVaryingParameters_BuildsAScoreHeatmapWithACellPerTrial()
        {
            // A full 2x2 grid over fast x slow: each (fast, slow) point is one Trial carrying its Score, so
            // the heatmap has one cell per Trial.
            Trial t1 = Trial(score: 1.0m, parameters: Params(("fast", 10), ("slow", 30)));
            Trial t2 = Trial(score: 2.0m, parameters: Params(("fast", 10), ("slow", 50)));
            Trial t3 = Trial(score: 3.0m, parameters: Params(("fast", 12), ("slow", 30)));
            Trial t4 = Trial(score: 4.0m, parameters: Params(("fast", 12), ("slow", 50)));
            OptimizationResult result = new(new[] { t4, t3, t2, t1 }, t4);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.NotNull(model.Heatmap);
            Assert.Equal(4, model.Heatmap.Cells.Count);
        }

        [Fact]
        public void Build_Heatmap_ExposesAscendingDistinctAxesAndCellsIndexIntoThem()
        {
            // Trials arrive best-first, not in grid order; the heatmap axes are still the distinct values in
            // ascending order, and each cell addresses its grid point by index into those axes.
            Trial topLeft = Trial(score: 4.0m, parameters: Params(("fast", 12), ("slow", 30)));
            Trial rest1 = Trial(score: 3.0m, parameters: Params(("fast", 5), ("slow", 50)));
            Trial rest2 = Trial(score: 2.0m, parameters: Params(("fast", 5), ("slow", 30)));
            Trial rest3 = Trial(score: 1.0m, parameters: Params(("fast", 12), ("slow", 50)));
            OptimizationResult result = new(new[] { topLeft, rest1, rest2, rest3 }, topLeft);

            OptimizationScoreHeatmap heatmap = new OptimizationReportModelBuilder().Build(result).Heatmap;

            Assert.Equal("fast", heatmap.XParameterName);
            Assert.Equal("slow", heatmap.YParameterName);
            Assert.Equal(new[] { "5", "12" }, heatmap.XValues);
            Assert.Equal(new[] { "30", "50" }, heatmap.YValues);
            // The (fast=12, slow=30) Trial scored 4.0: column index 1 (12 is the second X value), row index 0.
            OptimizationHeatmapCell cell = Assert.Single(heatmap.Cells, candidate => candidate.Score == 4.0m);
            Assert.Equal(1, cell.XIndex);
            Assert.Equal(0, cell.YIndex);
        }

        [Fact]
        public void Build_MoreThanTwoVaryingParameters_BuildsPerParameterMarginalsAndNoHeatmap()
        {
            // Three varying axes cannot be a single heatmap, so the report profiles each Parameter on its
            // own: one marginal per varying axis, in axis order, and no heatmap.
            List<Trial> trials = new();
            foreach (int fast in new[] { 10, 12 })
            {
                foreach (int slow in new[] { 30, 50 })
                {
                    foreach (int stop in new[] { 1, 2 })
                    {
                        trials.Add(Trial(score: fast + slow + stop, parameters: Params(("fast", fast), ("slow", slow), ("stop", stop))));
                    }
                }
            }
            OptimizationResult result = new(trials, trials[0]);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Null(model.Heatmap);
            Assert.Equal(new[] { "fast", "slow", "stop" }, model.Marginals.Select(marginal => marginal.ParameterName));
        }

        [Fact]
        public void Build_Marginal_SummarisesEachValueWithBestAndMeanScoreOverSharingTrials()
        {
            // Three varying axes trigger marginals. The four Trials at fast=10 score 1..4; at fast=12 they
            // score 5..8. The fast marginal must summarise each value with the best and mean over its Trials.
            decimal[,,] scores =
            {
                { { 1m, 2m }, { 3m, 4m } }, // fast=10: slow=30 -> {stop1,stop2}, slow=50 -> {stop1,stop2}
                { { 5m, 6m }, { 7m, 8m } }  // fast=12
            };
            List<Trial> trials = new();
            int[] fasts = { 10, 12 };
            int[] slows = { 30, 50 };
            int[] stops = { 1, 2 };
            for (int fastIndex = 0; fastIndex < fasts.Length; fastIndex++)
            {
                for (int slowIndex = 0; slowIndex < slows.Length; slowIndex++)
                {
                    for (int stopIndex = 0; stopIndex < stops.Length; stopIndex++)
                    {
                        trials.Add(Trial(
                            score: scores[fastIndex, slowIndex, stopIndex],
                            parameters: Params(("fast", fasts[fastIndex]), ("slow", slows[slowIndex]), ("stop", stops[stopIndex]))));
                    }
                }
            }
            OptimizationResult result = new(trials, trials[0]);

            OptimizationParameterMarginal fast = new OptimizationReportModelBuilder().Build(result)
                .Marginals.Single(marginal => marginal.ParameterName == "fast");

            Assert.Equal(new[] { "10", "12" }, fast.Points.Select(point => point.Value));
            Assert.Equal(4m, fast.Points[0].MaxScore);
            Assert.Equal(2.5m, fast.Points[0].MeanScore);
            Assert.Equal(8m, fast.Points[1].MaxScore);
            Assert.Equal(6.5m, fast.Points[1].MeanScore);
        }

        [Fact]
        public void Build_OneVaryingParameter_YieldsNeitherHeatmapNorMarginals()
        {
            // A single axis is just the leaderboard's own ordering; there is no Score surface to chart.
            Trial low = Trial(score: 1m, parameters: Params(("fast", 10)));
            Trial high = Trial(score: 2m, parameters: Params(("fast", 12)));
            OptimizationResult result = new(new[] { high, low }, high);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Null(model.Heatmap);
            Assert.Empty(model.Marginals);
        }

        [Fact]
        public void Build_PinnedAxisDoesNotCountAsVarying_TwoSweptButOneFixedYieldsNoHeatmap()
        {
            // Two Parameters are swept, but "slow" took a single value across every Trial, so only one axis
            // truly varies — not a heatmap. This guards the "varies = more than one distinct value" rule.
            Trial a = Trial(score: 1m, parameters: Params(("fast", 10), ("slow", 30)));
            Trial b = Trial(score: 2m, parameters: Params(("fast", 12), ("slow", 30)));
            OptimizationResult result = new(new[] { b, a }, b);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);

            Assert.Null(model.Heatmap);
            Assert.Empty(model.Marginals);
        }

        [Fact]
        public void Build_Model_SerializesWithSystemTextJson()
        {
            Trial trial = Trial(score: 1.5m, parameters: Params(("fast", 10)));
            OptimizationResult result = new(new[] { trial }, trial);

            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);
            string json = System.Text.Json.JsonSerializer.Serialize(model);

            Assert.Contains("\"ParameterNames\"", json);
            Assert.Contains("\"Trials\"", json);
        }
    }
}
