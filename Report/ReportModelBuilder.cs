using System;
using System.Collections.Generic;
using Backtester.Core;
using Backtester.Engine;

namespace Backtester.Report
{
    /// <summary>
    /// Builds the serializable <see cref="ReportModel"/> from a backtest run. A pure function: it
    /// performs no I/O and derives every report value from the supplied result and run context.
    /// </summary>
    public class ReportModelBuilder
    {
        /// <summary>
        /// Maps the run's <paramref name="result"/> and <paramref name="context"/> to a report model.
        /// </summary>
        public ReportModel Build(BacktestResult result, ReportRunContext context)
        {
            PerformanceStats stats = result.Portfolio.GetPerformanceStats();
            decimal finalEquity = FinalEquity(result.Portfolio, context.StartingEquity);

            return new ReportModel
            {
                Stats = MapStats(stats, context.StartingEquity),
                RoundTrips = MapRoundTrips(stats.RoundTrips),
                Indicators = result.IndicatorSeries,
                EquityCurve = MapEquityCurve(result.Portfolio.EquityHistory),
                Candles = result.CandleHistory,
                Run = new ReportRunInfo
                {
                    Symbols = context.Symbols,
                    Interval = context.Interval,
                    FromUtc = context.FromUtc,
                    ToUtc = context.ToUtc,
                    StartingEquity = context.StartingEquity,
                    FinalEquity = finalEquity,
                    TotalReturnPercent = context.StartingEquity != 0m
                        ? (finalEquity - context.StartingEquity) / context.StartingEquity
                        : 0m
                }
            };
        }

        private static IReadOnlyList<ReportRoundTrip> MapRoundTrips(IReadOnlyList<RoundTrip> roundTrips)
        {
            List<ReportRoundTrip> mapped = new(roundTrips.Count);
            for (int i = 0; i < roundTrips.Count; i++)
            {
                RoundTrip trip = roundTrips[i];
                mapped.Add(new ReportRoundTrip
                {
                    Number = i + 1,
                    Symbol = trip.Symbol,
                    EntryTime = trip.EntryTime,
                    ExitTime = trip.ExitTime,
                    EntryPrice = trip.EntryPrice,
                    ExitPrice = trip.ExitPrice,
                    Quantity = trip.Quantity,
                    RealizedPnL = trip.RealizedPnL,
                    ReturnPercent = trip.EntryPrice != 0m ? (trip.ExitPrice - trip.EntryPrice) / trip.EntryPrice : 0m,
                    TimeHeld = FormatTimeHeld(trip.ExitTime - trip.EntryTime)
                });
            }

            return mapped;
        }

        /// <summary>
        /// Formats a holding duration compactly using its two most significant units: days and hours
        /// when a day or more, otherwise hours and minutes, otherwise minutes.
        /// </summary>
        private static string FormatTimeHeld(TimeSpan span)
        {
            if (span.Days > 0)
            {
                return $"{span.Days}d {span.Hours}h";
            }

            if (span.Hours > 0)
            {
                return $"{span.Hours}h {span.Minutes}m";
            }

            return $"{span.Minutes}m";
        }

        private static IReadOnlyList<ReportEquityPoint> MapEquityCurve(IReadOnlyList<EquitySnapshot> history)
        {
            List<ReportEquityPoint> curve = new(history.Count);
            foreach (EquitySnapshot snapshot in history)
            {
                curve.Add(new ReportEquityPoint { Timestamp = snapshot.Timestamp, Equity = snapshot.MarkedEquity });
            }

            return curve;
        }

        private static decimal FinalEquity(Portfolio portfolio, decimal startingEquity)
        {
            IReadOnlyList<EquitySnapshot> history = portfolio.EquityHistory;
            return history.Count > 0 ? history[history.Count - 1].MarkedEquity : startingEquity;
        }

        private static ReportStats MapStats(PerformanceStats stats, decimal startingEquity)
        {
            return new ReportStats
            {
                NetProfit = stats.NetProfit,
                NetProfitPercent = startingEquity != 0m ? stats.NetProfit / startingEquity : 0m,
                Trades = stats.Trades,
                WinRate = stats.WinRate,
                ProfitFactor = stats.ProfitFactor,
                AvgWin = stats.AvgWin,
                AvgLoss = stats.AvgLoss,
                Expectancy = stats.Expectancy,
                MaxDrawdown = stats.MaxDrawdown,
                Cagr = stats.Cagr,
                Sharpe = stats.Sharpe,
                MaxConsecLosses = stats.MaxConsecLosses
            };
        }
    }
}
