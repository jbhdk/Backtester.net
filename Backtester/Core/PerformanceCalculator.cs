using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Core
{
    /// <summary>
    /// Computes aggregate performance metrics from round trips and the equity curve.
    /// </summary>
    public static class PerformanceCalculator
    {
        /// <summary>
        /// Computes aggregate performance statistics from round trips and the marked equity curve.
        /// </summary>
        public static PerformanceStats Calculate(
            IReadOnlyList<RoundTrip> roundTrips,
            IReadOnlyList<EquitySnapshot> equityHistory,
            decimal startingCash)
        {
            int trades = roundTrips.Count;

            List<RoundTrip> wins   = roundTrips.Where(r => r.RealizedPnL > 0m).ToList();
            List<RoundTrip> losses = roundTrips.Where(r => r.RealizedPnL < 0m).ToList();
            // Round trips that closed at exactly zero P&L are neither winners nor losers; they are reported
            // separately so winners + break-even + losers reconciles to the total trade count.
            int breakEven = trades - wins.Count - losses.Count;

            decimal grossProfit = wins.Sum(r => r.RealizedPnL);
            decimal grossLoss   = losses.Sum(r => r.RealizedPnL);    // negative
            decimal netProfit   = grossProfit + grossLoss;

            decimal winRate      = trades > 0 ? (decimal)wins.Count / trades : 0m;
            decimal profitFactor = grossLoss != 0m ? grossProfit / Math.Abs(grossLoss) : 0m;
            decimal avgWin       = wins.Count   > 0 ? grossProfit / wins.Count   : 0m;
            decimal avgLoss      = losses.Count > 0 ? grossLoss   / losses.Count : 0m;
            decimal expectancy   = trades > 0 ? netProfit / trades : 0m;

            decimal maxDrawdown = ComputeMaxDrawdown(equityHistory);
            decimal cagr        = ComputeCagr(equityHistory, startingCash);
            decimal sharpe      = ComputeSharpe(equityHistory);
            decimal sortino     = ComputeSortino(equityHistory);
            int maxConsecLosses = ComputeMaxConsecStreak(roundTrips, losing: true);
            int maxConsecWins   = ComputeMaxConsecStreak(roundTrips, losing: false);

            // Drawdown episode analysis backs the average-depth, longest-duration, recovery-time and
            // recovery-factor metrics; max-depth-in-currency feeds the recovery factor.
            (decimal avgDrawdown, decimal maxDrawdownAmount, TimeSpan maxDrawdownDuration, TimeSpan timeToRecover) =
                AnalyzeDrawdowns(equityHistory);

            decimal calmar         = maxDrawdown != 0m ? cagr / maxDrawdown : 0m;
            decimal recoveryFactor = maxDrawdownAmount != 0m ? netProfit / maxDrawdownAmount : 0m;

            // "Avg R" is the plain mean of per-trade R (RealizedPnL / InitialRisk) over the round trips
            // that declared an initial risk; trips with no stop (null risk) are excluded from both the
            // sum and the divisor rather than counted as 0R, and the stat is null when none qualify.
            List<decimal> definedRMultiples = roundTrips
                .Where(trip => trip.InitialRisk.HasValue && trip.InitialRisk.Value != 0m)
                .Select(trip => trip.RealizedPnL / trip.InitialRisk.Value)
                .ToList();
            decimal? avgRMultiple = definedRMultiples.Count > 0 ? definedRMultiples.Average() : (decimal?)null;

            decimal medianTrade = Median(roundTrips.Select(r => r.RealizedPnL).ToList());
            decimal largestWin  = wins.Count   > 0 ? wins.Max(r => r.RealizedPnL)   : 0m;
            decimal largestLoss = losses.Count > 0 ? losses.Min(r => r.RealizedPnL) : 0m;

            decimal longWinRate  = DirectionalWinRate(roundTrips, PositionDirection.Long);
            decimal shortWinRate = DirectionalWinRate(roundTrips, PositionDirection.Short);

            (decimal marketExposure, decimal avgCapitalInvested, decimal maxCapitalInvested) =
                ComputeExposure(equityHistory);

            (TimeSpan avgDuration, TimeSpan medianDuration, TimeSpan longestDuration, TimeSpan shortestDuration) =
                ComputeTradeDurations(roundTrips);

            return new PerformanceStats
            {
                RoundTrips          = roundTrips,
                NetProfit           = netProfit,
                GrossProfit         = grossProfit,
                GrossLoss           = grossLoss,
                Trades              = trades,
                Winners             = wins.Count,
                Losers              = losses.Count,
                BreakEven           = breakEven,
                WinRate             = winRate,
                ProfitFactor        = profitFactor,
                AvgWin              = avgWin,
                AvgLoss             = avgLoss,
                Expectancy          = expectancy,
                MaxDrawdown         = maxDrawdown,
                Cagr                = cagr,
                Sharpe              = sharpe,
                MaxConsecLosses     = maxConsecLosses,
                MaxConsecWins       = maxConsecWins,
                Sortino             = sortino,
                Calmar              = calmar,
                RecoveryFactor      = recoveryFactor,
                AvgDrawdown         = avgDrawdown,
                MaxDrawdownDuration = maxDrawdownDuration,
                TimeToRecover       = timeToRecover,
                MedianTrade         = medianTrade,
                LargestWin          = largestWin,
                LargestLoss         = largestLoss,
                AvgRMultiple        = avgRMultiple,
                LongWinRate         = longWinRate,
                ShortWinRate        = shortWinRate,
                MarketExposure      = marketExposure,
                AvgCapitalInvested  = avgCapitalInvested,
                MaxCapitalInvested  = maxCapitalInvested,
                AvgTradeDuration    = avgDuration,
                MedianTradeDuration = medianDuration,
                LongestTradeDuration = longestDuration,
                ShortestTradeDuration = shortestDuration
            };
        }

        /// <summary>
        /// Computes performance statistics for each symbol independently. A symbol's trade metrics are
        /// derived from its own round trips; the equity-based metrics (max drawdown, CAGR, Sharpe) are
        /// derived from that symbol's isolated equity curve recorded on each snapshot
        /// (<see cref="EquitySnapshot.EquityBySymbol"/>), so the per-symbol and portfolio figures use the
        /// same routines.
        /// </summary>
        public static IReadOnlyDictionary<string, PerformanceStats> CalculateBySymbol(
            IReadOnlyList<RoundTrip> roundTrips,
            IReadOnlyList<EquitySnapshot> equityHistory,
            decimal startingCash)
        {
            // Key: symbol/ticker -> that symbol's standalone performance statistics.
            Dictionary<string, PerformanceStats> statsBySymbol = new();
            foreach (string symbol in roundTrips.Select(trip => trip.Symbol).Distinct())
            {
                List<RoundTrip> symbolTrips = roundTrips.Where(trip => trip.Symbol == symbol).ToList();
                statsBySymbol[symbol] = Calculate(symbolTrips, IsolatedHistory(equityHistory, symbol, startingCash), startingCash);
            }

            return statsBySymbol;
        }

        /// <summary>
        /// Projects the recorded equity history onto a single symbol, reading that symbol's isolated
        /// equity at each snapshot and falling back to starting cash for snapshots taken before the
        /// symbol first traded.
        /// </summary>
        private static IReadOnlyList<EquitySnapshot> IsolatedHistory(
            IReadOnlyList<EquitySnapshot> equityHistory,
            string symbol,
            decimal startingCash)
        {
            List<EquitySnapshot> isolated = new(equityHistory.Count);
            foreach (EquitySnapshot snapshot in equityHistory)
            {
                decimal equity = snapshot.EquityBySymbol != null && snapshot.EquityBySymbol.TryGetValue(symbol, out decimal value)
                    ? value
                    : startingCash;

                // Carry only this symbol's open-position value so the per-symbol exposure and capital
                // metrics see the symbol in isolation; absent (flat) means an empty map for this bar.
                IReadOnlyDictionary<string, decimal> positionValue =
                    snapshot.PositionValueBySymbol != null && snapshot.PositionValueBySymbol.TryGetValue(symbol, out decimal posValue)
                        ? new Dictionary<string, decimal> { [symbol] = posValue }
                        : new Dictionary<string, decimal>();

                isolated.Add(new EquitySnapshot
                {
                    Timestamp = snapshot.Timestamp,
                    MarkedEquity = equity,
                    PositionValueBySymbol = positionValue
                });
            }

            return isolated;
        }

        private static decimal ComputeMaxDrawdown(IReadOnlyList<EquitySnapshot> history)
        {
            if (history.Count == 0)
            {
                return 0m;
            }

            decimal peak = history[0].MarkedEquity;
            decimal maxDd = 0m;
            foreach (EquitySnapshot snap in history)
            {
                if (snap.MarkedEquity > peak)
                {
                    peak = snap.MarkedEquity;
                }

                if (peak > 0m)
                {
                    decimal dd = (peak - snap.MarkedEquity) / peak;
                    if (dd > maxDd)
                    {
                        maxDd = dd;
                    }
                }
            }

            return maxDd;
        }

        private static decimal ComputeCagr(IReadOnlyList<EquitySnapshot> history, decimal startingCash)
        {
            if (history.Count < 2 || startingCash <= 0m)
            {
                return 0m;
            }

            decimal finalEquity = history[history.Count - 1].MarkedEquity;
            double years = (history[history.Count - 1].Timestamp - history[0].Timestamp).TotalDays / 365.25;
            if (years <= 0 || finalEquity <= 0m)
            {
                return 0m;
            }

            // For very short spans the exponent (1/years) is enormous; annualising a profit can
            // exceed decimal range and overflow the cast. Validate in double before converting.
            double cagr = Math.Pow((double)(finalEquity / startingCash), 1.0 / years) - 1.0;
            if (!double.IsFinite(cagr) || Math.Abs(cagr) > (double)decimal.MaxValue)
            {
                return 0m;
            }

            return (decimal)cagr;
        }

        private static decimal ComputeSharpe(IReadOnlyList<EquitySnapshot> history)
        {
            if (history.Count < 2)
            {
                return 0m;
            }

            List<double> returns = new();
            for (int i = 1; i < history.Count; i++)
            {
                double prev = (double)history[i - 1].MarkedEquity;
                if (prev == 0)
                {
                    continue;
                }

                returns.Add(((double)history[i].MarkedEquity - prev) / prev);
            }

            if (returns.Count < 2)
            {
                return 0m;
            }

            double mean   = returns.Average();
            double stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - mean, 2)) / (returns.Count - 1));
            if (stdDev == 0)
            {
                return 0m;
            }

            return (decimal)(mean / stdDev * Math.Sqrt(252));
        }

        /// <summary>
        /// Computes the longest run of consecutive losing round trips (<paramref name="losing"/> true) or
        /// winning round trips (false), in trade order. Break-even trips (zero P&amp;L) reset the streak.
        /// </summary>
        private static int ComputeMaxConsecStreak(IReadOnlyList<RoundTrip> roundTrips, bool losing)
        {
            int max = 0;
            int current = 0;
            foreach (RoundTrip trip in roundTrips)
            {
                bool counts = losing ? trip.RealizedPnL < 0m : trip.RealizedPnL > 0m;
                if (counts)
                {
                    current++;
                    if (current > max)
                    {
                        max = current;
                    }
                }
                else
                {
                    current = 0;
                }
            }

            return max;
        }

        /// <summary>
        /// Computes the annualised Sortino ratio from the bar-to-bar returns of marked equity: the mean
        /// return divided by the downside deviation (root-mean-square of the negative returns, target zero),
        /// scaled by sqrt(252). Returns zero when there are too few returns or no downside dispersion.
        /// </summary>
        private static decimal ComputeSortino(IReadOnlyList<EquitySnapshot> history)
        {
            if (history.Count < 2)
            {
                return 0m;
            }

            List<double> returns = new();
            for (int i = 1; i < history.Count; i++)
            {
                double prev = (double)history[i - 1].MarkedEquity;
                if (prev == 0)
                {
                    continue;
                }

                returns.Add(((double)history[i].MarkedEquity - prev) / prev);
            }

            if (returns.Count < 2)
            {
                return 0m;
            }

            double mean = returns.Average();
            // Downside deviation: penalise only returns below the target (zero), divided by the sample size.
            double downsideVar = returns.Sum(r => r < 0 ? r * r : 0) / (returns.Count - 1);
            double downsideDev = Math.Sqrt(downsideVar);
            if (downsideDev == 0)
            {
                return 0m;
            }

            return (decimal)(mean / downsideDev * Math.Sqrt(252));
        }

        /// <summary>
        /// Walks the marked-equity curve identifying drawdown episodes — each a decline from a peak until
        /// equity reclaims that peak (or the run ends underwater) — and returns the mean episode depth as a
        /// fraction, the deepest episode's depth in currency, the longest episode's duration, and the time
        /// from the deepest episode's trough back to a new high. All zero when equity never declines.
        /// </summary>
        private static (decimal avgDepth, decimal maxAmount, TimeSpan longestDuration, TimeSpan timeToRecover) AnalyzeDrawdowns(
            IReadOnlyList<EquitySnapshot> history)
        {
            if (history.Count == 0)
            {
                return (0m, 0m, TimeSpan.Zero, TimeSpan.Zero);
            }

            // Each completed (or open-at-end) drawdown episode: depth as a fraction and in currency, the
            // peak-to-recovery duration, and the trough-to-recovery time.
            List<(decimal depthFraction, decimal depthAmount, TimeSpan duration, TimeSpan recoverFromTrough)> episodes = new();

            decimal peak = history[0].MarkedEquity;
            DateTime peakTime = history[0].Timestamp;
            bool inDrawdown = false;
            decimal episodePeak = 0m;
            DateTime episodePeakTime = default;
            decimal troughValue = 0m;
            DateTime troughTime = default;

            void CloseEpisode(DateTime recoveryTime)
            {
                decimal depthAmount = episodePeak - troughValue;
                decimal depthFraction = episodePeak > 0m ? depthAmount / episodePeak : 0m;
                episodes.Add((depthFraction, depthAmount, recoveryTime - episodePeakTime, recoveryTime - troughTime));
            }

            foreach (EquitySnapshot snap in history)
            {
                if (snap.MarkedEquity >= peak)
                {
                    if (inDrawdown)
                    {
                        CloseEpisode(snap.Timestamp);
                        inDrawdown = false;
                    }

                    peak = snap.MarkedEquity;
                    peakTime = snap.Timestamp;
                }
                else if (!inDrawdown)
                {
                    inDrawdown = true;
                    episodePeak = peak;
                    episodePeakTime = peakTime;
                    troughValue = snap.MarkedEquity;
                    troughTime = snap.Timestamp;
                }
                else if (snap.MarkedEquity < troughValue)
                {
                    troughValue = snap.MarkedEquity;
                    troughTime = snap.Timestamp;
                }
            }

            // An episode still open at the end never recovered: close it at the final timestamp.
            if (inDrawdown)
            {
                CloseEpisode(history[history.Count - 1].Timestamp);
            }

            if (episodes.Count == 0)
            {
                return (0m, 0m, TimeSpan.Zero, TimeSpan.Zero);
            }

            decimal avgDepth = episodes.Average(e => e.depthFraction);

            // The deepest episode (by fraction) supplies the recovery-factor amount and time-to-recover;
            // the longest episode (by duration) supplies the max drawdown duration.
            decimal deepestFraction = -1m;
            decimal deepestAmount = 0m;
            TimeSpan deepestRecover = TimeSpan.Zero;
            TimeSpan longestDuration = TimeSpan.Zero;
            foreach ((decimal depthFraction, decimal depthAmount, TimeSpan duration, TimeSpan recoverFromTrough) in episodes)
            {
                if (depthFraction > deepestFraction)
                {
                    deepestFraction = depthFraction;
                    deepestAmount = depthAmount;
                    deepestRecover = recoverFromTrough;
                }

                if (duration > longestDuration)
                {
                    longestDuration = duration;
                }
            }

            return (avgDepth, deepestAmount, longestDuration, deepestRecover);
        }

        /// <summary>Returns the fraction of round trips in the given direction that were profitable (0–1).</summary>
        private static decimal DirectionalWinRate(IReadOnlyList<RoundTrip> roundTrips, PositionDirection direction)
        {
            int total = 0;
            int wins = 0;
            foreach (RoundTrip trip in roundTrips)
            {
                if (trip.Direction != direction)
                {
                    continue;
                }

                total++;
                if (trip.RealizedPnL > 0m)
                {
                    wins++;
                }
            }

            return total > 0 ? (decimal)wins / total : 0m;
        }

        /// <summary>
        /// Computes the market-exposure fraction and the time-weighted average and peak gross capital
        /// invested from the equity curve, reading each snapshot's open-position market values. A bar is
        /// "exposed" when any position is open; capital invested on a bar is the sum of the absolute
        /// position values (so a short counts its gross value). Averages divide by every bar, flat or not.
        /// </summary>
        private static (decimal exposure, decimal avgCapital, decimal maxCapital) ComputeExposure(
            IReadOnlyList<EquitySnapshot> history)
        {
            if (history.Count == 0)
            {
                return (0m, 0m, 0m);
            }

            int exposedBars = 0;
            decimal sumCapital = 0m;
            decimal maxCapital = 0m;
            foreach (EquitySnapshot snap in history)
            {
                decimal invested = 0m;
                if (snap.PositionValueBySymbol != null)
                {
                    foreach (KeyValuePair<string, decimal> position in snap.PositionValueBySymbol)
                    {
                        invested += Math.Abs(position.Value);
                    }
                }

                if (invested > 0m)
                {
                    exposedBars++;
                }

                sumCapital += invested;
                if (invested > maxCapital)
                {
                    maxCapital = invested;
                }
            }

            return ((decimal)exposedBars / history.Count, sumCapital / history.Count, maxCapital);
        }

        /// <summary>
        /// Computes the mean, median, longest, and shortest round-trip holding times. All zero when there
        /// are no round trips.
        /// </summary>
        private static (TimeSpan avg, TimeSpan median, TimeSpan longest, TimeSpan shortest) ComputeTradeDurations(
            IReadOnlyList<RoundTrip> roundTrips)
        {
            if (roundTrips.Count == 0)
            {
                return (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
            }

            List<long> ticks = new(roundTrips.Count);
            foreach (RoundTrip trip in roundTrips)
            {
                ticks.Add((trip.ExitTime - trip.EntryTime).Ticks);
            }

            ticks.Sort();
            long avgTicks = (long)ticks.Average();
            long medianTicks = ticks.Count % 2 == 1
                ? ticks[ticks.Count / 2]
                : (ticks[ticks.Count / 2 - 1] + ticks[ticks.Count / 2]) / 2;

            return (new TimeSpan(avgTicks), new TimeSpan(medianTicks), new TimeSpan(ticks[ticks.Count - 1]), new TimeSpan(ticks[0]));
        }

        /// <summary>Returns the median of the given values (mean of the two middle values when even-sized); zero when empty.</summary>
        private static decimal Median(List<decimal> values)
        {
            if (values.Count == 0)
            {
                return 0m;
            }

            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
        }
    }
}
