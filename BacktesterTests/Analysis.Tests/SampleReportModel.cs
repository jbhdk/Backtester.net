using System;
using System.Collections.Generic;
using Backtester.Report;

namespace BacktesterTests.Analysis.Tests
{
    /// <summary>
    /// Builds a small but complete <see cref="ReportModel"/> for the analyzer tests: two symbols,
    /// three round trips, a rejected order, configuration cards, and the chart material the digest
    /// is expected to leave out.
    /// </summary>
    public static class SampleReportModel
    {
        /// <summary>Builds the sample model.</summary>
        public static ReportModel Build()
        {
            return new ReportModel
            {
                Run = BuildRun(),
                Stats = BuildStats(),
                StatsBySymbol = BuildStatsBySymbol(),
                RoundTrips = BuildRoundTrips(),
                RejectedOrders = BuildRejectedOrders(),
                Configuration = BuildConfiguration(),
                Chart = BuildChart(),
                Indicators = BuildIndicators(),
                EquityCurve = BuildEquityCurve()
            };
        }

        /// <summary>Builds the sample model with the supplied number of round trips.</summary>
        public static ReportModel BuildWithRoundTrips(int count)
        {
            ReportModel model = Build();
            List<ReportRoundTrip> roundTrips = new List<ReportRoundTrip>();
            for (int i = 0; i < count; i++)
            {
                roundTrips.Add(new ReportRoundTrip
                {
                    Number = i + 1,
                    Symbol = "AAPL",
                    Direction = "Long",
                    EntryTime = new DateTime(2024, 1, 2, 14, 30, 0, DateTimeKind.Utc).AddDays(i),
                    ExitTime = new DateTime(2024, 1, 3, 20, 0, 0, DateTimeKind.Utc).AddDays(i),
                    EntryPrice = 100m,
                    ExitPrice = 101m,
                    Quantity = 10,
                    RealizedPnL = 10m,
                    ReturnPercent = 0.01m,
                    TimeHeld = "1d 5h",
                    ExitReason = "Signal"
                });
            }

            model.RoundTrips = roundTrips;
            return model;
        }

        private static ReportRunInfo BuildRun()
        {
            return new ReportRunInfo
            {
                Symbols = new List<string> { "AAPL", "MSFT" },
                Interval = "1d",
                FromUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2024, 6, 30, 0, 0, 0, DateTimeKind.Utc),
                StartingEquity = 100000m,
                FinalEquity = 112345.678m,
                TotalReturnPercent = 0.12345678m
            };
        }

        private static ReportStats BuildStats()
        {
            return new ReportStats
            {
                NetProfit = 12345.678m,
                NetProfitPercent = 0.12345678m,
                Trades = 3,
                Winners = 2,
                Losers = 1,
                BreakEven = 0,
                WinRate = 0.66666667m,
                ProfitFactor = 1.6234m,
                AvgWin = 900.125m,
                AvgLoss = -450.5m,
                Expectancy = 450.25m,
                MaxDrawdown = 0.0825m,
                Cagr = 0.2612m,
                Sharpe = 1.42m,
                MaxConsecLosses = 1,
                MaxConsecWins = 2,
                BuyHoldReturnPercent = 0.0912m,
                Sortino = 1.85m,
                Calmar = 3.1648m,
                RecoveryFactor = 2.44m,
                AvgDrawdown = 0.0312m,
                MaxDrawdownDuration = "63d 5h",
                TimeToRecover = "12d 3h",
                MedianTrade = 420.5m,
                LargestWin = 1200.75m,
                LargestLoss = -450.5m,
                AvgRMultiple = 0.9994m,
                LongWinRate = 0.66666667m,
                ShortWinRate = 0m,
                MarketExposure = 0.4321m,
                AvgCapitalInvested = 25400.5m,
                MaxCapitalInvested = 48200.25m,
                AvgTradeDuration = "3d 14h",
                MedianTradeDuration = "2d 6h",
                LongestTradeDuration = "9d 1h",
                ShortestTradeDuration = "1d 5h"
            };
        }

        // Key: symbol/ticker -> that symbol's standalone performance stats, as the report's per-symbol column shows them.
        private static IReadOnlyDictionary<string, ReportStats> BuildStatsBySymbol()
        {
            ReportStats apple = BuildStats();
            apple.NetProfit = 9000.5m;
            apple.Trades = 2;
            apple.WinRate = 1m;

            ReportStats microsoft = BuildStats();
            microsoft.NetProfit = 3345.178m;
            microsoft.Trades = 1;
            microsoft.WinRate = 0m;

            return new Dictionary<string, ReportStats>
            {
                { "AAPL", apple },
                { "MSFT", microsoft }
            };
        }

