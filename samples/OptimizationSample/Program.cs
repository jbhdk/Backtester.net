using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Sizing;
using Backtester.ExecutionModels.Slippage;
using Backtester.Optimization;
using Backtester.Report;
using Backtester.Report.Toolkit;
using Backtester.Strategies;

namespace OptimizationSample
{
    /// <summary>
    /// End-to-end, offline sample of the Optimizer: it sweeps the two <c>[Optimize]</c>-annotated axes on
    /// <see cref="MovingAverageCrossParameters"/> (the Fast and Slow periods) over the committed CSVs, runs
    /// the Optimizer, writes the Optimization report (leaderboard + heatmap), and writes the winning Trial's
    /// single-run report from <c>Best.BacktestResult</c> with the winning Parameters as configuration cards.
    /// Run by hand — it needs no network access or credentials.
    /// </summary>
    public static class Program
    {
        /// <summary>The folder holding the committed OHLCV files, copied next to the built sample.</summary>
        private const string DataFolder = "data";

        /// <summary>Where the leaderboard-and-heatmap Optimization report is written, relative to the working directory.</summary>
        private const string OptimizationReportPath = "optimization-report.html";

        /// <summary>Where the winning Trial's single-run report is written, relative to the working directory.</summary>
        private const string WinnerReportPath = "winner-report.html";

        /// <summary>The instruments the sweep is run over, read from the committed CSVs.</summary>
        private static readonly string[] Symbols = { "SPY", "QQQ", "GLD", "TLT" };

        /// <summary>The starting equity every Trial's fresh Portfolio is seeded with.</summary>
        private const decimal StartingEquity = 100_000m;

        /// <summary>
        /// The Round-trip floor below which a Trial is ineligible to win. Kept modest so this short sample
        /// sweep reliably produces a winner offline; the package default is higher (see ADR 0020).
        /// </summary>
        private const int MinimumTrades = 5;

        /// <summary>Runs the sweep and writes both reports.</summary>
        public static async Task Main()
        {
            // Resolved against the sample's own folder rather than the working directory, so it runs the
            // same however it is launched.
            IHistoricalDataFetcher fetcher = new CsvHistoricalDataFetcher(
                Path.Combine(AppContext.BaseDirectory, DataFolder));

            // Attributes-first authoring: the [Optimize]-decorated Fast and Slow periods become the two swept
            // axes, and the factory realizes each Parameter set into a fresh strategy and broker.
            OptimizationSetup setup = Optimize
                .For(new MovingAverageCrossParameters(), CreateTrial)
                .FromAttributes();

            Optimizer optimizer = new(
                fetcher,
                Symbols,
                fromUtc: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                toUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                interval: "1d",
                portfolioFactory: () => new Portfolio(StartingEquity),
                setup,
                minimumTrades: MinimumTrades);

            Console.WriteLine("Running the Optimization...");
            Progress<OptimizationProgress> progress = new(update =>
                Console.WriteLine($"  Trial {update.Completed}/{update.Total}"));
            OptimizationResult result = await optimizer.RunAsync(progress, CancellationToken.None);

            WriteOptimizationReport(result);
            WriteWinnerReport(result);
        }

        /// <summary>
        /// The <c>(ParameterSet, freshPortfolio) -> (strategy, broker)</c> factory: builds a moving-average
        /// cross strategy from the swept clone's Fast and Slow periods, over a broker with the fixed execution
        /// models every Trial shares.
        /// </summary>
        private static (IStrategy Strategy, IBrokerSimulator Broker) CreateTrial(
            MovingAverageCrossParameters parameters, Portfolio portfolio)
        {
            IBrokerSimulator broker = new BrokerSimulator(
                portfolio,
                commissionModel: new PerShareCommission { PerShare = 0.005m },
                slippageModel: new FixedSlippage { Amount = 0.01m },
                sizingModel: new FixedSizeModel { FixedSize = 100 });
            IStrategy strategy = new MovingAverageCrossStrategy(parameters.FastPeriod, parameters.SlowPeriod);
            return (strategy, broker);
        }

        /// <summary>Builds the Optimization report model from the ranked Trials and writes it as one self-contained HTML file.</summary>
        private static void WriteOptimizationReport(OptimizationResult result)
        {
            OptimizationReportModel model = new OptimizationReportModelBuilder().Build(result);
            new OptimizationHtmlReportWriter().Write(model, OptimizationReportPath);
            Console.WriteLine($"Wrote {Path.GetFullPath(OptimizationReportPath)}");
        }

        /// <summary>
        /// Writes the winning Trial's single-run report from <c>Best.BacktestResult</c>, rendering the winning
        /// Fast and Slow periods as configuration cards. When no Trial met the minimum-trades floor there is no
        /// winner, so no single-run report is written.
        /// </summary>
        private static void WriteWinnerReport(OptimizationResult result)
        {
            if (result.Best == null)
            {
                Console.WriteLine("No Trial met the minimum-trades floor, so no winner report was written.");
                return;
            }

            // The winning Parameters carried back onto a typed instance so the report toolkit can reflect them
            // into configuration cards — the same [ReportSetting]-attributed properties the sweep varied.
            MovingAverageCrossParameters winning = new()
            {
                FastPeriod = result.Best.Parameters.Int(nameof(MovingAverageCrossParameters.FastPeriod)),
                SlowPeriod = result.Best.Parameters.Int(nameof(MovingAverageCrossParameters.SlowPeriod))
            };

            IReadOnlyList<ReportCard> cards = new ConfigurationCardBuilder().Build(winning);
            new HtmlReportWriter().Write(result.Best.BacktestResult, WinnerReportPath, cards);
            Console.WriteLine($"Wrote {Path.GetFullPath(WinnerReportPath)} " +
                $"(winner: Fast {winning.FastPeriod}, Slow {winning.SlowPeriod}, {result.Best.Stats.Trades} trades).");
        }
    }
}
