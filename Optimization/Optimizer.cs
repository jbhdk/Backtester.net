using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using Backtester.Strategies;
using BacktestEngine = Backtester.Engine.Engine;

namespace Backtester.Optimization
{
    /// <summary>
    /// Runs an Optimization: expands a <see cref="ParameterSpace"/> into a grid, runs one backtest per
    /// Parameter set (a Trial) through the existing engine over data fetched once and shared across Trials,
    /// scores each Trial, and ranks them. The grid is run exhaustively and in parallel, reporting progress
    /// per completed Trial and honouring a <see cref="CancellationToken"/>; results are collected in
    /// Parameter-space order so a parallel sweep ranks identically to a sequential one.
    /// </summary>
    public class Optimizer
    {
        private readonly IHistoricalDataFetcher _fetcher;
        private readonly string[] _symbols;
        private readonly DateTime _fromUtc;
        private readonly DateTime _toUtc;
        private readonly string _interval;
        private readonly Func<Portfolio> _portfolioFactory;
        private readonly ParameterSpace _space;
        private readonly Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> _trialFactory;
        private readonly bool _retainAllBacktestResults;
        private readonly Objective _objective;
        private readonly int _minimumTrades;

        /// <summary>
        /// Initializes a new Optimizer over the shared run inputs, the Parameter space to sweep, and the
        /// Trial factory that builds a fresh strategy and broker for each Parameter set. A fresh
        /// <see cref="Portfolio"/> is produced per Trial from <paramref name="portfolioFactory"/>. Trials are
        /// ranked by <paramref name="objective"/>; when it is null the default is maximise Sharpe. A Trial
        /// with fewer Round trips than <paramref name="minimumTrades"/> is flagged ineligible and can never be
        /// <see cref="OptimizationResult.Best"/>, guarding against a degenerate low-trade Trial winning on a
        /// lucky Score.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime fromUtc,
            DateTime toUtc,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
        {
            _fetcher = fetcher;
            _symbols = symbols;
            _fromUtc = fromUtc;
            _toUtc = toUtc;
            _interval = interval;
            _portfolioFactory = portfolioFactory;
            _space = space;
            _trialFactory = trialFactory;
            _retainAllBacktestResults = retainAllBacktestResults;
            _objective = objective ?? Objectives.Sharpe;
            _minimumTrades = minimumTrades;
        }

        /// <summary>
        /// Initializes a new Optimizer from an <see cref="OptimizationSetup"/> — the Parameter space and
        /// Trial factory an authoring path (e.g. <see cref="Optimize.For{TParameters}"/>) already built
        /// together — over the same shared run inputs as the primary constructor.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime fromUtc,
            DateTime toUtc,
            string interval,
            Func<Portfolio> portfolioFactory,
            OptimizationSetup setup,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(
                fetcher,
                symbols,
                fromUtc,
                toUtc,
                interval,
                portfolioFactory,
                (setup ?? throw new ArgumentNullException(nameof(setup))).Space,
                setup.TrialFactory,
                retainAllBacktestResults,
                objective,
                minimumTrades)
        {
        }

        /// <summary>
        /// Fetches the bars once, evaluates every Parameter set as a Trial in parallel, and returns the Trials
        /// ranked by Score (best first) together with the best one. Progress is reported once per completed
        /// Trial through <paramref name="progress"/>; <paramref name="ct"/> stops the sweep and propagates
        /// cancellation.
        /// </summary>
        public async Task<OptimizationResult> RunAsync(IProgress<OptimizationProgress> progress = null, CancellationToken ct = default)
        {
            IHistoricalDataFetcher sharedFetcher = await FetchOnceAsync(ct).ConfigureAwait(false);

            IReadOnlyList<ParameterSet> parameterSets = _space.Expand();
            int total = parameterSets.Count;

            // Each evaluated Parameter set with the stats, score, and full result its backtest produced, held
            // at its Parameter-space (Expand) index so the collected order is independent of which Trial
            // finishes first — parallel results then rank identically to a sequential sweep, ties included.
            (ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result)[] evaluated =
                new (ParameterSet, PerformanceStats, decimal, BacktestResult)[total];
            int completed = 0;

            ParallelOptions options = new() { CancellationToken = ct };
            await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (index, token) =>
            {
                ParameterSet parameters = parameterSets[index];

                // A fresh Portfolio, strategy, and broker per Trial keep Trials independent; the shared fetcher
                // is read-only, so parallel Trials over it are safe and see identical bars.
                Portfolio portfolio = _portfolioFactory();
                (IStrategy strategy, IBrokerSimulator broker) = _trialFactory(parameters, portfolio);

                BacktestEngine engine = new(sharedFetcher, _symbols, _fromUtc, _toUtc, _interval, strategy, broker, portfolio);
                BacktestResult result = await engine.StartAsync(token).ConfigureAwait(false);

                PerformanceStats stats = portfolio.GetPerformanceStats();
                evaluated[index] = (parameters, stats, _objective.Score(stats), result);

                int done = Interlocked.Increment(ref completed);
                progress?.Report(new OptimizationProgress(done, total));
            }).ConfigureAwait(false);

            List<(ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result)> ranked =
                (_objective.Direction == OptimizationDirection.Maximize
                    ? evaluated.OrderByDescending(trial => trial.Score)
                    : evaluated.OrderBy(trial => trial.Score)).ToList();

            // Best is the highest-scoring eligible Trial: ranked is score-ordered, so the first eligible one
            // wins. A higher-scoring ineligible Trial stays in the list, flagged, but never becomes Best.
            int bestIndex = ranked.FindIndex(trial => trial.Stats.Trades >= _minimumTrades);

            List<Trial> trials = new();
            for (int index = 0; index < ranked.Count; index++)
            {
                (ParameterSet parameters, PerformanceStats stats, decimal score, BacktestResult result) = ranked[index];
                bool eligible = stats.Trades >= _minimumTrades;
                // Retain the full result for the winning Trial (so Best.BacktestResult is populated even when
                // a higher-scoring Trial is ineligible) and for every Trial when the caller opted in.
                bool keepResult = _retainAllBacktestResults || index == bestIndex;
                trials.Add(new Trial(parameters, stats, score, eligible, keepResult ? result : null));
            }

            Trial best = bestIndex >= 0 ? trials[bestIndex] : null;
            return new OptimizationResult(trials, best);
        }

        /// <summary>
        /// Fetches every symbol once through the supplied fetcher and returns an in-memory fetcher over the
        /// results, so every Trial's engine reads the same bars without re-fetching.
        /// </summary>
        private async Task<IHistoricalDataFetcher> FetchOnceAsync(CancellationToken ct)
        {
            // Key: symbol/ticker -> the bars fetched once for that symbol, shared by every Trial.
            Dictionary<string, IReadOnlyList<Candle>> series = new();
            foreach (string symbol in _symbols)
            {
                series[symbol] = await _fetcher.FetchAsync(symbol, _fromUtc, _toUtc, _interval, ct).ConfigureAwait(false);
            }

            return new InMemoryHistoricalDataFetcher(series);
        }
    }
}
