using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Optimization;
using Backtester.Strategies;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Reproduction probe for cross-Trial contamination of a retained <see cref="BacktestResult"/> when the
    /// Optimizer runs its Trials in parallel. Every Trial here runs the identical, parameter-independent set
    /// of Round trips over the same shared bars, differing only by an inert probe axis. In isolation each
    /// Trial's portfolio must end with exactly its own Round trips; if a parallel Trial's Round trips leak
    /// into another Trial's retained result, the count inflates and exact duplicates appear — the symptom
    /// seen in a real sweep's winner report.
    /// </summary>
    public class OptimizerParallelIsolationTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new() { Timestamp = ts, Open = close, High = close + 2, Low = close - 2, Close = close, Volume = 1000 };
        }

        /// <summary>A rising AAPL series of <paramref name="bars"/> bars from 100 in +10 steps, shared read-only by every Trial.</summary>
        private static IHistoricalDataFetcher RisingSeriesFetcher(int bars)
        {
            List<Candle> candles = new();
            for (int index = 0; index < bars; index++)
            {
                candles.Add(Bar(T0.AddDays(index), 100m + 10m * index));
            }

            IHistoricalDataFetcher fetcher = A.Fake<IHistoricalDataFetcher>();
            A.CallTo(() => fetcher.FetchAsync("AAPL", A<DateTime>._, A<DateTime>._, A<string>._, A<System.Threading.CancellationToken>._))
                .Returns(Task.FromResult<IReadOnlyList<Candle>>(candles));
            return fetcher;
        }

        /// <summary>
        /// The retained result of every parallel Trial holds exactly that Trial's own Round trips — no count
        /// inflation and no exact duplicates leaked from a concurrently running Trial.
        /// </summary>
        [Fact]
        public async Task RunAsync_ParallelTrials_EachRetainedResultHoldsOnlyItsOwnRoundTrips()
        {
            const int roundTripsPerTrial = 100;
            const int trials = 128;
            IHistoricalDataFetcher fetcher = RisingSeriesFetcher(bars: roundTripsPerTrial * 2 + 2);

            // An inert "probe" axis: it changes the strategy's structural fingerprint (so the unconsumed-axis
            // guard accepts it) without changing what the strategy trades, so every Trial runs identically.
            ParameterSpace space = new ParameterSpace().AddInt("probe", from: 1, to: trials, step: 1);
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                T0,
                T0.AddYears(5),
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) =>
                    (new ProbeRoundTripStrategy(roundTripsPerTrial, parameters.Int("probe")), new BrokerSimulator(portfolio)),
                retainAllBacktestResults: true);

            OptimizationResult result = await optimizer.RunAsync();

            Assert.Equal(trials, result.Trials.Count);
            foreach (Trial trial in result.Trials)
            {
                IReadOnlyList<RoundTrip> roundTrips = trial.BacktestResult.Portfolio.RoundTrips;

                // No round trips leaked in from a concurrent Trial: the count is exactly this Trial's own.
                Assert.Equal(roundTripsPerTrial, roundTrips.Count);

                // And none are exact duplicates (same entry/exit bar and prices), the leak's fingerprint.
                int distinct = roundTrips
                    .Select(trip => (trip.EntryTime, trip.ExitTime, trip.EntryPrice, trip.ExitPrice))
                    .Distinct()
                    .Count();
                Assert.Equal(roundTrips.Count, distinct);
            }
        }

        /// <summary>
        /// The bracketed counterpart: every parallel Trial runs the identical set of <em>bracketed</em> Round
        /// trips (a market entry with a resting protective stop, flattened a bar later so the resting leg is
        /// cancelled), exercising the broker's bracket/OCO/leg state — the machinery the real contaminated
        /// trades used. In isolation each Trial's retained result holds exactly its own Round trips.
        /// </summary>
        [Fact]
        public async Task RunAsync_ParallelBracketedTrials_EachRetainedResultHoldsOnlyItsOwnRoundTrips()
        {
            const int roundTripsPerTrial = 80;
            const int trials = 128;
            IHistoricalDataFetcher fetcher = RisingSeriesFetcher(bars: roundTripsPerTrial * 2 + 2);

            ParameterSpace space = new ParameterSpace().AddInt("probe", from: 1, to: trials, step: 1);
            Optimizer optimizer = new(
                fetcher,
                new[] { "AAPL" },
                T0,
                T0.AddYears(5),
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) =>
                    (new BracketedProbeStrategy(roundTripsPerTrial, parameters.Int("probe")), new BrokerSimulator(portfolio)),
                retainAllBacktestResults: true);

            OptimizationResult result = await optimizer.RunAsync();

            Assert.Equal(trials, result.Trials.Count);
            foreach (Trial trial in result.Trials)
            {
                IReadOnlyList<RoundTrip> roundTrips = trial.BacktestResult.Portfolio.RoundTrips;
                Assert.Equal(roundTripsPerTrial, roundTrips.Count);

                int distinct = roundTrips
                    .Select(trip => (trip.EntryTime, trip.ExitTime, trip.EntryPrice, trip.ExitPrice))
                    .Distinct()
                    .Count();
                Assert.Equal(roundTrips.Count, distinct);
            }
        }

        /// <summary>
        /// Performs exactly <c>roundTrips</c> buy-then-sell cycles at quantity one, one order per bar, so a
        /// rising series yields that many completed Round trips. Carries an inert <c>probe</c> in a field so
        /// Trials differ structurally (satisfying the Optimizer's unconsumed-axis guard) without changing what
        /// is traded — every Trial's Round trips are therefore identical.
        /// </summary>
        private sealed class ProbeRoundTripStrategy : StrategyBase
        {
            private readonly int _roundTrips;
            private readonly int _probe;
            private int _ordersSubmitted;

            public ProbeRoundTripStrategy(int roundTrips, int probe)
            {
                _roundTrips = roundTrips;
                _probe = probe;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_ordersSubmitted >= _roundTrips * 2)
                {
                    return;
                }

                OrderSide side = _ordersSubmitted % 2 == 0 ? OrderSide.Buy : OrderSide.Sell;
                broker.Submit(new OrderRequest { Symbol = symbol, Side = side, Type = OrderType.Market, Quantity = 1 });
                _ordersSubmitted++;
            }
        }

        /// <summary>
        /// Performs exactly <c>roundTrips</c> bracketed buy-then-flatten cycles at quantity one: a market
        /// entry with a far protective stop (never hit on the rising series), then a market sell a bar later
        /// that flattens the position and leaves the stop leg for the broker to cancel. Exercises the bracket,
        /// OCO, and resting-leg paths. Carries an inert <c>probe</c> so Trials differ structurally without
        /// changing what is traded.
        /// </summary>
        private sealed class BracketedProbeStrategy : StrategyBase
        {
            private readonly int _roundTrips;
            private readonly int _probe;
            private int _ordersSubmitted;

            public BracketedProbeStrategy(int roundTrips, int probe)
            {
                _roundTrips = roundTrips;
                _probe = probe;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (_ordersSubmitted >= _roundTrips * 2)
                {
                    return;
                }

                if (_ordersSubmitted % 2 == 0)
                {
                    // Open with a bracket: a market buy plus a protective stop 50 below the fill, far enough
                    // below the rising series that it never triggers — the flatten closes the trade instead.
                    broker.SubmitBracket(new BracketRequest
                    {
                        Entry = new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 },
                        StopOffset = 50m,
                        TargetPrice = null
                    });
                }
                else
                {
                    // Flatten with a plain market sell, leaving the bracket's resting stop for the broker to cancel.
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 1 });
                }

                _ordersSubmitted++;
            }
        }
    }
}
