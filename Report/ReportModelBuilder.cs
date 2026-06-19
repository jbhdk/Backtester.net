using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Marker colour for a winning round trip (matches the report's green accent).</summary>
        private const string WinColor = "#2ecc71";

        /// <summary>Marker colour for a losing round trip (matches the report's red accent).</summary>
        private const string LossColor = "#e74c3c";

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
                Chart = MapChart(result.CandleHistory, stats.RoundTrips),
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

        /// <summary>
        /// Projects the raw per-symbol candle history into chart-ready series, encoding each bar's
        /// timestamp as UTC seconds.
        /// </summary>
        private static ReportChart MapChart(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> candleHistory,
            IReadOnlyList<RoundTrip> roundTrips)
        {
            // Key: symbol/ticker -> the chart-ready candle series for that symbol.
            Dictionary<string, IReadOnlyList<ChartCandle>> series = new(candleHistory.Count);
            foreach (KeyValuePair<string, IReadOnlyList<Candle>> entry in candleHistory)
            {
                List<ChartCandle> bars = new(entry.Value.Count);
                foreach (Candle candle in entry.Value)
                {
                    bars.Add(new ChartCandle
                    {
                        Time = ToUnixSeconds(candle.Timestamp),
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close
                    });
                }

                series[entry.Key] = bars;
            }

            return new ReportChart { Series = series, Markers = MapMarkers(roundTrips) };
        }

        /// <summary>
        /// Derives the entry/exit markers for the round trips. Each round trip yields an entry marker
        /// (an arrow up below the entry bar) and an exit marker (an arrow down above the exit bar).
        /// </summary>
        private static IReadOnlyList<ChartMarker> MapMarkers(IReadOnlyList<RoundTrip> roundTrips)
        {
            List<ChartMarker> markers = new(roundTrips.Count * 2);
            foreach (RoundTrip trip in roundTrips)
            {
                // Entry and exit share one colour and P&L label reflecting the round trip's outcome.
                string color = trip.RealizedPnL >= 0m ? WinColor : LossColor;
                string label = FormatPnL(trip.RealizedPnL);
                markers.Add(new ChartMarker
                {
                    Symbol = trip.Symbol,
                    Time = ToUnixSeconds(trip.EntryTime),
                    Position = "belowBar",
                    Shape = "arrowUp",
                    Color = color,
                    Text = label
                });
                markers.Add(new ChartMarker
                {
                    Symbol = trip.Symbol,
                    Time = ToUnixSeconds(trip.ExitTime),
                    Position = "aboveBar",
                    Shape = "arrowDown",
                    Color = color,
                    Text = label
                });
            }

            return markers;
        }

        /// <summary>
        /// Formats a round trip's profit/loss as a signed currency label (e.g. <c>"+$200.00"</c>,
        /// <c>"-$50.00"</c>) for a marker.
        /// </summary>
        private static string FormatPnL(decimal pnl)
        {
            string sign = pnl >= 0m ? "+" : "-";
            return sign + "$" + Math.Abs(pnl).ToString("N2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Encodes a UTC timestamp as seconds since the Unix epoch, the form the chart library uses
        /// for intraday data.
        /// </summary>
        private static long ToUnixSeconds(DateTime timestamp)
        {
            return new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeSeconds();
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
