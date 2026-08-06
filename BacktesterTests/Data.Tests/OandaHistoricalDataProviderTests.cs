using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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

        // A single candle with distinct mid/bid/ask sub-objects, so a test can prove which one was read.
        private const string MidBidAskCandleJson = @"{
  ""candles"": [
    {
      ""complete"": true,
      ""volume"": 10,
      ""time"": ""2021-05-03T00:00:00.000000000Z"",
      ""mid"": { ""o"": ""1.20000"", ""h"": ""1.20150"", ""l"": ""1.19950"", ""c"": ""1.20100"" },
      ""bid"": { ""o"": ""1.19900"", ""h"": ""1.20050"", ""l"": ""1.19850"", ""c"": ""1.20000"" },
      ""ask"": { ""o"": ""1.20100"", ""h"": ""1.20250"", ""l"": ""1.20050"", ""c"": ""1.20200"" }
    }
  ],
  ""granularity"": ""H1"",
  ""instrument"": ""EUR_USD""
}";

        [Theory]
        [InlineData(OandaEnvironment.Practice, "https://api-fxpractice.oanda.com")]
        [InlineData(OandaEnvironment.Live, "https://api-fxtrade.oanda.com")]
        public async Task FetchAsync_Environment_RequestsMatchingHost(OandaEnvironment environment, string expectedHost)
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token", environment: environment);

            await provider.FetchAsync("EUR_USD", From, To, "1h");

            Assert.StartsWith($"{expectedHost}/v3/instruments/", stub.LastRequestUri);
        }

        [Theory]
        [InlineData(PriceComponent.Bid, "B", 1.19900)]
        [InlineData(PriceComponent.Ask, "A", 1.20100)]
        public async Task FetchAsync_NonDefaultPriceComponent_RequestsMatchingParamAndReadsMatchingSubObject(
            PriceComponent priceComponent, string expectedQueryParam, decimal expectedOpen)
        {
            StubHttpHandler stub = new(MidBidAskCandleJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token", priceComponent);

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", From, To, "1h"));

            Assert.Contains($"price={expectedQueryParam}", stub.LastRequestUri);
            Assert.Equal(expectedOpen, Assert.Single(candles).Open);
        }

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
        public async Task FetchAsync_RequestsCandlesEndpoint_FromRangeStart()
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", From, To, "1h");

            Assert.Contains("from=2021-05-03T00%3A00%3A00", stub.LastRequestUri);
        }

        /// <summary>
        /// Pages are bounded by count, never by the caller's range end. Oanda derives an implied count from a
        /// from/to span and rejects the request outright once that span exceeds the cap, so sending the
        /// caller's own end makes every wide range fail on its very first request.
        /// </summary>
        [Fact]
        public async Task FetchAsync_RequestsCandlesEndpoint_WithCountCapAndNoRangeEnd()
        {
            StubHttpHandler stub = new(EmptyCandlesJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", From, To, "1h");

            Assert.Contains("count=5000", stub.LastRequestUri);
            Assert.DoesNotContain("to=", stub.LastRequestUri);
        }

        /// <summary>
        /// A range far wider than the cap is still requested a page at a time, so it never trips Oanda's
        /// implied-count limit. Six years of hourly bars is the case that motivated paging by count.
        /// </summary>
        [Fact]
        public async Task FetchAsync_RangeFarWiderThanTheCap_KeepsEveryRequestWithinTheCap()
        {
            DateTime baseTime = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            SequencedStubHttpHandler stub = new(
                BuildCandlesJson(baseTime, count: 5000, stepMinutes: 60),
                BuildCandlesJson(baseTime.AddHours(5000), count: 3, stepMinutes: 60));
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            await provider.FetchAsync("EUR_USD", baseTime, baseTime.AddYears(6), "1h");

            Assert.All(stub.RequestUris, uri => Assert.Contains("count=5000", uri));
            Assert.All(stub.RequestUris, uri => Assert.DoesNotContain("to=", uri));
        }

        /// <summary>
        /// Paging by count overshoots the caller's range end, so candles past it are dropped rather than
        /// returned — the caller asked for a range, not for whole pages.
        /// </summary>
        [Fact]
        public async Task FetchAsync_PageOvershootsRangeEnd_TrimsCandlesPastIt()
        {
            DateTime baseTime = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            StubHttpHandler stub = new(BuildCandlesJson(baseTime, count: 10, stepMinutes: 60));
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", baseTime, baseTime.AddHours(4), "1h"));

            Assert.Equal(4, candles.Count);
            Assert.Equal(baseTime.AddHours(3), candles[^1].Timestamp);
        }

        /// <summary>
        /// A full page that already reaches the range end ends the walk. Asking again would return the same
        /// final page for ever, since every candle past the end is trimmed away.
        /// </summary>
        [Fact]
        public async Task FetchAsync_FullPageReachesRangeEnd_StopsRequesting()
        {
            DateTime baseTime = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            SequencedStubHttpHandler stub = new(BuildCandlesJson(baseTime, count: 5000, stepMinutes: 1));
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", baseTime, baseTime.AddMinutes(10), "1m"));

            Assert.Single(stub.RequestUris);
            Assert.Equal(10, candles.Count);
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

        [Fact]
        public async Task FetchAsync_FirstChunkReturnsFull5000Candles_RequestsAndReturnsFollowUpChunkToo()
        {
            DateTime baseTime = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string firstChunkJson = BuildCandlesJson(baseTime, count: 5000, stepMinutes: 1);
            DateTime secondChunkStart = baseTime.AddMinutes(5000);
            string secondChunkJson = BuildCandlesJson(secondChunkStart, count: 1, stepMinutes: 1);

            SequencedStubHttpHandler stub = new(firstChunkJson, secondChunkJson);
            OandaHistoricalDataProvider provider = new(new HttpClient(stub), "test-token");

            List<Candle> candles = new(await provider.FetchAsync("EUR_USD", baseTime, baseTime.AddYears(1), "1m"));

            Assert.Equal(2, stub.RequestUris.Count);
            Assert.Equal(5001, candles.Count);
            Assert.Equal(5001, candles.Select(c => c.Timestamp).Distinct().Count());
            Assert.Equal(baseTime, candles[0].Timestamp);
            Assert.Equal(secondChunkStart, candles[^1].Timestamp);
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

        /// <summary>Builds a candles JSON payload with <paramref name="count"/> candles, one-minute-granularity metadata, starting at <paramref name="start"/> and stepping by <paramref name="stepMinutes"/> minutes.</summary>
        private static string BuildCandlesJson(DateTime start, int count, int stepMinutes)
        {
            StringBuilder sb = new();
            sb.Append(@"{ ""candles"": [");
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                string time = start.AddMinutes(i * stepMinutes).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
                sb.Append(@"{ ""complete"": true, ""volume"": 1, ""time"": """).Append(time)
                  .Append(@""", ""mid"": { ""o"": ""1.0"", ""h"": ""1.0"", ""l"": ""1.0"", ""c"": ""1.0"" } }");
            }

            sb.Append(@"], ""granularity"": ""M1"", ""instrument"": ""EUR_USD"" }");
            return sb.ToString();
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

        /// <summary>Returns each given response body in order on successive requests, repeating the last body for any extra calls; records every request URI.</summary>
        private class SequencedStubHttpHandler : HttpMessageHandler
        {
            private readonly string[] _bodies;
            private int _index;

            /// <summary>Gets the URI strings of every request sent through this handler, in order.</summary>
            public List<string> RequestUris { get; } = new();

            public SequencedStubHttpHandler(params string[] bodies)
            {
                _bodies = bodies;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                RequestUris.Add(request.RequestUri?.ToString());
                string body = _bodies[Math.Min(_index, _bodies.Length - 1)];
                _index++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            }
        }
    }
}
