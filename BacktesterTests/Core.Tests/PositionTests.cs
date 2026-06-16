using System;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class PositionTests
    {
        private static Trade Buy(decimal price, int qty, decimal commission = 0m) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Price = price,
            Quantity = qty,
            Commission = commission,
            Timestamp = DateTime.UtcNow
        };

        private static Trade Sell(decimal price, int qty, decimal commission = 0m) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Price = price,
            Quantity = qty,
            Commission = commission,
            Timestamp = DateTime.UtcNow
        };

        [Fact]
        public void AddTrade_FirstBuy_SetsQuantityAndAveragePrice()
        {
            var position = new Position { Symbol = "AAPL" };

            position.AddTrade(Buy(100m, 10));

            Assert.Equal(10, position.Quantity);
            Assert.Equal(100m, position.AveragePrice);
        }

        [Fact]
        public void AddTrade_SecondBuy_AccumulatesQuantityAndUpdatesVwap()
        {
            var position = new Position { Symbol = "AAPL" };
            position.AddTrade(Buy(100m, 10));

            position.AddTrade(Buy(110m, 10));

            Assert.Equal(20, position.Quantity);
            Assert.Equal(105m, position.AveragePrice);
        }

        [Fact]
        public void AddTrade_Sell_ReducesQuantityAndKeepsCostBasis()
        {
            var position = new Position { Symbol = "AAPL" };
            position.AddTrade(Buy(100m, 10));

            position.AddTrade(Sell(120m, 5));

            Assert.Equal(5, position.Quantity);
            Assert.Equal(100m, position.AveragePrice);
        }
    }
}
