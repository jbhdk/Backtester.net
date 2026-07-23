using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    /// Integration behaviour of priming with the Optimizer: the Optimizer fetches through the real
    /// cache-aware <see cref="HistoricalDataFetcher"/>, so priming a wide range once lets in-sample and
    /// out-of-sample sweeps run entirely from the warm Cache, and an un-primed earlier window fails loudly
    /// with the inherited coverage guard. Priming is a caller step; the Optimizer's API is unchanged.
    /// </summary>
    public class OptimizerPrimingIntegrationTests
    {
        [Fact]
        public async Task PrimedWideRange_InSampleAndOutOfSampleSweeps_CallProviderOnlyDuringPriming()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime wideFrom = now.AddYears(-2);
            DateTime split = now.AddYears(-1);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(wideFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);

            // Prime the wide range once (no backtest runs here).
            await fetcher.PrimeAsync(new[] { "AAPL" }, wideFrom, now, "1d");

            // In-sample then out-of-sample sweeps over sub-ranges of the primed range.
            await BuySellOptimizer(fetcher, wideFrom, split).RunAsync();
            await BuySellOptimizer(fetcher, split, now).RunAsync();

            // Only the prime called the Provider; both sweeps read the warm Cache.
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Sweep_OverWindowBeforeCoverageFloor_ThrowsInheritedCoverageGuard()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime laterFrom = now.AddYears(-1);
            DateTime earlierFrom = now.AddYears(-2);

            IHistoricalDataProvider provider = ProviderReturning(WeeklySeries(laterFrom, now));
            HistoricalDataFetcher fetcher = new(provider, tmp);

            // Warm only the later window, establishing the coverage floor at laterFrom.
            await BuySellOptimizer(fetcher, laterFrom, now).RunAsync();

            // An earlier, un-primed out-of-sample window precedes the floor: the guard fires through the
            // Optimizer's fetch-once step rather than sweeping on a silently short slice.
            await Assert.ThrowsAsync<DataCoverageException>(
                () => BuySellOptimizer(fetcher, earlierFrom, laterFrom).RunAsync());
        }

        /// <summary>Builds an Optimizer over one symbol and a two-point grid running a buy-then-sell strategy over the given window.</summary>
        private static Optimizer BuySellOptimizer(IHistoricalDataFetcher fetcher, DateTime fromUtc, DateTime toUtc)
        {
            ParameterSpace space = new ParameterSpace().AddInt("qty", from: 1, to: 2, step: 1);
            return new Optimizer(
                fetcher,
                new[] { "AAPL" },
                fromUtc,
                toUtc,
                "1d",
                () => new Portfolio(100_000m),
                space,
                (parameters, portfolio) => (new BuyThenSellStrategy(parameters.Int("qty")), new BrokerSimulator(portfolio)),
                minimumTrades: 0);
        }

        /// <summary>A fake provider that returns the given series for any symbol and range.</summary>
        private static IHistoricalDataProvider ProviderReturning(IReadOnlyList<Candle> series)
        {
            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(series));
            return provider;
        }

        /// <summary>A rising weekly OHLCV series spanning the inclusive range.</summary>
        private static IReadOnlyList<Candle> WeeklySeries(DateTime fromUtc, DateTime toUtc)
        {
            List<Candle> bars = new();
            decimal price = 100m;
            for (DateTime ts = fromUtc; ts <= toUtc; ts = ts.AddDays(7))
            {
                bars.Add(new Candle { Timestamp = ts, Open = price, High = price + 2, Low = price - 2, Close = price, Volume = 1000 });
                price += 1m;
            }

            return bars;
        }

        private static string NewTempFolder()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_optimizer_prime_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            return tmp;
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }

        /// <summary>Buys a fixed quantity on a symbol's first bar and sells it on the next, for one round trip per symbol.</summary>
        private sealed class BuyThenSellStrategy : StrategyBase
        {
            private readonly int _quantity;
            private bool _bought;
            private bool _sold;

            public BuyThenSellStrategy(int quantity)
            {
                _quantity = quantity;
            }

            public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
            {
                if (!_bought)
                {
                    _bought = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market, Quantity = _quantity });
                }
                else if (!_sold)
                {
                    _sold = true;
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market, Quantity = _quantity });
                }
            }
        }
    }
}
