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
    /// feeds each bar to the strategy, submits resulting orders to the broker, and records portfolio
    /// equity after each bar.
    /// </summary>
    public class Engine : IEngine
    {
        private readonly IHistoricalDataFetcher _fetcher;
        private readonly string[] _symbols;
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
        /// range (ADR 0022). Market data for <paramref name="symbols"/> across the Test range and interval
        /// is fetched (through the cache) when <see cref="StartAsync"/> is called.
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
            _testFromUtc = testFrom;
            _testToUtc = testTo;
            _warmup = warmup;
            _interval = interval;
            _strategy = strategy;
            _broker = broker;
            _portfolio = portfolio;
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

            _strategy.OnStart(dataSeries);

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

            return new BacktestResult(testSeries, _portfolio, indicators, _symbols, _interval, _testFromUtc, _testToUtc, _broker.RejectedOrders, _broker.BracketLevelChanges);
        }

        /// <summary>Signals the engine to halt after completing the current bar.</summary>
        public void Stop()
        {
            _stopRequested = true;
        }

        /// <summary>
        /// Fetches every configured symbol concurrently and assembles the per-symbol series.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, IReadOnlyList<Candle>>> FetchSeriesAsync(CancellationToken ct)
        {
            DateTime dataFromUtc = _warmup.DataStart(_testFromUtc);
            Task<IReadOnlyList<Candle>>[] fetches = _symbols
                .Select(symbol => _fetcher.FetchAsync(symbol, dataFromUtc, _testToUtc, _interval, ct))
                .ToArray();

            IReadOnlyList<Candle>[] results = await Task.WhenAll(fetches).ConfigureAwait(false);

            // Key: symbol/ticker (string) -> fetched candle series for that symbol
            Dictionary<string, IReadOnlyList<Candle>> series = new();
            for (int i = 0; i < _symbols.Length; i++)
            {
                series[_symbols[i]] = results[i];
            }

            return series;
        }

        /// <summary>
        /// Processes a single slice: fills orders queued on the previous bar, records equity, delivers any
        /// round trips that closed on this bar to a round-trip observer, then invokes the strategy and queues
        /// any new orders for the next bar. This ordering prevents lookahead bias (ADR 0001).
        /// </summary>
        private void RunOnce(MarketSlice slice)
        {
            _broker.ProcessBar(slice);
            _portfolio.RecordEquitySnapshot(slice);
            DeliverClosedRoundTrips();

            PortfolioSnapshot snapshot = _portfolio.SnapshotAt(slice.Timestamp);
            foreach ((string symbol, Candle bar) in slice.BarsBySymbol)
            {
                if (bar is not null)
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
