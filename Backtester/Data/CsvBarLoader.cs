using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Backtester.Core;

namespace Backtester.Data
{
    public class CsvBarLoader
    {
        private const string Header = "Timestamp,Open,High,Low,Close,Volume";

        public IReadOnlyList<Candle> ReadAll(string path)
        {
            if (!File.Exists(path))
                return Array.Empty<Candle>();

            var lines = File.ReadAllLines(path);
            var result = new List<Candle>(lines.Length);

            int start = 0;
            if (lines.Length > 0 && lines[0].StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase))
                start = 1;

            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 6)
                    continue;

                if (!DateTime.TryParse(parts[0], null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts))
                    continue;

                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
                    continue;
                if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
                    continue;
                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
                    continue;
                if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                    continue;
                if (!decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var vol))
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

            return result.OrderBy(c => c.Timestamp).ToList();
        }

        public void WriteAll(string path, IEnumerable<Candle> candles)
        {
            var dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);

            var tmp = Path.GetTempFileName();
            using (var w = new StreamWriter(tmp, false))
            {
                w.WriteLine(Header);
                foreach (var candle in candles.OrderBy(candle => candle.Timestamp))
                {
                    w.WriteLine(Format(candle));
                }
            }

            // Replace atomically
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        public void AppendAndMerge(string path, IEnumerable<Candle> additional)
        {
            var existing = ReadAll(path).ToList();
            existing.AddRange(additional);
            var merged = existing
                .GroupBy(candle => candle.Timestamp)
                .Select(g => g.Last())
                .OrderBy(candle => candle.Timestamp)
                .ToList();

            WriteAll(path, merged);
        }

        public DateTime? GetLatestTimestamp(string path)
        {
            var list = ReadAll(path);
            if (list.Count == 0)
                return null;
            return list.Max(candle => candle.Timestamp);
        }

        private static string Format(Candle c)
        {
            // ISO 8601 UTC
            return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-ddTHH:mm:ssZ},{1},{2},{3},{4},{5}",
                c.Timestamp.ToUniversalTime(),
                c.Open, c.High, c.Low, c.Close, c.Volume);
        }
    }
}
