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
