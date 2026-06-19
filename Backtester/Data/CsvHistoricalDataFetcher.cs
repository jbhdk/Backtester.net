using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Returns historical candle data by reading the canonical <see cref="CsvBarLoader"/> CSV for a
    /// symbol and interval from a local folder. Backed entirely by a committed file: no network and
    /// no cache, so a backtest driven by this fetcher runs the same way every time.
    /// </summary>
    public class CsvHistoricalDataFetcher : IHistoricalDataFetcher
    {
        private readonly CsvBarLoader _csv;
        private readonly string _dataFolder;

        /// <summary>
        /// Initializes a new fetcher that reads CSV files from <paramref name="dataFolder"/>.
        /// </summary>
        public CsvHistoricalDataFetcher(string dataFolder)
        {
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentNullException(nameof(dataFolder));
            }

            _csv = new();
            _dataFolder = dataFolder;
        }

        /// <summary>
        /// Returns every candle in the symbol's CSV file. The requested range and interval select the
        /// file via the canonical naming convention; the file's contents are returned unfiltered.
        /// Returns an empty list when no file exists for the symbol.
        /// </summary>
        public Task<IReadOnlyList<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            string path = Path.Combine(_dataFolder, CsvBarLoader.FileName(symbol, interval));
            return Task.FromResult(_csv.ReadAll(path));
        }
    }
}
