using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Backtester.Data
{
    /// <summary>
    /// Reads and writes the Coverage floor sidecar for one symbol-and-interval: a small JSON file,
    /// beside the Cache CSV, recording the earliest range start ever requested from the Provider.
    /// The Cache CSV format is untouched; the floor lives here so the CSV stays a clean OHLCV file.
    /// </summary>
    public class CoverageFloorLoader
    {
        private const string FloorProperty = "coverageFloorUtc";

        /// <summary>
        /// Builds the canonical sidecar file name for a symbol and interval (e.g. <c>AAPL_1h.meta.json</c>).
        /// The symbol is trimmed and upper-cased so casing differences resolve to the same file.
        /// </summary>
        public string FileName(string symbol, string interval)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            return $"{symbol.Trim().ToUpperInvariant()}_{interval}.meta.json";
        }

        /// <summary>
        /// Reads the Coverage floor from the sidecar file at the given path, or null when the file is
        /// absent (a legacy Cache with no recorded floor).
        /// </summary>
        public DateTime? Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                if (!document.RootElement.TryGetProperty(FloorProperty, out JsonElement element))
                {
                    return null;
                }

                if (!DateTime.TryParse(element.GetString(), null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime floor))
                {
                    return null;
                }

                return floor;
            }
        }

        /// <summary>
        /// Writes the Coverage floor to the sidecar file at the given path, replacing any existing file.
        /// </summary>
        public void Write(string path, DateTime floorUtc)
        {
            string dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);

            using (FileStream stream = File.Create(path))
            using (Utf8JsonWriter writer = new(stream))
            {
                writer.WriteStartObject();
                writer.WriteString(FloorProperty, floorUtc.ToUniversalTime());
                writer.WriteEndObject();
            }
        }
    }
}
