using System;
using Backtester.Core;
using Backtester.Models.Commission;
using Backtester.Models.Risk;
using Backtester.Models.Sizing;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class ModelTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static OrderRequest BuyRequest(string symbol, decimal price, int qty = 1) => new()
        {
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Price = price,
            Quantity = qty
        };

        // --- RiskPercentSizing ---

        [Fact]
        public void RiskPercentSizing_ReturnsCorrectQuantity()
        {
            // 10% of $10,000 = $1,000; at $50/share = 20 shares
            RiskPercentSizing sizing = new RiskPercentSizing { RiskPercent = 0.10m };
            Portfolio portfolio = new Portfolio(10_000m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPercentSizing_ReturnsZero_WhenCashTooLowForOneShare()
        {
            // 1% of $100 = $1; at $50/share = 0 shares
            RiskPercentSizing sizing = new RiskPercentSizing { RiskPercent = 0.01m };
            Portfolio portfolio = new Portfolio(100m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        // --- PortfolioRiskModel ---

        [Fact]
        public void PortfolioRiskModel_ReturnsFalse_WhenEstimatedCostExceedsCash()
        {
            // portfolio has $500; trying to buy 10 @ $100 = $1,000 notional
            PortfolioRiskModel risk = new PortfolioRiskModel { MaxPortfolioHeatPercent = 1.0m };
            Portfolio portfolio = new Portfolio(500m);
            OrderRequest request = BuyRequest("AAPL", price: 100m, qty: 10);

            Assert.False(risk.Accept(request, portfolio));
        }

        [Fact]
        public void PortfolioRiskModel_ReturnsFalse_WhenHeatWouldBreachLimit()
        {
            // portfolio: $10,000 cash, max heat 20% → max open notional = $2,000
            // buying 30 @ $100 = $3,000 would push heat to 30% → reject
            PortfolioRiskModel risk = new PortfolioRiskModel { MaxPortfolioHeatPercent = 0.20m };
            Portfolio portfolio = new Portfolio(10_000m);
            OrderRequest request = BuyRequest("AAPL", price: 100m, qty: 30);

            Assert.False(risk.Accept(request, portfolio));
        }

        [Fact]
        public void PortfolioRiskModel_ReturnsTrue_WhenAffordableAndWithinHeat()
        {
            // $10,000 cash, max heat 50%, buying 10 @ $100 = $1,000 → heat 10% → accept
            PortfolioRiskModel risk = new PortfolioRiskModel { MaxPortfolioHeatPercent = 0.50m };
            Portfolio portfolio = new Portfolio(10_000m);
            OrderRequest request = BuyRequest("AAPL", price: 100m, qty: 10);

            Assert.True(risk.Accept(request, portfolio));
        }
    }
}
