using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Core
{
    /// <summary>
    /// Builds round trips from the trade ledger and computes aggregate performance metrics.
    /// </summary>
    public static class PerformanceCalculator
    {
        /// <summary>
        /// Pairs buy and sell trades into round trips. Each sell creates one round trip
        /// carrying the running average entry price, realized PnL, and bars held.
        /// </summary>
        public static IReadOnlyList<RoundTrip> BuildRoundTrips(
            IReadOnlyList<Trade> trades,
            IReadOnlyList<EquitySnapshot> equityHistory)
        {
            // key: symbol → (runningAvgEntry, runningQty, entryBarIndex, entryTime)
            Dictionary<string, (decimal avgEntry, int qty, int entryBarIdx, DateTime entryTime)> open = new();
            List<RoundTrip> trips = new();

            foreach (Trade trade in trades)
            {
                if (trade.Side == OrderSide.Buy)
                {
                    if (open.TryGetValue(trade.Symbol, out (decimal avgEntry, int qty, int entryBarIdx, DateTime entryTime) pos))
                    {
                        decimal totalCost = pos.avgEntry * pos.qty + trade.Price * trade.Quantity;
                        int newQty = pos.qty + trade.Quantity;
                        open[trade.Symbol] = (totalCost / newQty, newQty, pos.entryBarIdx, pos.entryTime);
                    }
                    else
                    {
                        int entryBarIdx = BarIndexAt(equityHistory, trade.Timestamp);
                        open[trade.Symbol] = (trade.Price, trade.Quantity, entryBarIdx, trade.Timestamp);
                    }
                }
                else
                {
                    if (!open.TryGetValue(trade.Symbol, out (decimal avgEntry, int qty, int entryBarIdx, DateTime entryTime) pos))
                    {
                        continue;
                    }

                    int exitBarIdx = BarIndexAt(equityHistory, trade.Timestamp);
                    trips.Add(new RoundTrip
                    {
                        Symbol      = trade.Symbol,
                        EntryPrice  = pos.avgEntry,
                        ExitPrice   = trade.Price,
                        Quantity    = trade.Quantity,
                        RealizedPnL = (trade.Price - pos.avgEntry) * trade.Quantity,
                        BarsHeld    = Math.Max(0, exitBarIdx - pos.entryBarIdx),
                        EntryTime   = pos.entryTime,
                        ExitTime    = trade.Timestamp
                    });

                    int remaining = pos.qty - trade.Quantity;
                    if (remaining <= 0)
                    {
                        open.Remove(trade.Symbol);
                    }
                    else
                    {
                        open[trade.Symbol] = (pos.avgEntry, remaining, pos.entryBarIdx, pos.entryTime);
                    }

                }
            }

            return trips;
        }

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
            int maxConsecLosses = ComputeMaxConsecLosses(roundTrips);

            return new PerformanceStats
            {
                RoundTrips      = roundTrips,
                NetProfit       = netProfit,
                GrossProfit     = grossProfit,
                GrossLoss       = grossLoss,
                Trades          = trades,
                WinRate         = winRate,
                ProfitFactor    = profitFactor,
                AvgWin          = avgWin,
                AvgLoss         = avgLoss,
                Expectancy      = expectancy,
                MaxDrawdown     = maxDrawdown,
                Cagr            = cagr,
                Sharpe          = sharpe,
                MaxConsecLosses = maxConsecLosses
            };
        }

        private static int BarIndexAt(IReadOnlyList<EquitySnapshot> history, DateTime ts)
        {
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Timestamp >= ts)
                {
                    return i;
                }
            }

            return history.Count;
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

            return (decimal)(Math.Pow((double)(finalEquity / startingCash), 1.0 / years) - 1.0);
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

        private static int ComputeMaxConsecLosses(IReadOnlyList<RoundTrip> roundTrips)
        {
            int max = 0;
            int current = 0;
            foreach (RoundTrip trip in roundTrips)
            {
                if (trip.RealizedPnL < 0m) { current++; if (current > max)
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
    }
}
