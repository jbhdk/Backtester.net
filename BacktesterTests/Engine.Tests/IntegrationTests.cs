using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Models.Commission;
using Backtester.Models.Risk;
using Backtester.Models.Sizing;
using Backtester.Models.Slippage;
using Backtester.Strategies;
using BacktestEngine = Backtester.Engine.Engine;
using Xunit;

namespace BacktesterTests.Engine.Tests
{
    public class IntegrationTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts, decimal close)
        {
            return new()
            {
                Timestamp = ts,
                Open = close,
                High = close + 2m,
                Low = close - 2m,
                Close = close,
                Volume = 10_000
            };
        }


        [Fact]
        public void MovingAverageCross_FullStack_ProducesTradesAndEquityHistory()
        {
            // Synthetic 10-bar series: [100,90,80,70,60,70,80,90,100,110]
            // Golden cross fires during bar index 7 (price=90) → market buy fills at bar index 8's Open=100
            decimal[] closes = new[] { 100m, 90m, 80m, 70m, 60m, 70m, 80m, 90m, 100m, 110m };
            Candle[] candles = closes
                .Select((c, i) => Bar(T0.AddDays(i), c))
                .ToArray();

            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(
                portfolio,
                commissionModel: new FixedCommission { Amount = 5m },
                slippageModel: new FixedSlippage { Amount = 0.10m },
                sizingModel: new FixedSizeModel { FixedSize = 10 },
                riskModel: new PortfolioRiskModel { MaxPortfolioHeatPercent = 1.0m });

            MovingAverageCrossStrategy strategy = new(fastPeriod: 3, slowPeriod: 5);
            IMarketDataFeed feed = MarketDataSynchronizer.CreateFromSeries(
                new Dictionary<string, IReadOnlyList<Candle>> { ["AAPL"] = candles });

            BacktestEngine engine = new(feed, strategy, broker, portfolio);
            engine.Start();

            // One entry per bar
            Assert.Equal(10, portfolio.EquityHistory.Count);

            // At least one trade was recorded
            Assert.NotEmpty(portfolio.Trades);

            // Trade carries commission and a realistic fill price
            Trade trade = portfolio.Trades[0];
            Assert.Equal(5m, trade.Commission);
            Assert.True(trade.Price > 0m);
            Assert.Equal(T0.AddDays(8), trade.Timestamp);

            // Final equity differs from starting cash (position was opened)
            decimal finalEquity = portfolio.EquityHistory.Last().MarkedEquity;
            Assert.NotEqual(10_000m, finalEquity);
        }

        [Fact]
        public void AtrBracket_TwoSymbols_OneEntersOnce_OtherEntersTwice()
        {
            // AAPL — flat throughout (H=103, L=97). ATR=6, stop=94, target=112. Neither ever hit.
            //   1 trade (entry fill only); position open at end; 0 round trips.
            //
            // MSFT — flat bars 0–9 (ATR=6, stop=194, target=212), spike at bar 10 (H=220→target hits),
            //   re-entry on bar 10 at Close=215 (stop2=209, target2=227), spike at bar 14 (H=235→target2 hits).
            //   4 trades (entry1+target1+entry2+target2); 2 round trips.

            static Candle MakeBar(DateTime ts, decimal o, decimal h, decimal l, decimal c) => new()
            {
                Timestamp = ts,
                Open = o,
                High = h,
                Low = l,
                Close = c,
                Volume = 10_000
            };

            Candle[] aaplBars = Enumerable.Range(0, 15)
                .Select(i => MakeBar(T0.AddDays(i), 100m, 103m, 97m, 100m))
                .ToArray();

            Candle[] msftBars = Enumerable.Range(0, 15)
                .Select(i => i switch
                {
                    10 => MakeBar(T0.AddDays(i), 200m, 220m, 197m, 215m),
                    11 => MakeBar(T0.AddDays(i), 215m, 218m, 212m, 215m),
                    12 => MakeBar(T0.AddDays(i), 215m, 218m, 212m, 215m),
                    13 => MakeBar(T0.AddDays(i), 215m, 218m, 212m, 215m),
                    14 => MakeBar(T0.AddDays(i), 215m, 235m, 212m, 230m),
                    _ => MakeBar(T0.AddDays(i), 200m, 203m, 197m, 200m)
                })
                .ToArray();

            Portfolio portfolio = new(100_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new FixedSizeModel { FixedSize = 1 });

            AtrBracketStrategy strategy = new(atrPeriod: 5);
            IMarketDataFeed feed = MarketDataSynchronizer.CreateFromSeries(
                new Dictionary<string, IReadOnlyList<Candle>>
                {
                    ["AAPL"] = aaplBars,
                    ["MSFT"] = msftBars
                });

            BacktestEngine engine = new(feed, strategy, broker, portfolio);
            engine.Start();

            // AAPL entered once, position still open
            Assert.Equal(1, portfolio.Trades.Count(t => t.Symbol == "AAPL"));
            Assert.Contains(portfolio.Positions, p => p.Symbol == "AAPL" && p.Quantity > 0);

            // MSFT entered twice: 2 entry fills + 2 target fills
            Assert.Equal(4, portfolio.Trades.Count(t => t.Symbol == "MSFT"));

            // Two MSFT round trips in performance stats
            PerformanceStats stats = portfolio.GetPerformanceStats();
            Assert.Equal(2, stats.Trades);
            Assert.True(stats.NetProfit > 0);
        }
    }
}
