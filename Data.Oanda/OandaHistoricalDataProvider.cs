using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data.Oanda
{
    /// <summary>
    /// Fetches historical OHLCV candle data for forex instruments from Oanda's v20 REST API.
    /// This tracer-bullet implementation always targets the Practice environment and always requests
    /// Mid-price candles. Wide date ranges are chunked to Oanda's 5000-candle-per-response cap and
    /// walked until the full range is covered. <see cref="Candle.Volume"/> is Oanda's per-candle tick
    /// count, not consolidated traded volume — forex spot is decentralized, so no such figure exists.
    /// </summary>
    public class OandaHistoricalDataProvider : IHistoricalDataProvider
    {
        private const string PracticeBaseUrl = "https://api-fxpractice.oanda.com";

        /// <summary>The maximum number of candles Oanda's v20 candles endpoint returns in a single response.</summary>
        private const int MaxCandlesPerRequest = 5000;

        private readonly HttpClient _http;
        private readonly string _apiToken;

        /// <summary>
        /// Initializes a new provider using the given <see cref="HttpClient"/> and Oanda API bearer token.
        /// </summary>
        public OandaHistoricalDataProvider(HttpClient http, string apiToken)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _apiToken = apiToken ?? throw new ArgumentNullException(nameof(apiToken));
        }

        /// <summary>
        /// Initializes a new provider from an Oanda API bearer token, building a default <see cref="HttpClient"/>.
        /// </summary>
        public OandaHistoricalDataProvider(string apiToken)
            : this(new HttpClient(), apiToken)
        {
        }

        /// <summary>
        /// Fetches Mid-price candles for the instrument from Oanda's v20 candles endpoint against the
        /// Practice host and maps them to <see cref="Candle"/>. The <paramref name="symbol"/> is used
        /// verbatim as Oanda's instrument name (e.g. <c>EUR_USD</c>). Oanda caps each response at
        /// <see cref="MaxCandlesPerRequest"/> candles and offers no page-token concept, so a full
        /// response is treated as a signal that more candles remain: <paramref name="fromUtc"/> is
        /// advanced to just after the last returned candle's timestamp and the chunk is re-requested,
        /// continuing until a short response is seen or <paramref name="toUtc"/> is reached.
        /// </summary>
        public async Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            string granularity = ParseGranularity(interval);

            List<Candle> candles = new();
            DateTime chunkFrom = fromUtc;
            while (chunkFrom < toUtc)
            {
                List<Candle> chunk = await FetchChunkAsync(symbol, granularity, chunkFrom, toUtc, ct).ConfigureAwait(false);
                if (chunk.Count == 0)
                {
                    break;
                }

                candles.AddRange(chunk);
                if (chunk.Count < MaxCandlesPerRequest)
                {
                    break;
                }

                chunkFrom = chunk[^1].Timestamp.AddTicks(1);
            }

            return candles;
        }

        /// <summary>Requests a single chunk of candles covering at most <see cref="MaxCandlesPerRequest"/> candles.</summary>
        private async Task<List<Candle>> FetchChunkAsync(string symbol, string granularity, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            string from = Uri.EscapeDataString(FormatTimestamp(fromUtc));
            string to = Uri.EscapeDataString(FormatTimestamp(toUtc));
            string url = $"{PracticeBaseUrl}/v3/instruments/{Uri.EscapeDataString(symbol)}/candles?price=M&granularity={granularity}&from={from}&to={to}";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);

            using HttpResponseMessage resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Oanda v20 HTTP error {resp.StatusCode}: {text}");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseCandles(json);
        }

        /// <summary>
        /// Parses an Oanda v20 candles JSON payload into candles, reading OHLC from the Mid sub-object
        /// and mapping the per-candle tick-count <c>volume</c> field into <see cref="Candle.Volume"/>.
        /// </summary>
        private static List<Candle> ParseCandles(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement candlesElement = doc.RootElement.GetProperty("candles");

            List<Candle> candles = new();
            foreach (JsonElement candle in candlesElement.EnumerateArray())
            {
                JsonElement mid = candle.GetProperty("mid");
                candles.Add(new Candle
                {
                    Timestamp = ParseTimestamp(candle.GetProperty("time").GetString()),
                    Open = decimal.Parse(mid.GetProperty("o").GetString(), CultureInfo.InvariantCulture),
                    High = decimal.Parse(mid.GetProperty("h").GetString(), CultureInfo.InvariantCulture),
                    Low = decimal.Parse(mid.GetProperty("l").GetString(), CultureInfo.InvariantCulture),
                    Close = decimal.Parse(mid.GetProperty("c").GetString(), CultureInfo.InvariantCulture),
                    Volume = candle.GetProperty("volume").GetDecimal()
                });
            }

            return candles.OrderBy(c => c.Timestamp).ToList();
        }

        /// <summary>Parses Oanda's RFC3339 nanosecond-precision timestamp into a UTC <see cref="DateTime"/>.</summary>
        private static DateTime ParseTimestamp(string text)
        {
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).UtcDateTime;
        }

        /// <summary>Formats a UTC <see cref="DateTime"/> as the RFC3339 timestamp Oanda's <c>from</c>/<c>to</c> query parameters expect.</summary>
        private static string FormatTimestamp(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses an interval string into an Oanda granularity code. The leading digits give the multiple
        /// and the trailing suffix the unit: <c>m</c> minutes, <c>h</c> hours, <c>d</c> days, <c>wk</c>
        /// weeks, <c>mo</c> months — this vocabulary is shared with the Yahoo/Alpaca providers. Oanda's
        /// day/week/month granularities have no multiplier, so only a multiple of 1 is valid for
        /// <c>d</c>/<c>wk</c>/<c>mo</c>. The <c>s</c> (seconds) unit is an Oanda-specific extension not
        /// present in the shared vocabulary, and only <c>5s</c>/<c>10s</c>/<c>15s</c>/<c>30s</c> map to a
        /// granularity, matching Oanda's fixed second-level set. Throws <see cref="NotSupportedException"/>
        /// for anything outside Oanda's fixed granularity set.
        /// </summary>
        private static string ParseGranularity(string interval)
        {
            string trimmed = (interval ?? string.Empty).Trim().ToLowerInvariant();

            int split = 0;
            while (split < trimmed.Length && char.IsDigit(trimmed[split]))
            {
                split++;
            }

            string digits = trimmed.Substring(0, split);
            string suffix = trimmed.Substring(split);

            if (!int.TryParse(digits, out int value) || value <= 0)
            {
                throw Unsupported(interval);
            }

            return (suffix, value) switch
            {
                ("m", 1) => "M1", ("m", 2) => "M2", ("m", 4) => "M4", ("m", 5) => "M5",
                ("m", 10) => "M10", ("m", 15) => "M15", ("m", 30) => "M30",
                ("h", 1) => "H1", ("h", 2) => "H2", ("h", 3) => "H3", ("h", 4) => "H4",
                ("h", 6) => "H6", ("h", 8) => "H8", ("h", 12) => "H12",
                ("d", 1) => "D",
                ("wk", 1) => "W",
                ("mo", 1) => "M",
                ("s", 5) => "S5", ("s", 10) => "S10", ("s", 15) => "S15", ("s", 30) => "S30",
                _ => throw Unsupported(interval)
            };
        }

        /// <summary>Builds the exception thrown for an interval the provider cannot map to an Oanda granularity.</summary>
        private static NotSupportedException Unsupported(string interval)
        {
            return new NotSupportedException(
                $"Oanda provider does not support interval '{interval}'. Supported: M1, M2, M4, M5, M10, M15, M30, " +
                "H1, H2, H3, H4, H6, H8, H12, D (1d), W (1wk), M (1mo), S5 (5s), S10 (10s), S15 (15s), S30 (30s).");
        }
    }
}