        private static IReadOnlyList<ReportRoundTrip> BuildRoundTrips()
        {
            return new List<ReportRoundTrip>
            {
                new ReportRoundTrip
                {
                    Number = 1,
                    Symbol = "AAPL",
                    Direction = "Long",
                    EntryTime = new DateTime(2024, 1, 2, 14, 30, 0, DateTimeKind.Utc),
                    ExitTime = new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc),
                    EntryPrice = 185.125m,
                    ExitPrice = 192.5m,
                    Quantity = 100,
                    RealizedPnL = 737.5m,
                    ReturnPercent = 0.03983794m,
                    TimeHeld = "3d 5h",
                    ExitReason = "Take-profit"
                },
                new ReportRoundTrip
                {
                    Number = 2,
                    Symbol = "AAPL",
                    Direction = "Long",
                    EntryTime = new DateTime(2024, 2, 12, 14, 30, 0, DateTimeKind.Utc),
                    ExitTime = new DateTime(2024, 2, 14, 20, 0, 0, DateTimeKind.Utc),
                    EntryPrice = 188m,
                    ExitPrice = 190.625m,
                    Quantity = 100,
                    RealizedPnL = 262.5m,
                    ReturnPercent = 0.01396277m,
                    TimeHeld = "2d 5h",
                    ExitReason = "Signal"
                },
                new ReportRoundTrip
                {
                    Number = 3,
                    Symbol = "MSFT",
                    Direction = "Short",
                    EntryTime = new DateTime(2024, 3, 4, 14, 30, 0, DateTimeKind.Utc),
                    ExitTime = new DateTime(2024, 3, 13, 15, 30, 0, DateTimeKind.Utc),
                    EntryPrice = 402.75m,
                    ExitPrice = 407.255m,
                    Quantity = 100,
                    RealizedPnL = -450.5m,
                    ReturnPercent = -0.01118560m,
                    TimeHeld = "9d 1h",
                    ExitReason = "Stop-loss"
                }
            };
        }

        private static IReadOnlyList<ReportRejectedOrder> BuildRejectedOrders()
        {
            return new List<ReportRejectedOrder>
            {
                new ReportRejectedOrder
                {
                    Symbol = "MSFT",
                    Direction = "Long",
                    Time = new DateTime(2024, 4, 8, 14, 30, 0, DateTimeKind.Utc),
                    Price = 421.375m,
                    Quantity = 250,
                    Reason = "Not enough funds"
                }
            };
        }

        private static IReadOnlyList<ReportCard> BuildConfiguration()
        {
            return new List<ReportCard>
            {
                new ReportCard
                {
                    Title = "MACD",
                    Headers = new List<string> { "Setting", "Value" },
                    Rows = new List<IReadOnlyList<string>>
                    {
                        new List<string> { "Fast period", "12" },
                        new List<string> { "Slow period", "26" }
                    }
                }
            };
        }

        private static ReportChart BuildChart()
        {
            // Key: symbol/ticker -> the candles the run executed on for that symbol.
            Dictionary<string, IReadOnlyList<ChartCandle>> series = new Dictionary<string, IReadOnlyList<ChartCandle>>
            {
                {
                    "AAPL",
                    new List<ChartCandle>
                    {
                        new ChartCandle { Time = 1704153600, Open = 184.25m, High = 186.5m, Low = 183.75m, Close = 185.125m }
                    }
                }
            };

            return new ReportChart { Series = series };
        }

        private static IReadOnlyList<ChartIndicator> BuildIndicators()
        {
            return new List<ChartIndicator>
            {
                new ChartIndicator
                {
                    Name = "RSI",
                    Symbol = "AAPL",
                    Pane = "separate",
                    Series = new List<ChartIndicatorSeries>
                    {
                        new ChartIndicatorSeries
                        {
                            Name = "RSI(14)",
                            Shape = "line",
                            Points = new List<ChartLinePoint> { new ChartLinePoint { Time = 1704153600, Value = 987.75m } }
                        }
                    }
                }
            };
        }

        private static IReadOnlyList<ReportEquityPoint> BuildEquityCurve()
        {
            return new List<ReportEquityPoint>
            {
                new ReportEquityPoint { Trade = 0, Equity = 100000m },
                new ReportEquityPoint { Trade = 1, Equity = 100737.5m }
            };
        }
    }
}
