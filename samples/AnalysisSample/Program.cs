using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Analysis;
using Backtester.Analysis.Claude;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Sizing;
using Backtester.ExecutionModels.Slippage;
using Backtester.Report;
using Backtester.Report.Toolkit;
using Backtester.Strategies;

namespace AnalysisSample
{
    /// <summary>
    /// End-to-end sample: runs a backtest, builds the report model, attaches the configuration cards,
    /// asks Claude for an Analysis, and writes a report containing it. Run by hand — it calls the
    /// Claude API and needs <c>ANTHROPIC_API_KEY</c> in the environment.
    /// </summary>
    public static class Program
    {
        /// <summary>The model asked for the Analysis. The documented floor is Sonnet-class.</summary>
        private const string ModelName = "claude-sonnet-5";

        /// <summary>Where the report is written, relative to the working directory.</summary>
        private const string ReportPath = "analysis-report.html";

        /// <summary>The folder holding the committed OHLCV files, copied next to the built sample.</summary>
        private const string DataFolder = "data";

        /// <summary>Runs the backtest, asks for the Analysis, and writes the report.</summary>
        public static async Task Main()
        {
            SampleSettings settings = new()
            {
                FastPeriod = 20,
                SlowPeriod = 100,
                OrderSize = 100,
                CommissionPerShare = 0.005m,
                SlippagePerShare = 0.01m,
                StartingEquity = 100_000m
            };

            BacktestResult result = await RunBacktestAsync(settings);

            // The analysis path is model-first and explicit. Build the model, attach the configuration,
            // then analyse: the digest carries the configuration, so a card attached after the Analyzer
            // has run is a setting the Analyzer was never shown and therefore cannot comment on.
            ReportModel model = new ReportModelBuilder().Build(result);
            model.Configuration = new ConfigurationCardBuilder().Build(settings);

            model.Analysis = await AnalyzeAsync(model);

            new HtmlReportWriter().Write(model, ReportPath);
            Console.WriteLine($"Wrote {Path.GetFullPath(ReportPath)}" +
                (model.Analysis == null ? " without an Analysis." : " with an Analysis."));
        }

        /// <summary>
        /// Runs the backtest the report is built from: a moving-average cross over four ETFs, read from
        /// the committed CSV files so the run is deterministic and needs no data provider or its
        /// credential. The Claude call is the sample's only outbound call.
        /// </summary>
        private static async Task<BacktestResult> RunBacktestAsync(SampleSettings settings)
        {
            Portfolio portfolio = new(settings.StartingEquity);
            IBrokerSimulator broker = new BrokerSimulator(
                portfolio,
                commissionModel: new PerShareCommission { PerShare = settings.CommissionPerShare },
                slippageModel: new FixedSlippage { Amount = settings.SlippagePerShare },
                sizingModel: new FixedSizeModel { FixedSize = settings.OrderSize });

            // Resolved against the sample's own folder rather than the working directory, so it runs
            // the same however it is launched.
            IHistoricalDataFetcher fetcher = new CsvHistoricalDataFetcher(
                Path.Combine(AppContext.BaseDirectory, DataFolder));
            IStrategy strategy = new MovingAverageCrossStrategy(settings.FastPeriod, settings.SlowPeriod);

            IEngine engine = new Engine(
                fetcher,
                symbols: new[] { "SPY", "QQQ", "GLD", "TLT" },
                fromUtc: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                toUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                interval: "1d",
                strategy,
                broker,
                portfolio);

            Console.WriteLine("Running the backtest...");
            return await engine.StartAsync();
        }

        /// <summary>
        /// Asks Claude for an Analysis of the supplied model, returning null when the run gets none. A
        /// malformed answer is rejected rather than repaired, and a report without an Analysis simply
        /// renders no Analysis section — so a failure here costs the section, never the report.
        /// </summary>
        private static async Task<ReportAnalysis> AnalyzeAsync(ReportModel model)
        {
            IAnalysisClient client = new ClaudeAnalysisClient(ModelName);
            AnalysisOptions options = new()
            {
                ModelName = ModelName,
                Guidance = "This is a long/short trend-following run on ETFs. I care most about " +
                    "whether the returns come from the strategy or from one instrument's regime."
            };

            ReportAnalyzer analyzer = new(client, options);

            Console.WriteLine($"Asking {ModelName} for an Analysis...");
            try
            {
                return await analyzer.AnalyzeAsync(model, CancellationToken.None);
            }
            catch (AnalysisFormatException exception)
            {
                Console.WriteLine("The Analysis was rejected: " + exception.Message);
                return null;
            }
            catch (AnalysisDigestOverflowException exception)
            {
                Console.WriteLine("The digest was rejected: " + exception.Message);
                return null;
            }
            catch (ClaudeAnalysisException exception)
            {
                Console.WriteLine("Claude could not answer: " + exception.Message);
                return null;
            }
        }
    }
}
