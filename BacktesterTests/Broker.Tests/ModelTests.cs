using System;
using Backtester.Core;
using Backtester.ExecutionModels.Sizing;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class ModelTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static OrderRequest BuyRequest(string symbol, decimal price, int qty = 1)
        {
            return new()
            {
                Symbol = symbol,
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Price = price,
                Quantity = qty
            };
        }

        // --- RiskPerTradeSizing ---


        [Fact]
        public void RiskPerTradeSizing_ReturnsExpectedShares()
        {
            // 1% of $10,000 = $100 risk budget; stop distance = |$50 - $45| = $5 → floor($100/$5) = 20 shares
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_WiderStop_YieldsSmallerSize()
        {
            // Same equity and fraction; stop distance = |$50 - $40| = $10 → floor($100/$10) = 10 shares
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(10, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_UsesRealizedEquity_NotCashAlone()
        {
            // Buy 10@$100 → Cash=$9,000; cost-basis equity=$9,000+$1,000=$10,000
            // 2% of $10,000=$200 budget; stop distance=|$50-$40|=$10 → floor($200/$10)=20
            // If using cash only: floor(0.02×$9,000/$10)=18 — proves cost-basis base
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.02m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 10, Timestamp = DateTime.UtcNow });
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        // --- PercentNotionalSizing ---

        [Fact]
        public void PercentNotionalSizing_ReturnsCorrectQuantity()
        {
            // 10% of $10,000 = $1,000; at $50/share = 20 shares
            PercentNotionalSizing sizing = new() { Percent =0.10m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void PercentNotionalSizing_ReturnsZero_WhenCashTooLowForOneShare()
        {
            // 1% of $100 = $1; at $50/share = 0 shares
            PercentNotionalSizing sizing = new() { Percent =0.01m };
            Portfolio portfolio = new(100m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        [Fact]
        public void PercentNotionalSizing_SizesOffBuyingPower_NotCashAlone()
        {
            // Buy 100@$100 spends all cash (Cash=$0) but leaves buying power: MarkedEquity $10,000 −
            // committed margin (0.5×$10,000=$5,000) = $5,000. 10% of $5,000=$500 at $50/share = 10 shares.
            // If sized off cash ($0) this would be 0 — proving the buying-power base.
            PercentNotionalSizing sizing = new() { Percent = 0.10m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 100, Timestamp = T0 });
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0m, portfolio.Cash);
            Assert.Equal(10, qty);
        }

        [Fact]
        public void PercentNotionalSizing_ReturnsZero_WhenBuyingPowerExhausted()
        {
            // Buy 200@$100 deploys the full 2:1 long margin: Cash=−$10,000, MarkedEquity=$10,000,
            // committed margin=0.5×$20,000=$10,000 → BuyingPower=$0. With nothing left to open on, sizing
            // must return zero (never a negative share count from the net-borrowed cash balance).
            PercentNotionalSizing sizing = new() { Percent = 0.20m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 200, Timestamp = T0 });
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0m, portfolio.BuyingPower);
            Assert.True(portfolio.Cash < 0m);
            Assert.Equal(0, qty);
        }
    }
}
