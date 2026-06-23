using System;
using System.Collections.Generic;
using System.Globalization;
using Backtester.Broker;
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
        /// Maps the run's <paramref name="result"/> to a report model. Everything the report renders,
        /// including the run inputs, is derived from the result alone (ADR 0008).
        /// </summary>
        public ReportModel Build(BacktestResult result)
        {
            PerformanceStats stats = result.Portfolio.GetPerformanceStats();
            decimal startingEquity = result.StartingEquity;
            decimal finalEquity = FinalEquity(result.Portfolio, startingEquity);
            decimal totalReturn = startingEquity != 0m ? (finalEquity - startingEquity) / startingEquity : 0m;

            // Buy-and-hold is a price benchmark, so it is derived here (the report layer holds the candle
            // history) rather than in the engine's stats. Per-symbol it is that symbol's first-to-last close
            // return; for the portfolio it is the equal-weight average across the symbols that have candles.
            IReadOnlyDictionary<string, decimal> buyHoldBySymbol = BuyHoldReturns(result.CandleHistory);
            decimal portfolioBuyHold = buyHoldBySymbol.Count > 0 ? Average(buyHoldBySymbol.Values) : 0m;

            return new ReportModel
            {
                Stats = MapStats(stats, startingEquity, portfolioBuyHold),
                StatsBySymbol = MapStatsBySymbol(result.Portfolio.GetPerformanceStatsBySymbol(), startingEquity, buyHoldBySymbol),
                RoundTrips = MapRoundTrips(stats.RoundTrips),
                RejectedOrders = MapRejectedOrders(result.RejectedOrders),
                Indicators = MapIndicators(result.IndicatorSeries),
                EquityCurve = MapEquityCurve(stats.RoundTrips, startingEquity),
                Chart = MapChart(result.CandleHistory, stats.RoundTrips),
                Run = new ReportRunInfo
                {
                    Symbols = result.Symbols,
                    Interval = result.Interval,
                    FromUtc = result.FromUtc,
                    ToUtc = result.ToUtc,
                    StartingEquity = startingEquity,
                    FinalEquity = finalEquity,
                    TotalReturnPercent = totalReturn
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
                    Direction = trip.Direction == PositionDirection.Short ? "Short" : "Long",
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
        /// Projects the broker's rejected orders into report form, mapping the attempted side to a
        /// direction (Buy → Long, Sell → Short) and carrying the instrument, size, price, time, and reason
        /// straight through. Empty when no orders were rejected.
        /// </summary>
        private static IReadOnlyList<ReportRejectedOrder> MapRejectedOrders(IReadOnlyList<RejectedOrder> rejectedOrders)
        {
            List<ReportRejectedOrder> mapped = new(rejectedOrders.Count);
            foreach (RejectedOrder rejected in rejectedOrders)
            {
                mapped.Add(new ReportRejectedOrder
                {
                    Symbol = rejected.Symbol,
                    Direction = rejected.Side == OrderSide.Sell ? "Short" : "Long",
                    Time = rejected.Timestamp,
                    Price = rejected.Price,
                    Quantity = rejected.Quantity,
                    Reason = rejected.Reason
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
        /// Projects the strategy-exposed indicator series into chart-ready form, preserving their order
        /// (no re-derivation): each point's timestamp encoded as UTC seconds so it aligns to the candle
        /// time axis, and each series' pane designation mapped to a page-friendly string.
        /// </summary>
        private static IReadOnlyList<ChartIndicator> MapIndicators(IReadOnlyList<IndicatorSeries> indicators)
        {
            List<ChartIndicator> mapped = new(indicators.Count);
            foreach (IndicatorSeries series in indicators)
            {
                List<ChartLinePoint> points = new(series.Points.Count);
                foreach (IndicatorPoint point in series.Points)
                {
                    points.Add(new ChartLinePoint { Time = ToUnixSeconds(point.Timestamp), Value = point.Value });
                }

                mapped.Add(new ChartIndicator
                {
                    Name = series.Name,
                    Symbol = series.Symbol,
                    Pane = MapPane(series.Pane),
                    Points = points
                });
            }

            return mapped;
        }

        /// <summary>
        /// Maps an indicator's pane designation to the page-friendly string the chart rendering reads.
        /// </summary>
        private static string MapPane(IndicatorPane pane)
        {
            return pane == IndicatorPane.PriceOverlay ? "priceOverlay" : "separatePane";
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
        /// Derives the entry/exit markers for the round trips. The arrows follow the trade direction: a
        /// long buys to enter (arrow up below the bar) and sells to exit (arrow down above the bar); a
        /// short sells to enter (arrow down above the bar) and buys to cover (arrow up below the bar).
        /// Colour and the P&amp;L label come from the round trip's outcome, independent of direction.
        /// </summary>
        private static IReadOnlyList<ChartMarker> MapMarkers(IReadOnlyList<RoundTrip> roundTrips)
        {
            List<ChartMarker> markers = new(roundTrips.Count * 2);
            for (int i = 0; i < roundTrips.Count; i++)
            {
                RoundTrip trip = roundTrips[i];
                // The 1-based round-trip number links both markers back to the matching table row.
                int number = i + 1;
                // Entry and exit share one colour and P&L label reflecting the round trip's outcome.
                string color = trip.RealizedPnL >= 0m ? WinColor : LossColor;
                string label = FormatPnL(trip.RealizedPnL);
                bool isShort = trip.Direction == PositionDirection.Short;
                markers.Add(new ChartMarker
                {
                    Symbol = trip.Symbol,
                    RoundTripNumber = number,
                    Time = ToUnixSeconds(trip.EntryTime),
                    Position = isShort ? "aboveBar" : "belowBar",
                    Shape = isShort ? "arrowDown" : "arrowUp",
                    Color = color,
                    Text = label
                });
                markers.Add(new ChartMarker
                {
                    Symbol = trip.Symbol,
                    RoundTripNumber = number,
                    Time = ToUnixSeconds(trip.ExitTime),
                    Position = isShort ? "belowBar" : "aboveBar",
                    Shape = isShort ? "arrowUp" : "arrowDown",
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

        /// <summary>
        /// Builds the portfolio-wide equity curve indexed by trade count: point zero is the starting
        /// equity, and each subsequent point adds the next closed round trip's realized P&amp;L (round
        /// trips taken in exit-time order).
        /// </summary>
        private static IReadOnlyList<ReportEquityPoint> MapEquityCurve(IReadOnlyList<RoundTrip> roundTrips, decimal startingEquity)
        {
            List<RoundTrip> ordered = new(roundTrips);
            ordered.Sort((left, right) => left.ExitTime.CompareTo(right.ExitTime));

            List<ReportEquityPoint> curve = new(ordered.Count + 1) { new ReportEquityPoint { Trade = 0, Equity = startingEquity } };
            decimal equity = startingEquity;
            for (int i = 0; i < ordered.Count; i++)
            {
                equity += ordered[i].RealizedPnL;
                curve.Add(new ReportEquityPoint { Trade = i + 1, Equity = equity });
            }

            return curve;
        }

        private static decimal FinalEquity(Portfolio portfolio, decimal startingEquity)
        {
            IReadOnlyList<EquitySnapshot> history = portfolio.EquityHistory;
            return history.Count > 0 ? history[history.Count - 1].MarkedEquity : startingEquity;
        }

        /// <summary>
        /// Projects each symbol's performance stats into report form, keyed by symbol, attaching that
        /// symbol's buy-and-hold return as its equal-weight contribution to the portfolio benchmark
        /// (the symbol's own price return divided by the number of benchmark symbols), so the per-symbol
        /// column compares against net profit % on the same whole-portfolio capital base and the
        /// contributions sum to the portfolio's buy-and-hold figure.
        /// </summary>
        private static IReadOnlyDictionary<string, ReportStats> MapStatsBySymbol(
            IReadOnlyDictionary<string, PerformanceStats> statsBySymbol,
            decimal startingEquity,
            IReadOnlyDictionary<string, decimal> buyHoldBySymbol)
        {
            int benchmarkSymbols = buyHoldBySymbol.Count;

            // Key: symbol/ticker -> that symbol's stats projected for the report's per-symbol column.
            Dictionary<string, ReportStats> mapped = new(statsBySymbol.Count);
            foreach (KeyValuePair<string, PerformanceStats> entry in statsBySymbol)
            {
                decimal buyHold = buyHoldBySymbol.TryGetValue(entry.Key, out decimal value) ? value : 0m;
                decimal buyHoldContribution = benchmarkSymbols > 0 ? buyHold / benchmarkSymbols : 0m;
                mapped[entry.Key] = MapStats(entry.Value, startingEquity, buyHoldContribution);
            }

            return mapped;
        }

        private static ReportStats MapStats(
            PerformanceStats stats,
            decimal startingEquity,
            decimal buyHoldReturnPercent)
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
                MaxConsecLosses = stats.MaxConsecLosses,
                MaxConsecWins = stats.MaxConsecWins,
                BuyHoldReturnPercent = buyHoldReturnPercent,
                Sortino = stats.Sortino,
                Calmar = stats.Calmar,
                RecoveryFactor = stats.RecoveryFactor,
                AvgDrawdown = stats.AvgDrawdown,
                MaxDrawdownDuration = FormatTimeHeld(stats.MaxDrawdownDuration),
                TimeToRecover = FormatTimeHeld(stats.TimeToRecover),
                MedianTrade = stats.MedianTrade,
                LargestWin = stats.LargestWin,
                LargestLoss = stats.LargestLoss,
                AvgRMultiple = stats.AvgRMultiple,
                LongWinRate = stats.LongWinRate,
                ShortWinRate = stats.ShortWinRate,
                MarketExposure = stats.MarketExposure,
                AvgCapitalInvested = stats.AvgCapitalInvested,
                MaxCapitalInvested = stats.MaxCapitalInvested,
                AvgTradeDuration = FormatTimeHeld(stats.AvgTradeDuration),
                MedianTradeDuration = FormatTimeHeld(stats.MedianTradeDuration),
                LongestTradeDuration = FormatTimeHeld(stats.LongestTradeDuration),
                ShortestTradeDuration = FormatTimeHeld(stats.ShortestTradeDuration)
            };
        }

        /// <summary>
        /// Computes each symbol's buy-and-hold return — its last close over its first close, minus one —
        /// from the candle history. Symbols with fewer than two bars or a non-positive first close are
        /// omitted (no meaningful benchmark).
        /// </summary>
        private static IReadOnlyDictionary<string, decimal> BuyHoldReturns(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> candleHistory)
        {
            // Key: symbol/ticker -> that symbol's buy-and-hold return over the run as a fraction.
            Dictionary<string, decimal> returns = new(candleHistory.Count);
            foreach (KeyValuePair<string, IReadOnlyList<Candle>> entry in candleHistory)
            {
                IReadOnlyList<Candle> candles = entry.Value;
                if (candles.Count < 2)
                {
                    continue;
                }

                decimal first = candles[0].Close;
                decimal last = candles[candles.Count - 1].Close;
                if (first <= 0m)
                {
                    continue;
                }

                returns[entry.Key] = (last - first) / first;
            }

            return returns;
        }

        /// <summary>Returns the arithmetic mean of the given values; zero when the sequence is empty.</summary>
        private static decimal Average(IEnumerable<decimal> values)
        {
            decimal sum = 0m;
            int count = 0;
            foreach (decimal value in values)
            {
                sum += value;
                count++;
            }

            return count > 0 ? sum / count : 0m;
        }
    }
}
