using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// Renders an Analysis digest as compact markdown. Stays behind the <see cref="ReportAnalyzer"/>
    /// seam: the digest is asserted by capturing what the analyzer hands its client.
    /// </summary>
    internal class AnalysisDigestRenderer
    {
        private static readonly IReadOnlyList<DigestStatsMetric> Metrics = BuildMetrics();

        /// <summary>Renders the supplied digest as compact markdown.</summary>
        public string Render(AnalysisDigest digest)
        {
            StringBuilder markdown = new();
            AppendRun(markdown, digest.Run);
            AppendConfiguration(markdown, digest.Configuration);
            AppendStats(markdown, digest.Stats, digest.StatsBySymbol);
            AppendRoundTrips(markdown, digest);
            AppendRejectedOrders(markdown, digest.RejectedOrders);
            return markdown.ToString();
        }

        private static void AppendRun(StringBuilder markdown, ReportRunInfo run)
        {
            if (run == null)
            {
                return;
            }

            markdown.AppendLine("## Run");
            markdown.AppendLine("Symbols: " + string.Join(", ", run.Symbols));
            markdown.AppendLine("Interval: " + run.Interval);
            markdown.AppendLine("Range: " + DigestValueFormatter.Timestamp(run.FromUtc) + " to " + DigestValueFormatter.Timestamp(run.ToUtc));
            markdown.AppendLine("Starting equity: " + DigestValueFormatter.Money(run.StartingEquity));
            markdown.AppendLine("Final equity: " + DigestValueFormatter.Money(run.FinalEquity));
            markdown.AppendLine("Total return: " + DigestValueFormatter.Percent(run.TotalReturnPercent));
            markdown.AppendLine();
        }

        /// <summary>
        /// Appends the caller's configuration cards. Every cell is opaque display text the caller already
        /// formatted, so it is passed through exactly as the report renders it.
        /// </summary>
        private static void AppendConfiguration(StringBuilder markdown, IReadOnlyList<ReportCard> configuration)
        {
            if (configuration == null || configuration.Count == 0)
            {
                return;
            }

            markdown.AppendLine("## Configuration");

            foreach (ReportCard card in configuration)
            {
                if (card.Rows == null || card.Rows.Count == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(card.Title))
                {
                    markdown.AppendLine("### " + card.Title);
                }

                if (card.Headers != null && card.Headers.Count > 0)
                {
                    markdown.AppendLine(Row(card.Headers));
                    markdown.AppendLine("| " + string.Join(" | ", new List<string>(card.Headers).ConvertAll(_ => "---")) + " |");
                }

                foreach (IReadOnlyList<string> row in card.Rows)
                {
                    markdown.AppendLine(Row(row));
                }

                markdown.AppendLine();
            }
        }

        private static string Row(IReadOnlyList<string> cells)
        {
            return "| " + string.Join(" | ", cells) + " |";
        }

        // Key: symbol/ticker -> that symbol's standalone stats, rendered as its own column beside Combined.
        private static void AppendStats(StringBuilder markdown, ReportStats stats, IReadOnlyDictionary<string, ReportStats> statsBySymbol)
        {
            if (stats == null)
            {
                return;
            }

            List<string> symbols = new();
            if (statsBySymbol != null)
            {
                symbols.AddRange(statsBySymbol.Keys);
            }

            markdown.AppendLine("## Performance");
            markdown.AppendLine("| Metric | Combined |" + string.Concat(symbols.ConvertAll(symbol => " " + symbol + " |")));
            markdown.AppendLine("| --- | --- |" + string.Concat(symbols.ConvertAll(_ => " --- |")));

            foreach (DigestStatsMetric metric in Metrics)
            {
                StringBuilder row = new();
                row.Append("| ").Append(metric.Label).Append(" | ").Append(metric.ValueOf(stats)).Append(" |");
                foreach (string symbol in symbols)
                {
                    row.Append(' ').Append(metric.ValueOf(statsBySymbol[symbol])).Append(" |");
                }

                markdown.AppendLine(row.ToString());
            }

            markdown.AppendLine();
        }

        /// <summary>
        /// Appends the round trips. A sampled digest states its own sampling first, so the AI is told
        /// explicitly that it is reasoning over part of a run.
        /// </summary>
        private static void AppendRoundTrips(StringBuilder markdown, AnalysisDigest digest)
        {
            IReadOnlyList<ReportRoundTrip> roundTrips = digest.RoundTrips;
            if (roundTrips == null || roundTrips.Count == 0)
            {
                return;
            }

            markdown.AppendLine("## Round trips");

            if (digest.IsSampled)
            {
                markdown.AppendLine("This is a sample, not the whole run: " +
                    roundTrips.Count.ToString(CultureInfo.InvariantCulture) + " of " +
                    digest.TotalRoundTrips.ToString(CultureInfo.InvariantCulture) + " round trips, " +
                    digest.SelectionBasis + ". Do not state whole-run conclusions from it.");
            }

            markdown.AppendLine("| # | Symbol | Side | Entry | Exit | Entry price | Exit price | Exit reason | Qty | P&L | Return % | Time held |");
            markdown.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

            foreach (ReportRoundTrip roundTrip in roundTrips)
            {
                markdown.AppendLine("| " + roundTrip.Number.ToString(CultureInfo.InvariantCulture) +
                    " | " + roundTrip.Symbol +
                    " | " + roundTrip.Direction +
                    " | " + DigestValueFormatter.Timestamp(roundTrip.EntryTime) +
                    " | " + DigestValueFormatter.Timestamp(roundTrip.ExitTime) +
                    " | " + DigestValueFormatter.Number(roundTrip.EntryPrice) +
                    " | " + DigestValueFormatter.Number(roundTrip.ExitPrice) +
                    " | " + roundTrip.ExitReason +
                    " | " + roundTrip.Quantity.ToString(CultureInfo.InvariantCulture) +
                    " | " + DigestValueFormatter.Money(roundTrip.RealizedPnL) +
                    " | " + DigestValueFormatter.Percent(roundTrip.ReturnPercent) +
                    " | " + roundTrip.TimeHeld + " |");
            }

            markdown.AppendLine();
        }

        private static void AppendRejectedOrders(StringBuilder markdown, IReadOnlyList<ReportRejectedOrder> rejectedOrders)
        {
            if (rejectedOrders == null || rejectedOrders.Count == 0)
            {
                return;
            }

            markdown.AppendLine("## Rejected orders");
            markdown.AppendLine("| Symbol | Side | Time | Price | Qty | Reason |");
            markdown.AppendLine("| --- | --- | --- | --- | --- | --- |");

            foreach (ReportRejectedOrder rejectedOrder in rejectedOrders)
            {
                markdown.AppendLine("| " + rejectedOrder.Symbol +
                    " | " + rejectedOrder.Direction +
                    " | " + DigestValueFormatter.Timestamp(rejectedOrder.Time) +
                    " | " + DigestValueFormatter.Number(rejectedOrder.Price) +
                    " | " + rejectedOrder.Quantity.ToString(CultureInfo.InvariantCulture) +
                    " | " + rejectedOrder.Reason + " |");
            }

            markdown.AppendLine();
        }

        /// <summary>
        /// Builds the Performance table's rows in the order and with the labels the report's stat panels
        /// use, so a Finding quoting a metric names it as the page does.
        /// </summary>
        private static IReadOnlyList<DigestStatsMetric> BuildMetrics()
        {
            return new List<DigestStatsMetric>
            {
                new("Net profit", stats => DigestValueFormatter.Money(stats.NetProfit)),
                new("Net profit %", stats => DigestValueFormatter.Percent(stats.NetProfitPercent)),
                new("Buy & hold", stats => DigestValueFormatter.Percent(stats.BuyHoldReturnPercent)),
                new("CAGR", stats => DigestValueFormatter.Percent(stats.Cagr)),
                new("Sharpe", stats => DigestValueFormatter.Number(stats.Sharpe)),
                new("Sortino", stats => DigestValueFormatter.Number(stats.Sortino)),
                new("Max drawdown", stats => DigestValueFormatter.Percent(stats.MaxDrawdown)),
                new("Avg drawdown", stats => DigestValueFormatter.Percent(stats.AvgDrawdown)),
                new("Drawdown length", stats => stats.MaxDrawdownDuration),
                new("Time to recover", stats => stats.TimeToRecover),
                new("Recovery factor", stats => DigestValueFormatter.Number(stats.RecoveryFactor)),
                new("Calmar", stats => DigestValueFormatter.Number(stats.Calmar)),
                new("Trades", stats => stats.Trades.ToString(CultureInfo.InvariantCulture)),
                new("Winners", stats => stats.Winners.ToString(CultureInfo.InvariantCulture)),
                new("Break-even", stats => stats.BreakEven.ToString(CultureInfo.InvariantCulture)),
                new("Losers", stats => stats.Losers.ToString(CultureInfo.InvariantCulture)),
                new("Win rate", stats => DigestValueFormatter.Percent(stats.WinRate)),
                new("Profit factor", stats => DigestValueFormatter.Number(stats.ProfitFactor)),
                new("Expectancy", stats => DigestValueFormatter.Money(stats.Expectancy)),
                new("Avg R", stats => DigestValueFormatter.Number(stats.AvgRMultiple)),
                new("Avg win", stats => DigestValueFormatter.Money(stats.AvgWin)),
                new("Avg loss", stats => DigestValueFormatter.Money(stats.AvgLoss)),
                new("Median trade", stats => DigestValueFormatter.Money(stats.MedianTrade)),
                new("Largest win", stats => DigestValueFormatter.Money(stats.LargestWin)),
                new("Largest loss", stats => DigestValueFormatter.Money(stats.LargestLoss)),
                new("Max consec. wins", stats => stats.MaxConsecWins.ToString(CultureInfo.InvariantCulture)),
                new("Max consec. losses", stats => stats.MaxConsecLosses.ToString(CultureInfo.InvariantCulture)),
                new("Profitable long", stats => DigestValueFormatter.Percent(stats.LongWinRate)),
                new("Profitable short", stats => DigestValueFormatter.Percent(stats.ShortWinRate)),
                new("Avg duration", stats => stats.AvgTradeDuration),
                new("Median duration", stats => stats.MedianTradeDuration),
                new("Longest trade", stats => stats.LongestTradeDuration),
                new("Shortest trade", stats => stats.ShortestTradeDuration),
                new("Market exposure", stats => DigestValueFormatter.Percent(stats.MarketExposure)),
                new("Avg capital", stats => DigestValueFormatter.Money(stats.AvgCapitalInvested)),
                new("Max capital", stats => DigestValueFormatter.Money(stats.MaxCapitalInvested))
            };
        }
    }
}
