using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data.Oanda;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class OandaHistoricalDataProviderTests
    {
        private static readonly DateTime From = new(2021, 5, 3, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime To   = new(2021, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        private const string EmptyCandlesJson = @"{
  ""candles"": [],
  ""granularity"": ""H1"",
  ""instrument"": ""EUR_USD""
}";

        // Candles deliberately returned out of order to prove the provider sorts ascending.
        private const string TwoCandlesJson = @"{
  ""candles"": [
    {
      ""complete"": true,
      ""volume"": 55,
      ""time"": ""2021-05-03T01:00:00.000000000Z"",
      ""mid"": { ""o"": ""1.20100"", ""h"": ""1.20300"", ""l"": ""1.20000"", ""c"": ""1.20250"" }
    },
    {
      ""complete"": true,
      ""volume"": 42,
      ""time"": ""2021-05-03T00:00:00.000000000Z"",
      ""mid"": { ""o"": ""1.20000"", ""h"": ""1.20150"", ""l"": ""1.19950"", ""c"": ""1.20100"" }
    }
  ],
  ""granularity"": ""H1"",
  ""instrument"": ""EUR_USD""
}";

        [Fact]
        public async Task FetchAsync_RequestsPracticeCandlesEndpoint_WithInstrumentAndMidPrice()
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", From, To, "1h");

            Assert.Contains("https://api-fxpractice.oanda.com/v3/instruments/EUR_USD/candles", stub.LastRequestUri);
            Assert.Contains("price=M", stub.LastRequestUri);
        }

        [Fact]
        public async Task FetchAsync_RequestsCandlesEndpoint_WithFromAndToRange()
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", From, To, "1h");

            Assert.Contains("from=2021-05-03T00%3A00%3A00", stub.LastRequestUri);
            Assert.Contains("to=2021-05-04T00%3A00%3A00", stub.LastRequestUri);
        }

        [Fact]
        public async Task FetchAsync_CandlesPayload_MapsMidPricesAndTickVolume()
        {
            StubHttpHandler stub = new(TwoCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", From, To, "1h"));

            Candle first = candles[0];
            Assert.Equal(new DateTime(2021, 5, 3, 0, 0, 0, DateTimeKind.Utc), first.Timestamp);
            Assert.Equal(1.20000m, first.Open);
            Assert.Equal(1.20150m, first.High);
            Assert.Equal(1.19950m, first.Low);
            Assert.Equal(1.20100m, first.Close);
            Assert.Equal(42m, first.Volume);
        }

        [Fact]
        public async Task FetchAsync_CandlesOutOfOrderInPayload_ReturnsSortedAscendingByTimestamp()
        {
            StubHttpHandler stub = new(TwoCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", From, To, "1h"));

            Assert.Equal(2, candles.Count);
            Assert.True(candles[0].Timestamp < candles[1].Timestamp);
        }

        [Theory]
        [InlineData("1m", "M1")]
        [InlineData("2m", "M2")]
        [InlineData("4m", "M4")]
        [InlineData("5m", "M5")]
        [InlineData("10m", "M10")]
        [InlineData("15m", "M15")]
        [InlineData("30m", "M30")]
        [InlineData("1h", "H1")]
        [InlineData("2h", "H2")]
        [InlineData("3h", "H3")]
        [InlineData("4h", "H4")]
        [InlineData("6h", "H6")]
        [InlineData("8h", "H8")]
        [InlineData("12h", "H12")]
        [InlineData("1d", "D")]
        [InlineData("1wk", "W")]
        [InlineData("1mo", "M")]
        [InlineData("5s", "S5")]
        [InlineData("10s", "S10")]
        [InlineData("15s", "S15")]
        [InlineData("30s", "S30")]
        public async Task FetchAsync_SupportedInterval_RequestsMatchingGranularity(string interval, string granularity)
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", From, To, interval);

            Assert.Contains($"granularity={granularity}", stub.LastRequestUri);
        }

        [Theory]
        [InlineData("7m")]
        [InlineData("7h")]
        [InlineData("2d")]
        [InlineData("2wk")]
        [InlineData("2mo")]
        [InlineData("1s")]
        [InlineData("20s")]
        [InlineData("45s")]
        [InlineData("bogus")]
        public async Task FetchAsync_UnsupportedInterval_ThrowsNotSupportedException(string interval)
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await Assert.ThrowsAsync<NotSupportedException>(
                () => provider.FetchAsync("EUR_USD", From, To, interval));
        }

        [Fact]
        public async Task FetchAsync_HttpError_ThrowsInvalidOperationExceptionWithStatusAndBody()
        {
            StubHttpHandler stub = new(@"{""errorMessage"":""Invalid token""}", HttpStatusCode.Unauthorized);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.FetchAsync("EUR_USD", From, To, "1h"));

            Assert.Contains("Unauthorized", ex.Message);
            Assert.Contains("Invalid token", ex.Message);
        }

        [Fact(Skip = "Requires network and a valid Oanda Practice API token — run manually to smoke-test the live v20 endpoint")]
        public async Task FetchAsync_LiveEurUsd1h_ReturnsCandles()
        {
            string apiToken = Environment.GetEnvironmentVariable("OANDA_API_TOKEN");
            OandaHistoricalDataProvider provider = new(apiToken);
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-5);

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", from, to, "1h"));

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
