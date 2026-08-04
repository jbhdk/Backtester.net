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
        /// ranked by Score (best first) together with the best one. A Parameter set whose configuration the
        /// code under test refuses (an argument rejection from the Trial factory or the backtest) becomes a
        /// Rejected trial — ranked below every scored Trial with its reason, never Best — while any other
        /// exception propagates and stops the sweep (ADR 0027). Progress is reported once per completed
        /// Trial through <paramref name="progress"/>; <paramref name="ct"/> stops the sweep and propagates
        /// cancellation.
        /// </summary>
        public async Task<OptimizationResult> RunAsync(IProgress<OptimizationProgress> progress = null, CancellationToken ct = default)
        {
            string[] fetchSymbols = BuildFetchSymbols();
            IHistoricalDataFetcher sharedFetcher = await FetchOnceAsync(fetchSymbols, ct).ConfigureAwait(false);
            // The series every Trial may read. Held as a local rather than a field so concurrent RunAsync
            // calls on one Optimizer never share it.
            HashSet<string> preFetched = new(fetchSymbols, StringComparer.Ordinal);

            IReadOnlyList<ParameterSet> parameterSets = _space.Expand();
            int total = parameterSets.Count;

            // Each evaluated Parameter set with the stats, score, and full result its backtest produced — or
            // the rejection reason when its configuration was refused — held at its Parameter-space (Expand)
            // index so the collected order is independent of which Trial finishes first — parallel results
            // then rank identically to a sequential sweep, ties included.
            (ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result, string RejectionReason)[] evaluated =
                new (ParameterSet, PerformanceStats, decimal, BacktestResult, string)[total];
            int completed = 0;

            ParallelOptions options = new() { CancellationToken = ct };
            await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (index, token) =>
            {
                ParameterSet parameters = parameterSets[index];

                try
                {
                    // A fresh Portfolio, strategy, and broker per Trial keep Trials independent; the shared fetcher
                    // is read-only, so parallel Trials over it are safe and see identical bars.
                    Portfolio portfolio = _portfolioFactory();
                    RefuseUnfetchedConversionSymbols(portfolio, preFetched);
                    (IStrategy strategy, IBrokerSimulator broker) = _trialFactory(parameters, portfolio);

                    // The shared fetcher already holds the Data-range bars (warmup lead-in included), so a plain
                    // Test-range engine warms OnStart on the full series yet loops and measures only the Test range.
                    BacktestEngine engine = new(sharedFetcher, _symbols, _testFromUtc, _testToUtc, _interval, strategy, broker, portfolio);
                    BacktestResult result = await engine.StartAsync(token).ConfigureAwait(false);

                    PerformanceStats stats = portfolio.GetPerformanceStats();
                    evaluated[index] = (parameters, stats, _objective.Score(stats), result, null);
                }
                catch (ArgumentException rejection)
                {
                    // A configuration rejection — the strategy, broker, or one of their components refused this
                    // Parameter set — becomes a Rejected trial rather than killing the sweep (ADR 0027). Only
                    // argument rejections are contained: any other exception, and cancellation, still propagates
                    // so genuine defects stay loud.
                    evaluated[index] = (parameters, null, 0m, null, rejection.Message);
                }

                int done = Interlocked.Increment(ref completed);
                progress?.Report(new OptimizationProgress(done, total));
            }).ConfigureAwait(false);

            // Rejected trials carry no Score, so only scored Trials are ranked by it; the rejected ones follow
            // below every scored Trial, in Parameter-space order, each shown with its reason (ADR 0027).
            List<(ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result, string RejectionReason)> scored =
                evaluated.Where(trial => trial.RejectionReason == null).ToList();
            List<(ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result, string RejectionReason)> ranked =
                (_objective.Direction == OptimizationDirection.Maximize
                    ? scored.OrderByDescending(trial => trial.Score)
                    : scored.OrderBy(trial => trial.Score)).ToList();

            // Best is the highest-scoring eligible Trial: ranked is score-ordered, so the first eligible one
            // wins. A higher-scoring ineligible Trial stays in the list, flagged, but never becomes Best.
            int bestIndex = ranked.FindIndex(trial => trial.Stats.Trades >= _minimumTrades);

            List<Trial> trials = new();
            for (int index = 0; index < ranked.Count; index++)
            {
                (ParameterSet parameters, PerformanceStats stats, decimal score, BacktestResult result, _) = ranked[index];
                bool eligible = stats.Trades >= _minimumTrades;
                // Retain the full result for the winning Trial (so Best.BacktestResult is populated even when
                // a higher-scoring Trial is ineligible) and for every Trial when the caller opted in.
                bool keepResult = _retainAllBacktestResults || index == bestIndex;
                trials.Add(new Trial(parameters, stats, score, eligible, keepResult ? result : null));
            }

            foreach ((ParameterSet Parameters, PerformanceStats Stats, decimal Score, BacktestResult Result, string RejectionReason) rejected
                in evaluated.Where(trial => trial.RejectionReason != null))
            {
                trials.Add(Trial.Rejected(rejected.Parameters, rejected.RejectionReason));
            }

            Trial best = bestIndex >= 0 ? trials[bestIndex] : null;
            return new OptimizationResult(trials, best);
        }

        /// <summary>
        /// Fetches every symbol the sweep needs — the tradable ones and any Conversion series
        /// (<see cref="BuildFetchSymbols"/>) — once through the supplied fetcher over the Data range, the Test
        /// range plus any warmup lead-in, and returns an in-memory fetcher over the results, so every Trial's
        /// engine reads the same warm bars without re-fetching. A Conversion series resolves its Data-range
        /// start exactly as a tradable symbol does, so a Trial reads precisely the bars an equivalent single
        /// run reads (ADR 0032). Bar-count warmup is resolved here, once per symbol, so its throw-if-short
        /// refusal fires a single time rather than per Trial (ADR 0022).
        /// </summary>
        private async Task<IHistoricalDataFetcher> FetchOnceAsync(string[] fetchSymbols, CancellationToken ct)
        {
            // Key: symbol/ticker -> the Data-range bars fetched once for that symbol, shared by every Trial.
            Dictionary<string, IReadOnlyList<Candle>> series = new();
            foreach (string symbol in fetchSymbols)
            {
                DateTime dataFromUtc = await _resolveDataStartAsync(symbol, _testFromUtc, _interval, ct).ConfigureAwait(false);
                series[symbol] = await _fetcher.FetchAsync(symbol, dataFromUtc, _testToUtc, _interval, ct).ConfigureAwait(false);
            }

            return new InMemoryHistoricalDataFetcher(series);
        }

        /// <summary>
        /// Returns the series the sweep must pre-fetch: the tradable symbols plus every Conversion symbol the
        /// caller's own Portfolio declares, deduplicated. The Optimizer builds one Portfolio from the portfolio
        /// factory purely to read that declaration, asking the same single hand-off point the Engine asks
        /// (ADR 0031) rather than taking the Instruments a second time through a constructor parameter
        /// (ADR 0032). A Portfolio declaring no conversion yields the caller's own array untouched, so a plain
        /// symbol-list sweep fetches exactly its symbols and builds no conversion machinery at all.
        /// </summary>
        private string[] BuildFetchSymbols()
        {
            IReadOnlyCollection<string> conversionSymbols = _portfolioFactory().ConversionSymbols;
            if (conversionSymbols.Count == 0)
            {
                return _symbols;
            }

            return _symbols.Concat(conversionSymbols).Distinct().ToArray();
        }

        /// <summary>
        /// Throws unless every Conversion symbol <paramref name="portfolio"/> declares was pre-fetched. The
        /// set comes from one portfolio-factory call at setup, which a <see cref="Func{Portfolio}"/> cannot
        /// promise every Trial matches, so each Trial's Portfolio is checked before its Engine runs — the
        /// refusal then names the factory rather than surfacing as a missing rate from inside a bar loop. A
        /// Portfolio declaring no conversion returns immediately, so the common path costs one count check.
        /// </summary>
        private static void RefuseUnfetchedConversionSymbols(Portfolio portfolio, HashSet<string> preFetched)
        {
            IReadOnlyCollection<string> conversionSymbols = portfolio.ConversionSymbols;
            if (conversionSymbols.Count == 0)
            {
                return;
            }

            foreach (string conversionSymbol in conversionSymbols)
            {
                if (!preFetched.Contains(conversionSymbol))
                {
                    throw new InconsistentPortfolioFactoryException(conversionSymbol);
                }
            }
        }
    }
}
