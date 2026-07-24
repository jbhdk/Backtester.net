using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Fetches historical candle data from a provider and caches the results as CSV files on disk.
    /// Subsequent requests are served from cache when data is fresh and covers the requested range.
    /// </summary>
    public class HistoricalDataFetcher : IWarmupResolvingFetcher, IDataPrimer
    {
        private readonly IHistoricalDataProvider _provider;
        private readonly CsvBarLoader _csv;
        private readonly CoverageFloorLoader _floors;
        private readonly string _dataFolder;
        private readonly TimeSpan _freshnessWindow = TimeSpan.FromDays(7);

        /// <summary>
        /// Initializes a new fetcher backed by the given provider. Data is cached under <paramref name="dataFolder"/>.
        /// </summary>
        public HistoricalDataFetcher(IHistoricalDataProvider provider, string dataFolder = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _csv = new();
            _floors = new();
            _dataFolder = dataFolder ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        }

        /// <summary>
        /// Returns candles for the symbol and date range, fetching from the provider only when the cache is absent
        /// or stale. A requested start earlier than the cache's Coverage floor is refused with a
        /// <see cref="DataCoverageException"/> rather than served a silently short slice.
        /// </summary>
        public async Task<IReadOnlyList<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            symbol = symbol.Trim().ToUpperInvariant();

            Directory.CreateDirectory(_dataFolder);
            string filename = Path.Combine(_dataFolder, CsvBarLoader.FileName(symbol, interval));
            string floorFilename = Path.Combine(_dataFolder, _floors.FileName(symbol, interval));

            List<Candle> existing = _csv.ReadAll(filename).ToList();

            // Empty cache: fetch the full requested range, persist it, and establish the Coverage floor at
            // the requested start — this is the earliest range start we have asked the Provider for.
            if (existing.Count == 0)
            {
                List<Candle> fetched = (await _provider.FetchAsync(symbol, fromUtc, toUtc, interval, ct).ConfigureAwait(false)).ToList();
                _csv.WriteAll(filename, fetched);
                _floors.Write(floorFilename, fromUtc);
                return fetched;
            }

            // Front-edge guard: a requested start earlier than the Coverage floor is refused, because below
            // the floor the Cache's lack of bars is unknown (that window was never asked of the Provider).
            // A legacy Cache with no floor sidecar is trusted as before. See ADR 0021.
            DateTime? floor = _floors.Read(floorFilename);
            if (floor.HasValue && fromUtc < floor.Value)
            {
                throw new DataCoverageException(symbol, fromUtc, floor.Value, interval);
            }

            // A non-empty cache is trusted while its most recent bar is within the freshness window of the
            // requested end (or now, when the end is in the future). This treats a completed historical window
            // as fresh forever, and tolerates the cache lagging the requested end by up to one window. See
            // docs/adr/0006-cache-freshness-over-completeness.md.
            DateTime latest = existing.Max(candle => candle.Timestamp);
            DateTime now = DateTime.UtcNow;
            DateTime reference = toUtc < now ? toUtc : now;
            if (latest >= reference - _freshnessWindow)
            {
                return existing.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).ToList();
            }

            // Stale: extend the tail from the latest cached bar. The dedup in AppendAndMerge absorbs the overlap.
            List<Candle> fetchedMore = (await _provider.FetchAsync(symbol, latest, toUtc, interval, ct).ConfigureAwait(false)).ToList();
            if (fetchedMore.Count > 0)
            {
                _csv.AppendAndMerge(filename, fetchedMore);
                existing.AddRange(fetchedMore);
            }

            return existing.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).OrderBy(candle => candle.Timestamp).ToList();
        }

        /// <summary>
        /// Resolves the exact Data-range start for the symbol that yields exactly <paramref name="warmupBars"/>
        /// bars before <paramref name="testFrom"/>, reading only the cached bars and the Coverage floor (no
        /// Provider call). Counts the cached bars at or above the floor and strictly before the Test start;
        /// when at least that many exist, returns the timestamp of the Nth-from-last such bar, otherwise throws
        /// <see cref="InsufficientWarmupBarsException"/> rather than serve a short lead-in (ADR 0021 / 0022).
        /// </summary>
        public Task<DateTime> ResolveWarmupStartAsync(string symbol, DateTime testFrom, int warmupBars, string interval, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            if (warmupBars <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "Warmup bar count must be positive.");
            }

            symbol = symbol.Trim().ToUpperInvariant();
            string filename = Path.Combine(_dataFolder, CsvBarLoader.FileName(symbol, interval));
            string floorFilename = Path.Combine(_dataFolder, _floors.FileName(symbol, interval));

            DateTime? floor = _floors.Read(floorFilename);
            List<Candle> eligible = _csv.ReadAll(filename)
                .Where(candle => candle.Timestamp < testFrom && (!floor.HasValue || candle.Timestamp >= floor.Value))
                .OrderBy(candle => candle.Timestamp)
                .ToList();

            if (eligible.Count < warmupBars)
            {
                throw new InsufficientWarmupBarsException(symbol, warmupBars, eligible.Count, interval);
            }

            DateTime dataStart = eligible[eligible.Count - warmupBars].Timestamp;
            return Task.FromResult(DateTime.SpecifyKind(dataStart, DateTimeKind.Utc));
        }

        /// <summary>
        /// Warms the Cache for each symbol over the given range and lowers each symbol's Coverage floor to
        /// <paramref name="fromUtc"/>. Symbols are primed concurrently.
        /// </summary>
        public async Task PrimeAsync(string[] symbols, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            if (symbols is null)
            {
                throw new ArgumentNullException(nameof(symbols));
            }

            Directory.CreateDirectory(_dataFolder);
            Task[] primes = symbols.Select(symbol => PrimeSymbolAsync(symbol, fromUtc, toUtc, interval, ct)).ToArray();
            await Task.WhenAll(primes).ConfigureAwait(false);
        }

        /// <summary>
        /// Primes one symbol: fetches the full range wholesale, merges it into the Cache (dedup absorbs any
        /// overlap), then lowers the symbol's Coverage floor to the requested start.
        /// </summary>
        private async Task PrimeSymbolAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            symbol = symbol.Trim().ToUpperInvariant();
            string filename = Path.Combine(_dataFolder, CsvBarLoader.FileName(symbol, interval));
            string floorFilename = Path.Combine(_dataFolder, _floors.FileName(symbol, interval));

            List<Candle> fetched = (await _provider.FetchAsync(symbol, fromUtc, toUtc, interval, ct).ConfigureAwait(false)).ToList();
            _csv.AppendAndMerge(filename, fetched);
            _floors.Lower(floorFilename, fromUtc);
        }
    }
}
