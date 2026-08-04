using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Engine;
using Backtester.Report;
using Backtester.Strategies;
using FakeItEasy;
using BacktestEngine = Backtester.Engine.Engine;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    /// <summary>
    /// Pins the entry-rate and exit-rate semantics (ADR 0032) above the module seams: one end-to-end run
    /// whose conversion rate moves between a round trip's entry bar and its exit bar, driven through the
    /// Engine into a report model with only the data fetcher faked. Each module's own tests pin its
    /// arithmetic at one seam; only a run like this can show that figures translated at different moments
    /// meet correctly once they travel together.
    /// </summary>
    public class CrossCurrencyRateMoveTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Builds a candle with explicit OHLC, so a bar can gap away from its own close.</summary>
        private static Candle Bar(DateTime ts, decimal open, decimal high, decimal low, decimal close)
        {
            return new() { Timestamp = ts, Open = open, High = high, Low = low, Close = close, Volume = 1000 };
        }

        /// <summary>Builds a fake fetcher that returns the given candle series for each named symbol.</summary>
        private static IHistoricalDataFetcher FetcherReturning(params (string Symbol, IReadOnlyList<Candle> Candles)[] series)
        {
            IHistoricalDataFetcher fetcher = A.Fake<IHistoricalDataFetcher>();
            foreach ((string symbol, IReadOnlyList<Candle> candles) in series)
            {
                A.CallTo(() => fetcher.FetchAsync(symbol, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                    .Returns(Task.FromResult(candles));
            }

            return fetcher;
        }

        /// <summary>
        /// Runs the scenario every test here shares: a USD account of 10,000 trading JPY-quoted EUR_JPY
        /// through USD_JPY, whose rate moves from 100 to 125 between the trip's entry bar and its exit bar.
        /// <para>
        /// Bar 0 submits a bracket (Buy stop 14,950 for 10 units, stop leg 14,990, target leg 15,010) and
        /// closes USD_JPY at 100. Bar 1 opens at 15,000, above that stop, so the entry fills there — priced
        /// gap-aware — while the last completed conversion close is still bar 0's 100; it then closes
        /// USD_JPY at 125. Bar 2 opens at 15,020, above the 15,010 target, so the exit fills there against
        /// the 125 rate.
        /// </para>
        /// <para>
        /// So the trip risked 10 JPY per unit over 10 units — 100 JPY at a rate of 100 — and made 20 JPY
        /// per unit — 200 JPY at a rate of 125. Returns the run's report model paired with the Portfolio it
        /// was built from, since the initial risk behind the R-multiple is a domain figure the report
        /// carries only as R.
        /// </para>
        /// </summary>
        private static async Task<(ReportModel Report, Portfolio Portfolio)> RunRateMoveBetweenEntryAndExitAsync()
        {
            Candle[] eurJpyBars =
            {
                Bar(T0,             15_000m, 15_005m, 14_995m, 15_000m),
                Bar(T0.AddDays(1),  15_000m, 15_005m, 14_995m, 15_000m),
                Bar(T0.AddDays(2),  15_020m, 15_030m, 15_015m, 15_025m)
            };
            Candle[] usdJpyBars =
            {
                Bar(T0,             100m, 100m, 100m, 100m),
                Bar(T0.AddDays(1),  125m, 125m, 125m, 125m),
                Bar(T0.AddDays(2),  125m, 125m, 125m, 125m)
            };

            Instrument[] instruments =
            {
                new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY", MarginRate = 0.02m }
            };
            IHistoricalDataFetcher fetcher = FetcherReturning(("EUR_JPY", eurJpyBars), ("USD_JPY", usdJpyBars));
            Portfolio portfolio = new(10_000m, "USD", instruments);
            BrokerSimulator broker = new(portfolio);

            BacktestEngine engine = new(
                fetcher, new[] { "EUR_JPY" }, T0, T0.AddYears(1), "1d",
                new BracketedEntryOnFirstBar(entryStop: 14_950m, protectiveStop: 14_990m, target: 15_010m, quantity: 10),
                broker, portfolio);
            BacktestResult result = await engine.StartAsync();

            return (new ReportModelBuilder().Build(result), portfolio);
        }

        /// <summary>
        /// Runs the scenario and returns the one round trip it reached the report as — the seam most of
        /// these tests assert through, since the report is where a translated figure is finally read.
        /// </summary>
        private static async Task<ReportRoundTrip> ReportedTripAsync()
        {
            (ReportModel report, Portfolio _) = await RunRateMoveBetweenEntryAndExitAsync();

            return Assert.Single(report.RoundTrips);
        }

        /// <summary>
        /// Runs the scenario and returns the one round trip the Portfolio recorded, for the entry-rate
        /// figures the report carries only indirectly.
        /// </summary>
        private static async Task<RoundTrip> RecordedTripAsync()
        {
            (ReportModel _, Portfolio portfolio) = await RunRateMoveBetweenEntryAndExitAsync();

            return Assert.Single(portfolio.RoundTrips);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_ReportsRAgainstTheRiskFrozenAtTheEntryRate()
        {
            // The figure the whole scenario exists to pin: 100 JPY risked at 100 is $1.00, 200 JPY made at
            // 125 is $1.60, so R is 1.6. Translating both figures at either single rate gives 2.0 — the
            // exit rate shrinks the risk to $0.80, the entry rate inflates the profit to $2.00 — so 1.6 is
            // exactly the value that dies the moment anything retranslates a historical entry figure.
            ReportRoundTrip trip = await ReportedTripAsync();

            Assert.Equal(1.6m, trip.RMultiple);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_FreezesInitialRiskAtTheEntryRate()
        {
            // The denominator of that R, on its own: 10 JPY per unit over 10 units is 100 JPY, and the rate
            // in force as the position opened was 100, so the trip carries $1.00 — what a broker would have
            // said was at risk as it was entered. The rate's later move to 125 leaves it alone; retranslated
            // there it would read $0.80.
            RoundTrip trip = await RecordedTripAsync();

            Assert.Equal(1.00m, trip.InitialRisk);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_ReportsRealizedProfitAtTheExitRate()
        {
            // The numerator, and the half of R that is meant to move with the rate: 20 JPY per unit over 10
            // units is 200 JPY, converted as the trip closed at 125 — $1.60, the money that actually reached
            // the account. The entry rate would claim $2.00 for a position that never earned it.
            ReportRoundTrip trip = await ReportedTripAsync();

            Assert.Equal(1.60m, trip.RealizedPnL);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_ReportsLeverageFromTheEntryRateNotional()
        {
            // Both sides of the ratio come from the entry bar: 10 units at 15,000 is 150,000 JPY, which left
            // the account as $1,500 at the rate of 100, against the $10,000 marked equity the account held
            // as the position opened. 0.15 — the exit rate would report the same capital as $1,200 and the
            // trip as a 0.12 one.
            ReportRoundTrip trip = await ReportedTripAsync();

            Assert.Equal(0.15m, trip.Leverage);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_ReportsMarginFromTheEntryRateNotional()
        {
            // The same entry-rate notional at the Instrument's own 0.02 rate: $30 committed as the trip
            // opened, frozen there rather than re-marked as the rate moved. Reg-T's long 0.5 would say $750,
            // and the exit rate $24 — neither is what this account posted.
            ReportRoundTrip trip = await ReportedTripAsync();

            Assert.Equal(30m, trip.Margin);
        }

        [Fact]
        public async Task RateMoveBetweenEntryAndExit_RecordsNativeGapAwarePricesOnBothSides()
        {
            // Translation moves money, never execution semantics (ADR 0024): both prices stay in EUR_JPY's
            // own quote currency and both are the bar's open, the level the market gapped to past a trigger
            // it never traded at — 15,000 rather than the 14,950 entry stop, 15,020 rather than the 15,010
            // target. Neither rate produces either figure, at any moment.
            ReportRoundTrip trip = await ReportedTripAsync();

            Assert.Equal(15_000m, trip.EntryPrice);
            Assert.Equal(15_020m, trip.ExitPrice);
            Assert.Equal("JPY", trip.QuoteCurrency);
        }

        /// <summary>
        /// Submits one bracketed Buy stop the first time it sees any bar, then never trades again — so the
        /// run's single round trip, and every figure stamped on it, is attributable to that entry alone.
        /// </summary>
        private sealed class BracketedEntryOnFirstBar : StrategyBase
        {
            private readonly decimal _entryStop;
            private readonly decimal _protectiveStop;
            private readonly decimal _target;
            private readonly int _quantity;
            private bool _submitted;

            public BracketedEntryOnFirstBar(decimal entryStop, decimal protectiveStop, decimal target, int quantity)
            {
                _entryStop = entryStop;
                _protectiveStop = protectiveStop;
                _target = target;
                _quantity = quantity;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_submitted)
                {
                    return;
                }

                _submitted = true;
                broker.SubmitBracket(new BracketRequest
                {
                    Entry = new OrderRequest
                    {
                        Symbol = symbol,
                        Side = OrderSide.Buy,
                        Type = OrderType.Stop,
                        Price = _entryStop,
                        Quantity = _quantity
                    },
                    StopPrice = _protectiveStop,
                    TargetPrice = _target
                });
            }
        }
    }
}
