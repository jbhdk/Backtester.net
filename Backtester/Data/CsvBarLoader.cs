using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Reads and writes OHLCV candle data in a simple CSV format.
    /// </summary>
    public class CsvBarLoader
    {
        private const string Header = "Timestamp,Open,High,Low,Close,Volume";

        /// <summary>
        /// Reads all candles from the CSV file at the given path, sorted by timestamp ascending.
        /// Returns an empty list if the file does not exist.
        /// </summary>
        public IReadOnlyList<Candle> ReadAll(string path)
        {
            if (!File.Exists(path))
                return Array.Empty<Candle>();

            string[] lines = File.ReadAllLines(path);
            List<Candle> result = new List<Candle>(lines.Length);

            int start = 0;
            if (lines.Length > 0 && lines[0].StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase))
                start = 1;

            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 6)
                    continue;

                if (!DateTime.TryParse(parts[0], null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime ts))
                    continue;

                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open))
                    continue;
                if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high))
                    continue;
                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low))
                    continue;
                if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close))
                    continue;
                if (!decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal vol))
                    vol = 0m;

                result.Add(new Candle
                {
                    Timestamp = ts,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = vol
                });
            }

            return result.OrderBy(candle => candle.Timestamp).ToList();
        }

        /// <summary>
        /// Writes the given candles to the CSV file at the specified path, replacing any existing file.
        /// </summary>
        public void WriteAll(string path, IEnumerable<Candle> candles)
        {
            string dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);

            string tmp = Path.GetTempFileName();
            using (StreamWriter writer = new StreamWriter(tmp, false))
            {
                writer.WriteLine(Header);
                foreach (Candle candle in candles.OrderBy(candle => candle.Timestamp))
                {
                    writer.WriteLine(Format(candle));
                }
            }

            // Replace atomically
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        /// <summary>
        /// Appends the given candles to the existing file, deduplicating by timestamp and re-sorting.
        /// </summary>
        public void AppendAndMerge(string path, IEnumerable<Candle> additional)
        {
            List<Candle> existing = ReadAll(path).ToList();
            existing.AddRange(additional);
            List<Candle> merged = existing
                .GroupBy(candle => candle.Timestamp)
                .Select(g => g.Last())
                .OrderBy(candle => candle.Timestamp)
                .ToList();

            WriteAll(path, merged);
        }

        /// <summary>
        /// Returns the most recent candle timestamp in the file, or null if the file is empty or missing.
        /// </summary>
        public DateTime? GetLatestTimestamp(string path)
        {
            IReadOnlyList<Candle> list = ReadAll(path);
            if (list.Count == 0)
                return null;
            return list.Max(candle => candle.Timestamp);
        }

        private static string Format(Candle candle)
        {
            // ISO 8601 UTC
            return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-ddTHH:mm:ssZ},{1},{2},{3},{4},{5}",
                candle.Timestamp.ToUniversalTime(),
                candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
        }
    }
}
