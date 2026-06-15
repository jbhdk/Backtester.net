using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    public class YahooHistoricalDataProvider : IHistoricalDataProvider
    {
        private readonly HttpClient _http;

        public YahooHistoricalDataProvider(HttpClient http = null)
        {
            _http = http ?? new HttpClient();
        }

        public async Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            // Yahoo public CSV download supports daily/weekly/monthly intervals and also intraday hourly data (limited to ~730 days).
            // For unsupported intervals we surface an error so caller can select another provider.
            string[] supported = new[] { "1d", "1wk", "1mo", "1h", "60m" };
            if (!supported.Contains(interval))
                throw new NotSupportedException($"Yahoo provider does not support interval '{interval}'. Supported: {string.Join(',', supported)}.");

            // Convert to unix timestamps (seconds)
            long period1 = ((DateTimeOffset)fromUtc).ToUnixTimeSeconds();
            long period2 = ((DateTimeOffset)toUtc).ToUnixTimeSeconds();

            string url = $"https://query1.finance.yahoo.com/v7/finance/download/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval={interval}&events=history&includeAdjustedClose=true";

            using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Yahoo provider HTTP error {resp.StatusCode}: {text}");
            }

            string csv = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using StringReader sr = new StringReader(csv);
            string header = sr.ReadLine();
            List<Candle> list = new();
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(',');
                // Expected: Date,Open,High,Low,Close,Adj Close,Volume
                if (parts.Length < 6)
                    continue;
                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt))
                    continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open))
                    continue;
                if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high))
                    continue;
                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low))
                    continue;
                if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close))
                    continue;

                decimal vol = 0m;
                // volume may be at position 6 if Adj Close exists
                if (parts.Length >= 7)
                    decimal.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out vol);
                else if (parts.Length >= 6)
                    decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out vol);

                list.Add(new Candle
                {
                    Timestamp = dt.ToUniversalTime(),
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = vol
                });
            }

            return list.OrderBy(candle => candle.Timestamp).ToList();
        }
    }
}
