using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using Backtester.Core;

namespace Backtester.Data.Alpaca
{
    /// <summary>
    /// Fetches historical OHLCV candle data for US equities from Alpaca using the Alpaca.Markets SDK.
    /// Pure acquisition: it performs no caching and touches no disk; caching is handled by the fetcher.
    /// </summary>
    public class AlpacaHistoricalDataProvider : IHistoricalDataProvider
    {
        private readonly IAlpacaDataClient _client;
        private readonly MarketDataFeed _feed;
        private readonly Adjustment _adjustment;

        /// <summary>
        /// Initializes a new provider over the given Alpaca data client. The market data <paramref name="feed"/>
        /// and price <paramref name="adjustment"/> default to the values that make a bar-by-bar backtest
        /// correct — consolidated SIP and split-adjusted (see docs/adr/0010) — and are overridable.
        /// </summary>
        public AlpacaHistoricalDataProvider(
            IAlpacaDataClient client,
            MarketDataFeed feed = MarketDataFeed.Sip,
            Adjustment adjustment = Adjustment.SplitsOnly)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _feed = feed;
            _adjustment = adjustment;
        }

        /// <summary>
        /// Fetches candles for the symbol from Alpaca's historical bars endpoint and maps them to <see cref="Candle"/>.
        /// </summary>
        public async Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            HistoricalBarsRequest request = new(symbol, fromUtc, toUtc, ParseTimeFrame(interval))
            {
                Feed = _feed,
                Adjustment = _adjustment
            };

            // Pull the largest page Alpaca allows to minimize round-trips over a wide range.
            request.Pagination.Size = Pagination.MaxPageSize;

            List<Candle> candles = new();

            // Alpaca returns a single capped page per call; walk the NextPageToken until the range is
            // exhausted, accumulating every page's bars. There is no cap on the total number of bars.
            IPage<IBar> page;
            do
            {
                page = await _client.ListHistoricalBarsAsync(request, ct).ConfigureAwait(false);
                foreach (IBar bar in page.Items)
                {
                    candles.Add(ToCandle(bar));
                }

                request.Pagination.Token = page.NextPageToken;
            }
            while (!string.IsNullOrEmpty(page.NextPageToken));

            candles.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            return candles;
        }

        /// <summary>
        /// Parses an interval string (shared with the Yahoo provider's vocabulary) into an Alpaca
        /// <see cref="BarTimeFrame"/>. The leading digits give the multiple and the trailing suffix the
        /// unit: <c>m</c> minutes, <c>h</c> hours, <c>d</c> days, <c>wk</c> weeks, <c>mo</c> months.
        /// Throws <see cref="NotSupportedException"/> for anything it cannot parse.
        /// </summary>
        private static BarTimeFrame ParseTimeFrame(string interval)
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

            return suffix switch
            {
                "m"  => new BarTimeFrame(value, BarTimeFrameUnit.Minute),
                "h"  => new BarTimeFrame(value, BarTimeFrameUnit.Hour),
                "d"  => new BarTimeFrame(value, BarTimeFrameUnit.Day),
                "wk" => new BarTimeFrame(value, BarTimeFrameUnit.Week),
                "mo" => new BarTimeFrame(value, BarTimeFrameUnit.Month),
                _    => throw Unsupported(interval)
            };
        }

        /// <summary>Builds the exception thrown for an interval the provider cannot parse.</summary>
        private static NotSupportedException Unsupported(string interval)
        {
            return new NotSupportedException(
                $"Alpaca provider does not support interval '{interval}'. Supported suffixes: m, h, d, wk, mo (e.g. 5m, 1h, 1d).");
        }

        /// <summary>Maps an Alpaca <see cref="IBar"/> to a <see cref="Candle"/> (plain OHLCV, dropping Vwap and TradeCount).</summary>
        private static Candle ToCandle(IBar bar)
        {
            return new Candle
            {
                Timestamp = bar.TimeUtc,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume
            };
        }
    }
}
