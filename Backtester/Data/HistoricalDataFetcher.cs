using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    public class HistoricalDataFetcher
    {
        private readonly IHistoricalDataProvider _provider;
        private readonly CsvBarLoader _csv;
        private readonly string _dataFolder;
        private readonly TimeSpan _freshnessWindow = TimeSpan.FromDays(7);

        public HistoricalDataFetcher(IHistoricalDataProvider provider, string dataFolder = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _csv = new();
            _dataFolder = dataFolder ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        }

        public async Task<IReadOnlyList<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentNullException(nameof(symbol));
            symbol = symbol.Trim().ToUpperInvariant();

            Directory.CreateDirectory(_dataFolder);
            string filename = Path.Combine(_dataFolder, $"{symbol}_{interval}.csv");

            List<Candle> existing = _csv.ReadAll(filename).ToList();

            // If latest candle is recent enough and covers requested range, return filtered
            DateTime? latest = existing.Count == 0 ? (DateTime?)null : existing.Max(candle => candle.Timestamp);
            DateTime now = DateTime.UtcNow;
            if (latest != null && latest >= now - _freshnessWindow && CoversRange(existing, fromUtc, toUtc))
            {
                return existing.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).ToList();
            }

            // Need to fetch missing data
            if (existing.Count == 0)
            {
                List<Candle> fetched = (await _provider.FetchAsync(symbol, fromUtc, toUtc, interval, ct).ConfigureAwait(false)).ToList();
                _csv.WriteAll(filename, fetched);
                return fetched;
            }

            // existing non-empty but maybe stale or incomplete
            DateTime lastTimestamp = latest!.Value;
            DateTime nextNeeded = AddInterval(lastTimestamp, interval);
            if (nextNeeded > toUtc)
            {
                // existing data covers range but was stale by time; still return filtered
                return existing.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).ToList();
            }

            List<Candle> fetchedMore = (await _provider.FetchAsync(symbol, nextNeeded, toUtc, interval, ct).ConfigureAwait(false)).ToList();
            if (fetchedMore.Count > 0)
            {
                _csv.AppendAndMerge(filename, fetchedMore);
                existing.AddRange(fetchedMore);
            }

            return existing.Where(candle => candle.Timestamp >= fromUtc && candle.Timestamp <= toUtc).OrderBy(candle => candle.Timestamp).ToList();
        }

        private static bool CoversRange(List<Candle> list, DateTime fromUtc, DateTime toUtc)
        {
            if (list.Count == 0) return false;
            DateTime min = list.Min(candle => candle.Timestamp);
            DateTime max = list.Max(candle => candle.Timestamp);
            return min <= fromUtc && max >= toUtc;
        }

        private static DateTime AddInterval(DateTime ts, string interval)
        {
            if (string.IsNullOrWhiteSpace(interval)) throw new ArgumentNullException(nameof(interval));
            interval = interval.Trim().ToLowerInvariant();
            if (interval.EndsWith("h") && int.TryParse(interval.Substring(0, interval.Length - 1), out int h))
            {
                return ts.AddHours(h);
            }
            if (interval.EndsWith("d") && int.TryParse(interval.Substring(0, interval.Length - 1), out int d))
            {
                return ts.AddDays(d);
            }
            if (interval.EndsWith("wk") && int.TryParse(interval.Substring(0, interval.Length - 2), out int wk))
            {
                return ts.AddDays(7 * wk);
            }
            if (interval.EndsWith("mo") && int.TryParse(interval.Substring(0, interval.Length - 2), out int m))
            {
                return ts.AddMonths(m);
            }

            // Fallback: try parse as minutes (e.g., "60m")
            if (interval.EndsWith("m") && int.TryParse(interval.Substring(0, interval.Length - 1), out int mm))
            {
                return ts.AddMinutes(mm);
            }

            throw new NotSupportedException($"Interval string '{interval}' not supported.");
        }
    }
}
