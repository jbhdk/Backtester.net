using System;
using System.Collections.Generic;
using System.Globalization;
using Backtester.Core;
using Backtester.Report;

namespace Backtester.Optimization
{
    /// <summary>
    /// Builds the serializable <see cref="OptimizationReportModel"/> from an <see cref="OptimizationResult"/>.
    /// A pure function: it performs no I/O and derives every leaderboard value from the ranked Trials and
    /// the best one alone. The report-rendered types live in the Report project, so this builder is the
    /// only seam that sees both the Optimization domain and the report model.
    /// </summary>
    public class OptimizationReportModelBuilder
    {
        /// <summary>
        /// Projects the ranked Trials of <paramref name="result"/> into a leaderboard model, preserving the
        /// result's best-first ranking.
        /// </summary>
        public OptimizationReportModel Build(OptimizationResult result)
        {
            IReadOnlyList<string> parameterNames = ParameterNames(result.Trials);

            List<OptimizationTrialRow> rows = new(result.Trials.Count);
            for (int index = 0; index < result.Trials.Count; index++)
            {
                Trial trial = result.Trials[index];
                PerformanceStats stats = trial.Stats;
                rows.Add(new OptimizationTrialRow
                {
                    Rank = index + 1,
                    ParameterValues = ParameterValues(trial.Parameters, parameterNames),
                    Score = trial.Score,
                    Eligible = trial.Eligible,
                    // Best is identified by reference: it is one of the ranked Trials (or null when no Trial
                    // is eligible), so exactly the winning row is flagged.
                    IsBest = ReferenceEquals(trial, result.Best),
                    // The compact risk-adjusted set the leaderboard compares Trials by. Net profit is carried
                    // in currency (not as a percentage): every Trial in a sweep shares one capital base, so
                    // currency ranks identically to a percentage and needs no starting-equity input here.
                    Trades = stats.Trades,
                    NetProfit = stats.NetProfit,
                    MaxDrawdown = stats.MaxDrawdown,
                    Sharpe = stats.Sharpe,
                    Sortino = stats.Sortino,
                    Calmar = stats.Calmar,
                    ProfitFactor = stats.ProfitFactor,
                    WinRate = stats.WinRate
                });
            }

            return new OptimizationReportModel
            {
                ParameterNames = parameterNames,
                Trials = rows
            };
        }

        /// <summary>
        /// The swept Parameter names, in axis order, taken from the first Trial — every Trial in a sweep
        /// shares the same axes, so one Trial's names are the shared column headers. Empty when there are no
        /// Trials or the sweep varied no Parameters.
        /// </summary>
        private static IReadOnlyList<string> ParameterNames(IReadOnlyList<Trial> trials)
        {
            List<string> names = new();
            if (trials.Count > 0)
            {
                foreach (string name in trials[0].Parameters.Values.Keys)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        /// <summary>
        /// One Trial's Parameter values as display strings, read back in the shared column order and
        /// formatted invariantly so int, decimal, and bool axes each render faithfully.
        /// </summary>
        private static IReadOnlyList<string> ParameterValues(ParameterSet parameters, IReadOnlyList<string> names)
        {
            List<string> values = new(names.Count);
            foreach (string name in names)
            {
                values.Add(Convert.ToString(parameters.Values[name], CultureInfo.InvariantCulture));
            }

            return values;
        }
    }
}
