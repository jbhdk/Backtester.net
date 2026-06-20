using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data.Yahoo
{
    /// <summary>
    /// Fetches historical OHLCV candle data from Yahoo Finance's v8 chart JSON API.
    /// Supports intraday and daily intervals; caching is handled by <see cref="HistoricalDataFetcher"/>.
    /// </summary>
    public class YahooHistoricalDataProvider : IHistoricalDataProvider
    {
        private readonly HttpClient _http;

        /// <summary>
        /// Initializes a new provider using the given <see cref="HttpClient"/>, or a default instance if null.
        /// </summary>
        public YahooHistoricalDataProvider(HttpClient http = null)
        {
            _http = http ?? new HttpClient();
        }

        /// <summary>
        /// Downloads and parses candles for the symbol from the Yahoo Finance v8 chart endpoint.
        /// Throws <see cref="NotSupportedException"/> for intervals not supported by Yahoo.
        /// </summary>
        public async Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            string[] supported = new[] { "1m", "2m", "5m", "15m", "30m", "60m", "1h", "1d", "1wk", "1mo" };
            if (!supported.Contains(interval))
            {
                throw new NotSupportedException($"Yahoo v8 provider does not support interval '{interval}'. Supported: {string.Join(',', supported)}.");
            }

            long period1 = ((DateTimeOffset)fromUtc).ToUnixTimeSeconds();
            long period2 = ((DateTimeOffset)toUtc).ToUnixTimeSeconds();

            string url = $"https://query2.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval={interval}";

            using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Yahoo v8 HTTP error {resp.StatusCode}: {text}");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseV8Json(json);
        }

        /// <summary>
        /// Parses a Yahoo Finance v8 chart JSON payload into candles.
        /// Rows where open or close is null (market holiday gaps) are skipped.
        /// </summary>
        private static IEnumerable<Candle> ParseV8Json(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement result = doc.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0];

            JsonElement timestamps = result.GetProperty("timestamp");
            JsonElement quote = result.GetProperty("indicators").GetProperty("quote")[0];

            JsonElement opens   = quote.GetProperty("open");
            JsonElement highs   = quote.GetProperty("high");
            JsonElement lows    = quote.GetProperty("low");
            JsonElement closes  = quote.GetProperty("close");
            JsonElement volumes = quote.GetProperty("volume");

            List<Candle> candles = new();
            for (int i = 0; i < timestamps.GetArrayLength(); i++)
            {
                if (opens[i].ValueKind  == JsonValueKind.Null)
                {
                    continue;
                }

                if (closes[i].ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                candles.Add(new Candle
                {
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime,
                    Open      = opens[i].GetDecimal(),
                    High      = highs[i].GetDecimal(),
                    Low       = lows[i].GetDecimal(),
                    Close     = closes[i].GetDecimal(),
                    Volume    = volumes[i].ValueKind != JsonValueKind.Null ? volumes[i].GetDecimal() : 0m
                });
            }

            return candles.OrderBy(c => c.Timestamp).ToList();
        }
    }
}
