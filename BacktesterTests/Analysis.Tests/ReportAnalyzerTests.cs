using System.Threading;
using System.Threading.Tasks;
using Backtester.Analysis;
using Backtester.Report;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Analysis.Tests
{
    /// <summary>
    /// Covers <see cref="ReportAnalyzer"/> through its public entry point, asserting digest content by
    /// capturing the request handed to a faked <see cref="IAnalysisClient"/>.
    /// </summary>
    public class ReportAnalyzerTests
    {
        [Fact]
        public async Task AnalyzeAsync_ReturnsTheClientsAnswerUnaltered()
        {
            IAnalysisClient client = A.Fake<IAnalysisClient>();
            A.CallTo(() => client.AskAsync(A<AnalysisRequest>._, A<CancellationToken>._))
                .Returns(Task.FromResult("the raw answer"));
            ReportAnalyzer analyzer = new ReportAnalyzer(client, new AnalysisOptions { ModelName = "qwen2.5:14b" });

            string answer = await analyzer.AnalyzeAsync(SampleReportModel.Build(), CancellationToken.None);

            Assert.Equal("the raw answer", answer);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesTheRunContext()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("Symbols: AAPL, MSFT", request.UserPrompt);
            Assert.Contains("Interval: 1d", request.UserPrompt);
            Assert.Contains("Range: 2024-01-01 00:00Z to 2024-06-30 00:00Z", request.UserPrompt);
            Assert.Contains("Starting equity: $100,000.00", request.UserPrompt);
            Assert.Contains("Final equity: $112,345.68", request.UserPrompt);
            Assert.Contains("Total return: 12.35%", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesTheCombinedStats()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("| Metric | Combined |", request.UserPrompt);
            Assert.Contains("| Net profit | $12,345.68 |", request.UserPrompt);
            Assert.Contains("| Win rate | 66.67% |", request.UserPrompt);
            Assert.Contains("| Profit factor | 1.62 |", request.UserPrompt);
            Assert.Contains("| Max drawdown | 8.25% |", request.UserPrompt);
            Assert.Contains("| Drawdown length | 63d 5h |", request.UserPrompt);
            Assert.Contains("| Avg duration | 3d 14h |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesThePerSymbolStatsAsColumns()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("| Metric | Combined | AAPL | MSFT |", request.UserPrompt);
            Assert.Contains("| Net profit | $12,345.68 | $9,000.50 | $3,345.18 |", request.UserPrompt);
            Assert.Contains("| Win rate | 66.67% | 100.00% | 0.00% |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesTheRoundTrips()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("| # | Symbol | Side | Entry | Exit | Entry price | Exit price | Exit reason | Qty | P&L | Return % | Time held |", request.UserPrompt);
            Assert.Contains("| 1 | AAPL | Long | 2024-01-02 14:30Z | 2024-01-05 20:00Z | 185.13 | 192.50 | Take-profit | 100 | $737.50 | 3.98% | 3d 5h |", request.UserPrompt);
            Assert.Contains("| 3 | MSFT | Short | 2024-03-04 14:30Z | 2024-03-13 15:30Z | 402.75 | 407.26 | Stop-loss | 100 | -$450.50 | -1.12% | 9d 1h |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesTheRejectedOrders()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("## Rejected orders", request.UserPrompt);
            Assert.Contains("| MSFT | Long | 2024-04-08 14:30Z | 421.38 | 250 | Not enough funds |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestCarriesTheAttachedConfigurationCards()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.Contains("## Configuration", request.UserPrompt);
            Assert.Contains("### MACD", request.UserPrompt);
            Assert.Contains("| Setting | Value |", request.UserPrompt);
            Assert.Contains("| Fast period | 12 |", request.UserPrompt);
            Assert.Contains("| Slow period | 26 |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestOmitsCandlesIndicatorsAndTheEquityCurve()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.DoesNotContain("186.50", request.UserPrompt);
            Assert.DoesNotContain("1704153600", request.UserPrompt);
            Assert.DoesNotContain("RSI", request.UserPrompt);
            Assert.DoesNotContain("987.75", request.UserPrompt);
            Assert.DoesNotContain("$100,737.50", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_DigestNeverCarriesRawDecimalPrecision()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.DoesNotContain("0.66666667", request.UserPrompt);
            Assert.DoesNotContain("12345.678", request.UserPrompt);
            Assert.DoesNotContain("0.03983794", request.UserPrompt);
            Assert.DoesNotContain("185.125", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_CallerGuidanceReachesTheRequest()
        {
            AnalysisOptions options = new AnalysisOptions { Guidance = "Mean-reversion; judge the stop, not the entry." };

            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), options);

            Assert.Contains("## Guidance", request.UserPrompt);
            Assert.Contains("Mean-reversion; judge the stop, not the entry.", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_NoGuidanceSectionWhenTheCallerSuppliesNone()
        {
            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.Build(), new AnalysisOptions());

            Assert.DoesNotContain("## Guidance", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_OverTheDefaultCap_ThrowsNamingBothCounts()
        {
            ReportAnalyzer analyzer = new ReportAnalyzer(A.Fake<IAnalysisClient>(), new AnalysisOptions());

            AnalysisDigestOverflowException exception = await Assert.ThrowsAsync<AnalysisDigestOverflowException>(
                () => analyzer.AnalyzeAsync(SampleReportModel.BuildWithRoundTrips(501), CancellationToken.None));

            Assert.Contains("501", exception.Message);
            Assert.Contains("500", exception.Message);
            Assert.Contains("sampling", exception.Message);
        }

        [Fact]
        public async Task AnalyzeAsync_OverTheCap_NeverAsksTheClient()
        {
            IAnalysisClient client = A.Fake<IAnalysisClient>();
            ReportAnalyzer analyzer = new ReportAnalyzer(client, new AnalysisOptions { RoundTripCap = 2 });

            await Assert.ThrowsAsync<AnalysisDigestOverflowException>(
                () => analyzer.AnalyzeAsync(SampleReportModel.Build(), CancellationToken.None));

            A.CallTo(() => client.AskAsync(A<AnalysisRequest>._, A<CancellationToken>._)).MustNotHaveHappened();
        }

        [Fact]
        public async Task AnalyzeAsync_WithSampling_KeepsEvenlySpacedRoundTripsSpanningTheRun()
        {
            AnalysisOptions options = new AnalysisOptions
            {
                RoundTripCap = 3,
                OverflowPolicy = AnalysisOverflowPolicy.Sample
            };

            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.BuildWithRoundTrips(9), options);

            Assert.Contains("| 1 | AAPL |", request.UserPrompt);
            Assert.Contains("| 5 | AAPL |", request.UserPrompt);
            Assert.Contains("| 9 | AAPL |", request.UserPrompt);
            Assert.DoesNotContain("| 2 | AAPL |", request.UserPrompt);
            Assert.DoesNotContain("| 8 | AAPL |", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_WithSampling_DigestDeclaresItsOwnSampling()
        {
            AnalysisOptions options = new AnalysisOptions
            {
                RoundTripCap = 3,
                OverflowPolicy = AnalysisOverflowPolicy.Sample
            };

            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.BuildWithRoundTrips(9), options);

            Assert.Contains("3 of 9 round trips", request.UserPrompt);
            Assert.Contains("evenly spaced across the run", request.UserPrompt);
        }

        [Fact]
        public async Task AnalyzeAsync_UnderTheCap_DigestDeclaresNoSampling()
        {
            AnalysisOptions options = new AnalysisOptions
            {
                RoundTripCap = 3,
                OverflowPolicy = AnalysisOverflowPolicy.Sample
            };

            AnalysisRequest request = await CaptureRequestAsync(SampleReportModel.BuildWithRoundTrips(3), options);

            Assert.DoesNotContain("of 3 round trips", request.UserPrompt);
            Assert.DoesNotContain("evenly spaced across the run", request.UserPrompt);
        }

        /// <summary>
        /// Runs the analyzer against a faked client and returns the request it was handed. This is the
        /// only seam the digest is asserted through — the renderer itself stays private.
        /// </summary>
        private static async Task<AnalysisRequest> CaptureRequestAsync(ReportModel model, AnalysisOptions options)
        {
            AnalysisRequest captured = null;
            IAnalysisClient client = A.Fake<IAnalysisClient>();
            A.CallTo(() => client.AskAsync(A<AnalysisRequest>._, A<CancellationToken>._))
                .Invokes((AnalysisRequest request, CancellationToken _) => captured = request)
                .Returns(Task.FromResult("{}"));

            ReportAnalyzer analyzer = new ReportAnalyzer(client, options);
            await analyzer.AnalyzeAsync(model, CancellationToken.None);

            return captured;
        }
    }
}
