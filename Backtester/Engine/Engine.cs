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
        private readonly DateTime _fromUtc;
        private readonly DateTime _toUtc;
        private readonly string _interval;
        private readonly IStrategy _strategy;
        private readonly IBrokerSimulator _broker;
        private readonly Portfolio _portfolio;
        private bool _stopRequested;

        // The number of round trips already delivered to a round-trip observer: a high-water mark over
        // Portfolio.RoundTrips, so each bar delivers only the trips that closed on it.
        private int _deliveredRoundTrips;

        /// <summary>
        /// Initializes a new engine. Market data for <paramref name="symbols"/> over the given range and interval
        /// is fetched (through the cache) when <see cref="StartAsync"/> is called.
        /// </summary>
        public Engine(
            IHistoricalDataFetcher fetcher,
            string[] symbols,
            DateTime fromUtc,
            DateTime toUtc,
            string interval,
            IStrategy strategy,
            IBrokerSimulator broker,
            Portfolio portfolio)
        {
            _fetcher = fetcher;
            _symbols = symbols;
            _fromUtc = fromUtc;
            _toUtc = toUtc;
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
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> series = await FetchSeriesAsync(ct).ConfigureAwait(false);

            _strategy.OnStart(series);

            SliceSequence sequence = new(series);
            foreach (MarketSlice slice in sequence.Slices())
            {
                if (_stopRequested)
                {
                    break;
                }

                RunOnce(slice);
            }

            // Collect any indicators the strategy chose to expose (ADR 0007 / 0012); a strategy that
            // does not implement the seam contributes none.
            IReadOnlyList<Indicator> indicators = _strategy is IIndicatorSource source
                ? source.Indicators
                : Array.Empty<Indicator>();

            return new BacktestResult(series, _portfolio, indicators, _symbols, _interval, _fromUtc, _toUtc, _broker.RejectedOrders, _broker.BracketLevelChanges);
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
            Task<IReadOnlyList<Candle>>[] fetches = _symbols
                .Select(symbol => _fetcher.FetchAsync(symbol, _fromUtc, _toUtc, _interval, ct))
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
    }
}
