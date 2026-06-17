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

        private static Candle Bar(DateTime ts, decimal close) => new()
        {
            Timestamp = ts,
            Open = close,
            High = close + 2m,
            Low = close - 2m,
            Close = close,
            Volume = 10_000
        };

        [Fact]
        public void MovingAverageCross_FullStack_ProducesTradesAndEquityHistory()
        {
            // Synthetic 10-bar series: [100,90,80,70,60,70,80,90,100,110]
            // Golden cross on bar 8 (index 7, price=90) → market buy fills at next Open=90
            decimal[] closes = new[] { 100m, 90m, 80m, 70m, 60m, 70m, 80m, 90m, 100m, 110m };
            Candle[] candles = closes
                .Select((c, i) => Bar(T0.AddDays(i), c))
                .ToArray();

            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(
                portfolio,
                commissionModel: new FixedCommission { Amount = 5m },
                slippageModel: new FixedSlippage { Amount = 0.10m },
                sizingModel: new FixedSizeModel { FixedSize = 10 },
                riskModel: new PortfolioRiskModel { MaxPortfolioHeatPercent = 1.0m });

            MovingAverageCrossStrategy strategy = new MovingAverageCrossStrategy(fastPeriod: 3, slowPeriod: 5);
            IMarketDataFeed feed = MarketDataSynchronizer.CreateFromSeries(
                new Dictionary<string, IReadOnlyList<Candle>> { ["AAPL"] = candles });

            BacktestEngine engine = new BacktestEngine(feed, strategy, broker, portfolio);
            engine.Start();

            // One entry per bar
            Assert.Equal(10, portfolio.EquityHistory.Count);

            // At least one trade was recorded
            Assert.NotEmpty(portfolio.Trades);

            // Trade carries commission and a realistic fill price
            Trade trade = portfolio.Trades[0];
            Assert.Equal(5m, trade.Commission);
            Assert.True(trade.Price > 0m);
            Assert.Equal(T0.AddDays(7), trade.Timestamp);

            // Final equity differs from starting cash (position was opened)
            decimal finalEquity = portfolio.EquityHistory.Last().TotalEquity;
            Assert.NotEqual(10_000m, finalEquity);
        }
    }
}
