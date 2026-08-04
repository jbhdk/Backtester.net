using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Strategies;

namespace Backtester.Engine
{
    /// <summary>
    /// Orchestrates the bar-by-bar backtest loop: fetches market data, synchronizes it into slices,
    /// feeds each symbol's real bar to the strategy, submits resulting orders to the broker, and records
    /// portfolio equity after each bar.
    /// </summary>
    public class Engine : IEngine
    {
        private readonly IHistoricalDataFetcher _fetcher;
        private readonly string[] _symbols;
        private readonly HashSet<string> _tradableSymbols;
        // The tradable symbols plus every ConversionSymbol the Portfolio declares, deduplicated: the full
        // set Engine fetches and slices on. A ConversionSymbol never reaches _symbols/_tradableSymbols, so
        // it never triggers strategy.OnBar and never surfaces in BacktestResult's symbol list or candle
        // history. A run whose Portfolio declares no conversion holds the caller's own array, so the
        // non-forex path builds nothing.
        private readonly string[] _fetchSymbols;
        private readonly DateTime _testFromUtc;
        private readonly DateTime _testToUtc;
        private readonly Warmup _warmup;
        private readonly string _interval;
        private readonly IStrategy _strategy;
        private readonly IBrokerSimulator _broker;
        private readonly Portfolio _portfolio;
        private bool _stopRequested;

        // The number of round trips already delivered to a round-trip observer: a high-water mark over
        // Portfolio.RoundTrips, so each bar delivers only the trips that closed on it.
        private int _deliveredRoundTrips;

