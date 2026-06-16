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
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class BrokerSimulatorTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static MarketSlice SliceWithBar(string symbol, decimal close) => new()
        {
            Timestamp = T0,
            BarsBySymbol = new Dictionary<string, Candle>
            {
                [symbol] = new Candle { Timestamp = T0, Open = close, High = close, Low = close, Close = close, Volume = 1000 }
            }
        };

        private static OrderRequest MarketBuy(string symbol, int qty) => new()
        {
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = qty
        };

        [Fact]
        public void ProcessBar_WithNoOrders_ReturnsEmpty()
        {
            var broker = new BrokerSimulator(new Portfolio(10_000m));

            var trades = broker.ProcessBar(SliceWithBar("AAPL", 150m));

            Assert.Empty(trades);
        }

        [Fact]
        public void SubmitOrder_DoesNotThrow()
        {
            var broker = new BrokerSimulator(new Portfolio(10_000m));

            var id = broker.SubmitOrder(MarketBuy("AAPL", 10));

            Assert.NotNull(id);
        }

        [Fact]
        public void ProcessBar_MarketBuy_FillsAtClose_ReturnsTrade()
        {
            var portfolio = new Portfolio(10_000m);
            var broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            var trades = broker.ProcessBar(SliceWithBar("AAPL", 102m)).ToList();

            Assert.Single(trades);
            Assert.Equal(102m, trades[0].Price);
            Assert.Equal(10, trades[0].Quantity);
            Assert.Equal("AAPL", trades[0].Symbol);
            Assert.Equal(OrderSide.Buy, trades[0].Side);
        }

        [Fact]
        public void ProcessBar_AppliesTrade_PortfolioUpdated()
        {
            var portfolio = new Portfolio(10_000m);
            var broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            Assert.Equal(9_000m, portfolio.Cash);
            Assert.Single(portfolio.Positions);
        }

        [Fact]
        public void ProcessBar_DrainsPendingOrders_DoesNotRefillNextBar()
        {
            var broker = new BrokerSimulator(new Portfolio(10_000m));
            broker.SubmitOrder(MarketBuy("AAPL", 10));
            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            var secondBarTrades = broker.ProcessBar(SliceWithBar("AAPL", 105m)).ToList();

            Assert.Empty(secondBarTrades);
        }

        [Fact]
        public void ProcessBar_MultipleOrders_FillsAll()
        {
            var portfolio = new Portfolio(10_000m);
            var broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 5));
            broker.SubmitOrder(MarketBuy("AAPL", 3));

            var trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Equal(2, trades.Count);
        }

        [Fact]
        public void ProcessBar_WithCommissionAndSlippage_TradeCarriesNonZeroValues()
        {
            // Market buy at Open=100; 1% slippage → fill at 101; 0.5% commission on notional 101×10 = $5.05
            var portfolio = new Portfolio(10_000m);
            var broker = new BrokerSimulator(
                portfolio,
                commissionModel: new PercentCommission { Percent = 0.005m },
                slippageModel: new PercentSlippage { Percent = 0.01m });

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
            var slice = new MarketSlice
            {
                Timestamp = T0,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000 }
                }
            };

            var trades = broker.ProcessBar(slice).ToList();

            // fill: Open=100 + 1% slippage → price=101, slippage=1
            // commission: 0.5% × (101 × 10) = 5.05
            Assert.Single(trades);
            Assert.Equal(101m, trades[0].Price);
            Assert.Equal(1m, trades[0].Slippage);
            Assert.Equal(5.05m, trades[0].Commission);
        }

        [Fact]
        public void SubmitOrder_WithSizingModel_OverridesRequestedQuantity()
        {
            // 10% of $10,000 at $100/share = 10 shares, regardless of the 1 in the request
            var portfolio = new Portfolio(10_000m);
            var sizing = new RiskPercentSizing { RiskPercent = 0.10m };
            var broker = new BrokerSimulator(portfolio, sizingModel: sizing);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Price = 100m, Quantity = 1 });
            var trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Single(trades);
            Assert.Equal(10, trades[0].Quantity);
        }

        [Fact]
        public void SubmitOrder_RejectedByRiskModel_ProducesNoTrade()
        {
            // portfolio $500, risk model blocks orders costing > cash, buying 10@$100 = $1000 → rejected
            var portfolio = new Portfolio(500m);
            var risk = new PortfolioRiskModel { MaxPortfolioHeatPercent = 1.0m };
            var broker = new BrokerSimulator(portfolio, riskModel: risk);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Price = 100m, Quantity = 10 });
            var trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Empty(trades);
        }

        [Fact]
        public void BrokerSimulator_DefaultModel_MarketOrder_FillsAtOpen_NotClose()
        {
            var portfolio = new Portfolio(10_000m);
            var broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 1));

            var slice = new MarketSlice
            {
                Timestamp = T0,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 115m, Low = 95m, Close = 110m, Volume = 1000 }
                }
            };
            var trades = broker.ProcessBar(slice).ToList();

            Assert.Single(trades);
            Assert.Equal(100m, trades[0].Price);
        }
    }
}
