using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data.Yahoo;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class YahooHistoricalDataProviderTests
    {
        private static readonly DateTime From = new(2021, 5, 3, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime To   = new(2021, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        // Minimal valid v8 JSON with two hourly bars.
        // timestamp[0]=1620000000 → 2021-05-03 00:00:00 UTC
        // timestamp[1]=1620003600 → 2021-05-03 01:00:00 UTC
        private const string TwoBarJson = @"{
  ""chart"": {
    ""result"": [{
      ""timestamp"": [1620000000, 1620003600],
      ""indicators"": {
        ""quote"": [{
          ""open"":   [420.10, 421.20],
          ""high"":   [422.50, 423.60],
          ""low"":    [418.30, 419.40],
          ""close"":  [421.80, 422.90],
          ""volume"": [1000000, 2000000]
        }]
      }
    }],
    ""error"": null
  }
}";

        [Fact]
        public async Task FetchAsync_RequestsV8ChartEndpoint_WithSymbol()
        {
            StubHttpHandler stub = new(TwoBarJson);
            YahooHistoricalDataProvider provider = new(new HttpClient(stub));

            await provider.FetchAsync("SPY", From, To, "1h");

            Assert.Contains("v8/finance/chart/SPY", stub.LastRequestUri);
        }

        [Fact]
        public async Task FetchAsync_V8JsonPayload_ParsesIntoOrderedCandles()
        {
            StubHttpHandler stub = new(TwoBarJson);
            YahooHistoricalDataProvider provider = new(new HttpClient(stub));

            List<Candle> candles = new(await provider.FetchAsync("SPY", From, To, "1h"));

            Assert.Equal(2, candles.Count);

            Candle first = candles[0];
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1620000000).UtcDateTime, first.Timestamp);
            Assert.Equal(420.10m, first.Open);
            Assert.Equal(422.50m, first.High);
            Assert.Equal(418.30m, first.Low);
            Assert.Equal(421.80m, first.Close);
            Assert.Equal(1_000_000m, first.Volume);

            Candle second = candles[1];
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1620003600).UtcDateTime, second.Timestamp);
        }

        [Fact]
        public async Task FetchAsync_NullEntryInPayload_SkipsRow()
        {
            string jsonWithNull = @"{
  ""chart"": {
    ""result"": [{
      ""timestamp"": [1620000000, 1620003600],
      ""indicators"": {
        ""quote"": [{
          ""open"":   [null, 421.20],
          ""high"":   [null, 423.60],
          ""low"":    [null, 419.40],
          ""close"":  [null, 422.90],
          ""volume"": [null, 2000000]
        }]
      }
    }],
    ""error"": null
  }
}";
            StubHttpHandler stub = new(jsonWithNull);
            YahooHistoricalDataProvider provider = new(new HttpClient(stub));

            List<Candle> candles = new(await provider.FetchAsync("SPY", From, To, "1h"));

            Assert.Single(candles);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1620003600).UtcDateTime, candles[0].Timestamp);
        }

        [Fact]
        public async Task FetchAsync_HttpError_ThrowsInvalidOperationException()
        {
            StubHttpHandler stub = new("{}", HttpStatusCode.Unauthorized);
            YahooHistoricalDataProvider provider = new(new HttpClient(stub));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.FetchAsync("SPY", From, To, "1h"));
        }

        [Fact(Skip = "Requires network — run manually to smoke-test live Yahoo v8 endpoint")]
        public async Task FetchAsync_LiveSpy1h_ReturnsCandles()
        {
            YahooHistoricalDataProvider provider = new();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-5);

            List<Candle> candles = new(await provider.FetchAsync("SPY", from, to, "1h"));

            Assert.NotEmpty(candles);
            Assert.All(candles, c => Assert.True(c.Close > 0m));
        }

        /// <summary>Returns a fixed response body for every HTTP request; records the last request URI.</summary>
        private class StubHttpHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            /// <summary>Gets the URI string of the most recent request sent through this handler.</summary>
            public string LastRequestUri { get; private set; }

            public StubHttpHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
            {
                _body = body;
                _status = status;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                LastRequestUri = request.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body)
                });
            }
        }
    }
}
