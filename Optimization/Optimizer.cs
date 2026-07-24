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
        private readonly DateTime _testFromUtc;
        private readonly DateTime _testToUtc;
        // Resolves one symbol's Data-range start (how far the shared fetch reaches ahead of the Test range) for
        // the run's chosen warmup form: (symbol, testFrom, interval, ct) -> Data.Start. Bar-count warmup is
        // resolved through this once, in FetchOnceAsync, rather than per Trial.
        private readonly Func<string, DateTime, string, CancellationToken, Task<DateTime>> _resolveDataStartAsync;
        private readonly string _interval;
        private readonly Func<Portfolio> _portfolioFactory;
        private readonly ParameterSpace _space;
        private readonly Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> _trialFactory;
        private readonly bool _retainAllBacktestResults;
        private readonly Objective _objective;
        private readonly int _minimumTrades;

        /// <summary>
        /// Initializes a new Optimizer over a Test range with no warmup (the Data range equals the Test range,
        /// ADR 0022), the Parameter space to sweep, and the Trial factory that builds a fresh strategy and
        /// broker for each Parameter set. A fresh <see cref="Portfolio"/> is produced per Trial from
        /// <paramref name="portfolioFactory"/>. Trials are ranked by <paramref name="objective"/>; when it is
        /// null the default is maximise Sharpe. A Trial with fewer Round trips than
        /// <paramref name="minimumTrades"/> is flagged ineligible and can never be
        /// <see cref="OptimizationResult.Best"/>, guarding against a degenerate low-trade Trial winning on a
        /// lucky Score.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(fetcher, symbols, testFrom, testTo, NoWarmupResolver(), interval, portfolioFactory, space, trialFactory, retainAllBacktestResults, objective, minimumTrades)
        {
        }

        /// <summary>
        /// Initializes a new Optimizer over a Test range with a period (<see cref="TimeSpan"/>) warmup lead-in
        /// (ADR 0022): the shared fetch reaches back <paramref name="warmup"/> before <paramref name="testFrom"/>,
        /// so every Trial's <c>OnStart</c> receives the full Data-range history while each Trial is measured
        /// over the Test range.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            TimeSpan warmup,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(fetcher, symbols, testFrom, testTo, PeriodResolver(warmup), interval, portfolioFactory, space, trialFactory, retainAllBacktestResults, objective, minimumTrades)
        {
        }

        /// <summary>
        /// Initializes a new Optimizer over a Test range with an absolute-date warmup lead-in (ADR 0022): the
        /// shared Data range starts exactly at <paramref name="warmupStart"/>, guarded to be on or before
        /// <paramref name="testFrom"/>.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            DateTime warmupStart,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(fetcher, symbols, testFrom, testTo, AbsoluteResolver(warmupStart, testFrom), interval, portfolioFactory, space, trialFactory, retainAllBacktestResults, objective, minimumTrades)
        {
        }

        /// <summary>
        /// Initializes a new Optimizer over a Test range with a bar-count warmup lead-in (ADR 0022): the shared
        /// Data range reaches back exactly <paramref name="warmupBars"/> bars before <paramref name="testFrom"/>,
        /// resolved per symbol once in the fetch-once step through the warmup-capable <paramref name="fetcher"/>.
        /// A symbol lacking that many bars above its Coverage floor is refused once, there, rather than per Trial.
        /// </summary>
        public Optimizer(
            IWarmupResolvingFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            int warmupBars,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(fetcher, symbols, testFrom, testTo, BarCountResolver(fetcher, warmupBars), interval, portfolioFactory, space, trialFactory, retainAllBacktestResults, objective, minimumTrades)
        {
        }

        /// <summary>
        /// Initializes a new Optimizer from an <see cref="OptimizationSetup"/> — the Parameter space and
        /// Trial factory an authoring path (e.g. <see cref="Optimize.For{TParameters}"/>) already built
        /// together — over a Test range with no warmup.
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            string interval,
            Func<Portfolio> portfolioFactory,
            OptimizationSetup setup,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(
                fetcher,
                symbols,
                testFrom,
                testTo,
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
        /// Initializes a new Optimizer from an <see cref="OptimizationSetup"/> over a Test range with a period
        /// (<see cref="TimeSpan"/>) warmup lead-in (ADR 0022).
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            TimeSpan warmup,
            string interval,
            Func<Portfolio> portfolioFactory,
            OptimizationSetup setup,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(
                fetcher,
                symbols,
                testFrom,
                testTo,
                warmup,
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
        /// Initializes a new Optimizer from an <see cref="OptimizationSetup"/> over a Test range with an
        /// absolute-date warmup lead-in (ADR 0022).
        /// </summary>
        public Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            DateTime warmupStart,
            string interval,
            Func<Portfolio> portfolioFactory,
            OptimizationSetup setup,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(
                fetcher,
                symbols,
                testFrom,
                testTo,
                warmupStart,
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
        /// Initializes a new Optimizer from an <see cref="OptimizationSetup"/> over a Test range with a
        /// bar-count warmup lead-in (ADR 0022), resolved once in the fetch-once step.
        /// </summary>
        public Optimizer(
            IWarmupResolvingFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            int warmupBars,
            string interval,
            Func<Portfolio> portfolioFactory,
            OptimizationSetup setup,
            bool retainAllBacktestResults = false,
            Objective objective = null,
            int minimumTrades = 30)
            : this(
                fetcher,
                symbols,
                testFrom,
                testTo,
                warmupBars,
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
        /// The private core all overloads delegate to, holding the resolved warmup as a per-symbol
        /// Data-range-start resolver so a future warmup form is a new resolver rather than a branch here.
        /// </summary>
        private Optimizer(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            Func<string, DateTime, string, CancellationToken, Task<DateTime>> resolveDataStartAsync,
            string interval,
            Func<Portfolio> portfolioFactory,
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory,
            bool retainAllBacktestResults,
            Objective objective,
            int minimumTrades)
        {
            _fetcher = fetcher;
            _symbols = symbols;
            _testFromUtc = testFrom;
            _testToUtc = testTo;
            _resolveDataStartAsync = resolveDataStartAsync;
            _interval = interval;
            _portfolioFactory = portfolioFactory;
            _space = space;
            _trialFactory = trialFactory;
            _retainAllBacktestResults = retainAllBacktestResults;
            _objective = objective ?? Objectives.Sharpe;
            _minimumTrades = minimumTrades;
        }

        /// <summary>Resolver for no warmup: the Data range starts at the Test range start.</summary>
        private static Func<string, DateTime, string, CancellationToken, Task<DateTime>> NoWarmupResolver()
        {
            return (symbol, testFrom, interval, ct) => Task.FromResult(testFrom);
        }

        /// <summary>Resolver for a period warmup: the Data range starts <paramref name="warmup"/> before the Test start.</summary>
        private static Func<string, DateTime, string, CancellationToken, Task<DateTime>> PeriodResolver(TimeSpan warmup)
        {
            return (symbol, testFrom, interval, ct) => Task.FromResult(testFrom - warmup);
        }

        /// <summary>
        /// Resolver for an absolute-date warmup: the Data range starts at <paramref name="warmupStart"/>,
        /// rejected eagerly if it is later than <paramref name="testFrom"/> since the lead-in may only reach back.
        /// </summary>
        private static Func<string, DateTime, string, CancellationToken, Task<DateTime>> AbsoluteResolver(DateTime warmupStart, DateTime testFrom)
        {
            if (warmupStart > testFrom)
            {
                throw new ArgumentOutOfRangeException(nameof(warmupStart), warmupStart, "Warmup start must be on or before the Test range start.");
            }

            return (symbol, tf, interval, ct) => Task.FromResult(warmupStart);
        }

        /// <summary>
        /// Resolver for a bar-count warmup: delegates to the fetcher seam to resolve "N bars before the Test
        /// start" per symbol. The count must be positive.
        /// </summary>
        private static Func<string, DateTime, string, CancellationToken, Task<DateTime>> BarCountResolver(IWarmupResolvingFetcher fetcher, int warmupBars)
        {
            if (warmupBars <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "Warmup bar count must be positive.");
            }

            return (symbol, testFrom, interval, ct) => fetcher.ResolveWarmupStartAsync(symbol, testFrom, warmupBars, interval, ct);
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

                // The shared fetcher already holds the Data-range bars (warmup lead-in included), so a plain
                // Test-range engine warms OnStart on the full series yet loops and measures only the Test range.
                BacktestEngine engine = new(sharedFetcher, _symbols, _testFromUtc, _testToUtc, _interval, strategy, broker, portfolio);
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
        /// Fetches every symbol once through the supplied fetcher over the Data range — the Test range plus any
        /// warmup lead-in — and returns an in-memory fetcher over the results, so every Trial's engine reads the
        /// same warm bars without re-fetching. Bar-count warmup is resolved here, once per symbol, so its
        /// throw-if-short refusal fires a single time rather than per Trial (ADR 0022).
        /// </summary>
        private async Task<IHistoricalDataFetcher> FetchOnceAsync(CancellationToken ct)
        {
            // Key: symbol/ticker -> the Data-range bars fetched once for that symbol, shared by every Trial.
            Dictionary<string, IReadOnlyList<Candle>> series = new();
            foreach (string symbol in _symbols)
            {
                DateTime dataFromUtc = await _resolveDataStartAsync(symbol, _testFromUtc, _interval, ct).ConfigureAwait(false);
                series[symbol] = await _fetcher.FetchAsync(symbol, dataFromUtc, _testToUtc, _interval, ct).ConfigureAwait(false);
            }

            return new InMemoryHistoricalDataFetcher(series);
        }
    }
}
