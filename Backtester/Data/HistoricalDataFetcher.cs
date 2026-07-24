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

            // The front edge is covered (guarded above); apply the shared tail-freshness policy to warm the
            // recent edge if needed, then serve the requested slice.
            List<Candle> warmed = await RefreshTailAsync(symbol, filename, existing, toUtc, interval, ct).ConfigureAwait(false);
            return warmed.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).OrderBy(candle => candle.Timestamp).ToList();
        }

        /// <summary>
        /// Applies the shared tail-freshness policy to a non-empty cache: the cache is trusted with no
        /// Provider call while its most recent bar is within the freshness window of the requested end (or
        /// now, when the end is in the future); otherwise the tail is extended from the latest cached bar and
        /// the dedup in <c>AppendAndMerge</c> absorbs the overlap. Returns the (possibly extended) cache. The
        /// front edge is assumed already covered by the caller. See
        /// docs/adr/0006-cache-freshness-over-completeness.md.
        /// </summary>
        private async Task<List<Candle>> RefreshTailAsync(string symbol, string filename, List<Candle> existing, DateTime toUtc, string interval, CancellationToken ct)
        {
            DateTime latest = existing.Max(candle => candle.Timestamp);
            DateTime now = DateTime.UtcNow;
            DateTime reference = toUtc < now ? toUtc : now;
            if (latest >= reference - _freshnessWindow)
            {
                return existing;
            }

            List<Candle> fetchedMore = (await _provider.FetchAsync(symbol, latest, toUtc, interval, ct).ConfigureAwait(false)).ToList();
            if (fetchedMore.Count > 0)
            {
                _csv.AppendAndMerge(filename, fetchedMore);
                existing.AddRange(fetchedMore);
            }

            return existing;
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
        /// Primes one symbol cache-aware, sharing <see cref="FetchAsync"/>'s caching policy: an empty Cache,
        /// or a requested start that reaches below the front already covered, triggers a wholesale
        /// <c>[from, to]</c> fetch (which alone may honestly lower the floor to <paramref name="fromUtc"/>);
        /// otherwise only the stale tail is extended and a still-fresh Cache costs no Provider call. The
        /// Coverage floor is then lowered to the requested start (a no-op when it already sits earlier).
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

            List<Candle> existing = _csv.ReadAll(filename).ToList();
            DateTime? floor = _floors.Read(floorFilename);

            // The front is uncovered when the requested start reaches below the earliest range ever asked of
            // the Provider — the Coverage floor, or (for a legacy Cache with no floor) the earliest cached bar.
            // Only then must we fetch wholesale, because the floor may be lowered to X only after actually
            // calling the Provider from X (ADR 0021); a still-covered front lets the shared tail policy run.
            bool frontUncovered = existing.Count > 0
                && (floor.HasValue ? fromUtc < floor.Value : fromUtc < existing.Min(candle => candle.Timestamp));

            if (existing.Count == 0 || frontUncovered)
            {
                List<Candle> fetched = (await _provider.FetchAsync(symbol, fromUtc, toUtc, interval, ct).ConfigureAwait(false)).ToList();
                if (existing.Count == 0)
                {
                    _csv.WriteAll(filename, fetched);
                }
                else
                {
                    _csv.AppendAndMerge(filename, fetched);
                }

                _floors.Lower(floorFilename, fromUtc);
                return;
            }

            // Front already covered: reuse FetchAsync's tail-freshness policy — no Provider call when fresh,
            // incremental tail otherwise. Lowering the floor to fromUtc here is a no-op (fromUtc >= floor).
            await RefreshTailAsync(symbol, filename, existing, toUtc, interval, ct).ConfigureAwait(false);
            _floors.Lower(floorFilename, fromUtc);
        }
    }
}
