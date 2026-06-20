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

        /// <summary>
        /// Initializes a new provider over the given Alpaca data client.
        /// </summary>
        public AlpacaHistoricalDataProvider(IAlpacaDataClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Fetches candles for the symbol from Alpaca's historical bars endpoint and maps them to <see cref="Candle"/>.
        /// </summary>
        public async Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            // Feed, adjustment, and timeframe are hard-coded for this first slice; later slices make
            // the interval drive the timeframe and expose feed/adjustment as overridable options.
            HistoricalBarsRequest request = new(symbol, fromUtc, toUtc, new BarTimeFrame(1, BarTimeFrameUnit.Day))
            {
                Feed = MarketDataFeed.Sip,
                Adjustment = Adjustment.SplitsOnly
            };

            IPage<IBar> page = await _client.ListHistoricalBarsAsync(request, ct).ConfigureAwait(false);

            List<Candle> candles = new();
            foreach (IBar bar in page.Items)
            {
                candles.Add(ToCandle(bar));
            }

            candles.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            return candles;
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
