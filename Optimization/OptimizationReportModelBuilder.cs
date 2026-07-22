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

            // Only Parameters that actually take more than one value shape the Score surface. Exactly two
            // varying axes render as a heatmap; more render as per-Parameter marginals; fewer render neither.
            IReadOnlyList<string> varying = VaryingParameterNames(result.Trials, parameterNames);

            return new OptimizationReportModel
            {
                ParameterNames = parameterNames,
                Trials = rows,
                Heatmap = varying.Count == 2 ? BuildHeatmap(result.Trials, varying[0], varying[1]) : null,
                Marginals = varying.Count > 2 ? BuildMarginals(result.Trials, varying) : new List<OptimizationParameterMarginal>()
            };
        }

        /// <summary>
        /// The swept Parameter names that take more than one distinct value across the Trials, in axis order.
        /// A swept axis pinned to a single value does not vary and does not shape the Score surface.
        /// </summary>
        private static IReadOnlyList<string> VaryingParameterNames(IReadOnlyList<Trial> trials, IReadOnlyList<string> names)
        {
            List<string> varying = new();
            foreach (string name in names)
            {
                // The distinct boxed values this axis took across every Trial.
                HashSet<object> distinct = new();
                foreach (Trial trial in trials)
                {
                    distinct.Add(trial.Parameters.Values[name]);
                }

                if (distinct.Count > 1)
                {
                    varying.Add(name);
                }
            }

            return varying;
        }

        /// <summary>
        /// The Score heatmap over the two varying axes: each Trial becomes one cell addressed by its position
        /// on the ascending, distinct X and Y axes.
        /// </summary>
        private static OptimizationScoreHeatmap BuildHeatmap(IReadOnlyList<Trial> trials, string xName, string yName)
        {
            List<object> xValues = OrderedDistinctValues(trials, xName);
            List<object> yValues = OrderedDistinctValues(trials, yName);

            List<OptimizationHeatmapCell> cells = new(trials.Count);
            foreach (Trial trial in trials)
            {
                cells.Add(new OptimizationHeatmapCell
                {
                    XIndex = xValues.IndexOf(trial.Parameters.Values[xName]),
                    YIndex = yValues.IndexOf(trial.Parameters.Values[yName]),
                    Score = trial.Score
                });
            }

            return new OptimizationScoreHeatmap
            {
                XParameterName = xName,
                YParameterName = yName,
                XValues = DisplayValues(xValues),
                YValues = DisplayValues(yValues),
                Cells = cells
            };
        }

        /// <summary>
        /// The distinct values a named axis took across the Trials, sorted ascending. Values on one axis
        /// share a type (int, decimal, or bool), so their natural <see cref="IComparable"/> order applies.
        /// </summary>
        private static List<object> OrderedDistinctValues(IReadOnlyList<Trial> trials, string name)
        {
            List<object> distinct = new();
            foreach (Trial trial in trials)
            {
                object value = trial.Parameters.Values[name];
                if (!distinct.Contains(value))
                {
                    distinct.Add(value);
                }
            }

            distinct.Sort((left, right) => Comparer<object>.Default.Compare(left, right));
            return distinct;
        }

        /// <summary>
        /// One marginal per varying Parameter, in axis order — the higher-dimensional alternative to a
        /// heatmap when more than two axes vary.
        /// </summary>
        private static List<OptimizationParameterMarginal> BuildMarginals(IReadOnlyList<Trial> trials, IReadOnlyList<string> varying)
        {
            List<OptimizationParameterMarginal> marginals = new(varying.Count);
            foreach (string name in varying)
            {
                marginals.Add(BuildMarginal(trials, name));
            }

            return marginals;
        }

        /// <summary>
        /// A single Parameter's marginal: for each distinct value (ascending), the best and mean Score over
        /// the Trials that share it.
        /// </summary>
        private static OptimizationParameterMarginal BuildMarginal(IReadOnlyList<Trial> trials, string name)
        {
            List<OptimizationMarginalPoint> points = new();
            foreach (object value in OrderedDistinctValues(trials, name))
            {
                decimal max = decimal.MinValue;
                decimal sum = 0m;
                int count = 0;
                foreach (Trial trial in trials)
                {
                    if (Equals(trial.Parameters.Values[name], value))
                    {
                        max = trial.Score > max ? trial.Score : max;
                        sum += trial.Score;
                        count++;
                    }
                }

                points.Add(new OptimizationMarginalPoint
                {
                    Value = Display(value),
                    MaxScore = max,
                    MeanScore = sum / count
                });
            }

            return new OptimizationParameterMarginal
            {
                ParameterName = name,
                Points = points
            };
        }

        /// <summary>The given boxed axis values rendered as invariant display strings, in the same order.</summary>
        private static IReadOnlyList<string> DisplayValues(IReadOnlyList<object> values)
        {
            List<string> display = new(values.Count);
            foreach (object value in values)
            {
                display.Add(Display(value));
            }

            return display;
        }

        /// <summary>
        /// A boxed Parameter value as an invariant display string, so int, decimal, and bool axes each render
        /// faithfully and identically wherever the report shows a Parameter value.
        /// </summary>
        private static string Display(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
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
                values.Add(Display(parameters.Values[name]));
            }

            return values;
        }
    }
}