        /// <summary>
        /// Initializes a new engine over a Test range with no warmup, so the Data range equals the Test
        /// range (ADR 0022). Market data for <paramref name="symbols"/> — plus any Conversion symbol
        /// <paramref name="portfolio"/> declares — across the Test range and interval is fetched (through
        /// the cache) when <see cref="StartAsync"/> is called.
        /// </summary>
        public Engine(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
            : this(fetcher, symbols, testFrom, testTo, Warmup.None, interval, strategy, broker, portfolio)
        {
        }

        /// <summary>
        /// Initializes a new engine over a Test range with a period (<see cref="TimeSpan"/>) warmup lead-in
        /// (ADR 0022): the fetch reaches back <paramref name="warmup"/> before <paramref name="testFrom"/>,
        /// the full Data-range history is handed to the strategy's <c>OnStart</c>, but only the Test range
        /// is looped and measured.
        /// </summary>
        public Engine(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            TimeSpan warmup,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
            : this(fetcher, symbols, testFrom, testTo, new PeriodWarmup(warmup), interval, strategy, broker, portfolio)
        {
        }

        /// <summary>
        /// Initializes a new engine over a Test range with an absolute-date warmup lead-in (ADR 0022): the
        /// Data range starts exactly at <paramref name="warmupStart"/> (guarded to be on or before
        /// <paramref name="testFrom"/>), the full Data-range history is handed to the strategy's <c>OnStart</c>,
        /// but only the Test range is looped and measured. A <paramref name="warmupStart"/> below a symbol's
        /// Coverage floor surfaces the existing <c>DataCoverageException</c> from the fetch.
        /// </summary>
        public Engine(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            DateTime warmupStart,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
            : this(fetcher, symbols, testFrom, testTo, new AbsoluteWarmup(warmupStart, testFrom), interval, strategy, broker, portfolio)
        {
        }

        /// <summary>
        /// Initializes a new engine over a Test range with a bar-count warmup lead-in (ADR 0022): the Data
        /// range reaches back exactly <paramref name="warmupBars"/> bars before <paramref name="testFrom"/>,
        /// resolved per symbol at fetch time through the warmup-capable <paramref name="fetcher"/>. The full
        /// Data-range history is handed to the strategy's <c>OnStart</c>, but only the Test range is looped and
        /// measured. A symbol lacking that many bars above its Coverage floor is refused with an
        /// <c>InsufficientWarmupBarsException</c>. Requiring an <see cref="IWarmupResolvingFetcher"/> makes
        /// bar-count warmup a compile-time capability rather than a runtime hope.
        /// </summary>
        public Engine(
            IWarmupResolvingFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            int warmupBars,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
            : this(fetcher, symbols, testFrom, testTo, new BarCountWarmup(warmupBars, fetcher), interval, strategy, broker, portfolio)
        {
        }

        /// <summary>
        /// The private core all overloads delegate to, holding the resolved <see cref="Warmup"/> that
        /// governs how far the Data range reaches ahead of the Test range.
        /// </summary>
        private Engine(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime testFrom,
            DateTime testTo,
            Warmup warmup,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
        {
            _fetcher = fetcher;
            _symbols = symbols;
            _tradableSymbols = new HashSet<string>(symbols);
            _fetchSymbols = BuildFetchSymbols(symbols, portfolio);
            _testFromUtc = testFrom;
            _testToUtc = testTo;
            _warmup = warmup;
            _interval = interval;
            _strategy = strategy;
            _broker = broker;
            _portfolio = portfolio;
        }

        /// <summary>
        /// Returns the series the run must fetch: the tradable <paramref name="symbols"/> plus every
        /// Conversion symbol <paramref name="portfolio"/> declares, deduplicated. Taking the conversion
        /// series from the Portfolio — the single hand-off point for Instruments — is what makes an
        /// Engine/Portfolio disagreement about which symbols convert through what unrepresentable rather
        /// than merely checked. A Portfolio declaring no conversion yields the caller's own array
        /// untouched, so a plain symbol-list run builds no conversion machinery at all.
        /// </summary>
        private static string[] BuildFetchSymbols(string[] symbols, Portfolio portfolio)
        {
            IReadOnlyCollection<string> conversionSymbols = portfolio.ConversionSymbols;
            if (conversionSymbols.Count == 0)
            {
                return symbols;
            }

            return symbols.Concat(conversionSymbols).Distinct().ToArray();
        }

        /// <summary>
        /// Fetches all symbols concurrently, hands the full history to the strategy's <c>OnStart</c>, then steps
        /// through the synchronized slices until exhausted or <see cref="Stop"/> is called.
        /// </summary>
        public async Task<BacktestResult> StartAsync(CancellationToken ct = default)
        {
            _stopRequested = false;
            _deliveredRoundTrips = 0;

            // The Data range: the Test range plus any warmup lead-in. Its full history warms the strategy's
            // precompute, but only the Test-range slice is looped and measured (ADR 0022).
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> dataSeries = await FetchSeriesAsync(ct).ConfigureAwait(false);

            _strategy.OnStart(TradableOnly(dataSeries));

            IReadOnlyDictionary<string, IReadOnlyList<Candle>> testSeries = ClipSeriesToTestRange(dataSeries);

            SliceSequence sequence = new(testSeries);
            foreach (MarketSlice slice in sequence.Slices())
            {
                if (_stopRequested)
                {
                    break;
                }

                RunOnce(slice);
            }

            // Collect any indicators the strategy chose to expose (ADR 0007 / 0012), then clip them to the
            // Test range so no point lands at a timestamp the clipped candles lack; a strategy that does not
            // implement the seam contributes none.
            IReadOnlyList<Indicator> indicators = _strategy is IIndicatorSource source
                ? ClipIndicatorsToTestRange(source.Indicators)
                : Array.Empty<Indicator>();

            return new BacktestResult(TradableOnly(testSeries), _portfolio, indicators, _symbols, _interval, _testFromUtc, _testToUtc, _broker.RejectedOrders, _broker.BracketLevelChanges);
        }

        /// <summary>Signals the engine to halt after completing the current bar.</summary>
        public void Stop()
        {
            _stopRequested = true;
        }

        /// <summary>
        /// Fetches every configured symbol concurrently and assembles the per-symbol series. Each symbol's
        /// Data-range start is resolved from the warmup first (per-symbol, since a bar-count warmup resolves
        /// to a different date per symbol), then fetched through to the Test range end.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> FetchSeriesAsync(CancellationToken ct)
        {
            Task<IReadOnlyList<Candle>>[] fetches = _fetchSymbols
                .Select(symbol => FetchSymbolSeriesAsync(symbol, ct))
                .ToArray();

            IReadOnlyList<Candle>[] results = await Task.WhenAll(fetches).ConfigureAwait(false);

            // Key: symbol/ticker (string) -> fetched candle series for that symbol
            Dictionary<string, IReadOnlyList<Candle>> series = new();
            for (int i = 0; i < _fetchSymbols.Length; i++)
            {
                series[_fetchSymbols[i]] = results[i];
            }

            return series;
        }

        /// <summary>
        /// Returns <paramref name="series"/> restricted to the tradable symbols, dropping any
        /// ConversionSymbol entries so they never reach the strategy's <c>OnStart</c> history or
        /// <see cref="BacktestResult.CandleHistory"/>. Returns the original reference unchanged when there
        /// are no ConversionSymbols to drop, so the common (non-forex) path allocates nothing.
        /// </summary>
        private IReadOnlyDictionary<string, IReadOnlyList<Candle>> TradableOnly(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> series)
        {
            if (_fetchSymbols.Length == _symbols.Length)
            {
                return series;
            }

            // Key: symbol/ticker (string) -> that symbol's candle series, tradable symbols only
            Dictionary<string, IReadOnlyList<Candle>> tradable = new(_symbols.Length);
            foreach (string symbol in _symbols)
            {
                if (series.TryGetValue(symbol, out IReadOnlyList<Candle> candles))
                {
                    tradable[symbol] = candles;
                }
            }

            return tradable;
        }

        /// <summary>
        /// Resolves one symbol's Data-range start from the warmup and fetches its series through to the Test
        /// range end.
        /// </summary>
        private async Task<IReadOnlyList<Candle>> FetchSymbolSeriesAsync(string symbol, CancellationToken ct)
        {
            DateTime dataFromUtc = await _warmup.ResolveDataStartAsync(symbol, _testFromUtc, _interval, ct).ConfigureAwait(false);
            return await _fetcher.FetchAsync(symbol, dataFromUtc, _testToUtc, _interval, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Processes a single slice: fills orders queued on the previous bar, records equity, delivers any
        /// round trips that closed on this bar to a round-trip observer, then invokes the strategy and queues
        /// any new orders for the next bar. This ordering prevents lookahead bias (ADR 0001).
        ///
        /// The strategy is invoked only for symbols with a <em>real</em> bar at this timestamp, never on a
        /// bar forward-filled from an earlier session because another symbol drove the timestamp. A symbol's
        /// resting orders cannot fill against such a stale bar (issue #56), so calling <c>OnBar</c> there
        /// would let the strategy act on a symbol whose orders are frozen — most damagingly, re-submit an
        /// entry whose prior submission is still pending, stacking the position. Gating on the real bar keeps
        /// each symbol's decision cadence aligned with its fill cadence.
        ///
        /// A ConversionSymbol riding along in the slice solely to convert another Instrument's currency
        /// (ADR 0029) never reaches this dispatch, even on its own real bar — it is plumbing the strategy
        /// never declared as tradable.
        /// </summary>
        private void RunOnce(MarketSlice slice)
        {
            // These two statements are ordered, not merely sequenced: filling first means a fill translates
            // at the conversion pair's last completed close, and recording the snapshot second is what
            // advances the Currency converter's rate to this bar's close for the end-of-bar mark. Swapping
            // them introduces currency lookahead — see CurrencyConverter's fill-timing invariant.
            _broker.ProcessBar(slice);
            _portfolio.RecordEquitySnapshot(slice);
            DeliverClosedRoundTrips();

            PortfolioSnapshot snapshot = _portfolio.SnapshotAt(slice.Timestamp);
            foreach ((string symbol, Candle bar) in slice.BarsBySymbol)
            {
                if (_tradableSymbols.Contains(symbol) && slice.HasRealBar(symbol))
                {
                    _strategy.OnBar(symbol, bar, snapshot, _broker);
                }
            }
        }

        /// <summary>
        /// Delivers each round trip that closed since the previous bar to the strategy when it observes
        /// round trips (<see cref="IRoundTripObserver"/>), in close order, before this bar's <c>OnBar</c>.
        /// A strategy that does not implement the seam receives nothing. The engine takes no action of its
        /// own on the result.
        /// </summary>
        private void DeliverClosedRoundTrips()
        {
            if (_strategy is not IRoundTripObserver observer)
            {
                return;
            }

            IReadOnlyList<RoundTrip> roundTrips = _portfolio.RoundTrips;
            for (int index = _deliveredRoundTrips; index < roundTrips.Count; index++)
            {
                observer.OnRoundTripClosed(roundTrips[index]);
            }

            _deliveredRoundTrips = roundTrips.Count;
        }

        /// <summary>
        /// Clips each symbol's Data-range series down to the Test range, dropping the warmup lead-in so the
        /// loop and the reported candles cover exactly the tested period. A series already wholly inside the
        /// Test range is returned by reference, so the no-warmup path allocates nothing.
        /// </summary>
        private IReadOnlyDictionary<string, IReadOnlyList<Candle>> ClipSeriesToTestRange(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> dataSeries)
        {
            // Key: symbol/ticker (string) -> that symbol's candle series clipped to the Test range
            Dictionary<string, IReadOnlyList<Candle>> clipped = new();
            foreach ((string symbol, IReadOnlyList<Candle> candles) in dataSeries)
            {
                clipped[symbol] = ClipCandles(candles);
            }

            return clipped;
        }

        /// <summary>
        /// Returns the candles within the Test range, preserving the original list reference when none fall
        /// outside it (nothing to trim).
        /// </summary>
        private IReadOnlyList<Candle> ClipCandles(IReadOnlyList<Candle> candles)
        {
            List<Candle> inRange = null;
            for (int index = 0; index < candles.Count; index++)
            {
                Candle candle = candles[index];
                if (InTestRange(candle.Timestamp))
                {
                    inRange?.Add(candle);
                }
                else if (inRange is null)
                {
                    // First out-of-range bar: materialize the kept prefix and switch to filtering.
                    inRange = new List<Candle>(candles.Count);
                    for (int kept = 0; kept < index; kept++)
                    {
                        inRange.Add(candles[kept]);
                    }
                }
            }

            return inRange ?? candles;
        }

        /// <summary>
        /// Clips each exposed indicator's series to the Test range. The values were computed over the full
        /// Data-range history, so the line is already at its correct warm level on the first drawn bar;
        /// only the lead-in points are dropped (ADR 0022).
        /// </summary>
        private IReadOnlyList<Indicator> ClipIndicatorsToTestRange(IReadOnlyList<Indicator> indicators)
        {
            List<Indicator> clipped = new(indicators.Count);
            foreach (Indicator indicator in indicators)
            {
                List<IndicatorSeries> clippedSeries = new(indicator.Series.Count);
                foreach (IndicatorSeries line in indicator.Series)
                {
                    clippedSeries.Add(new IndicatorSeries(line.Name, line.Shape, ClipPoints(line.Points)));
                }

                clipped.Add(new Indicator(indicator.Name, indicator.Symbol, indicator.Pane, clippedSeries));
            }

            return clipped;
        }

        /// <summary>Returns the indicator points whose timestamps fall within the Test range.</summary>
        private IReadOnlyList<IndicatorPoint> ClipPoints(IReadOnlyList<IndicatorPoint> points)
        {
            List<IndicatorPoint> inRange = new();
            foreach (IndicatorPoint point in points)
            {
                if (InTestRange(point.Timestamp))
                {
                    inRange.Add(point);
                }
            }

            return inRange;
        }

        /// <summary>
        /// Whether a bar timestamp falls inside the inclusive Test range, comparing as UTC so a series
        /// carrying unspecified-kind timestamps is treated the same as the SliceSequence timeline.
        /// </summary>
        private bool InTestRange(DateTime timestamp)
        {
            DateTime utc = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            return utc >= _testFromUtc && utc <= _testToUtc;
        }
    }
}
