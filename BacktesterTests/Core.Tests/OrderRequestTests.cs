using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class OrderRequestTests
    {
        [Fact]
        public void Copy_CarriesEveryProperty()
        {
            object metadata = new();
            OrderRequest request = new()
            {
                Symbol = "AAPL",
                Side = OrderSide.Sell,
                Type = OrderType.Limit,
                Price = 101.5m,
                Quantity = 42,
                StopPrice = 99m,
                StopOffset = 2.5m,
                Priority = 7,
                ClientMetadata = metadata
            };

            OrderRequest copy = request.Copy();

            Assert.Equal("AAPL", copy.Symbol);
            Assert.Equal(OrderSide.Sell, copy.Side);
            Assert.Equal(OrderType.Limit, copy.Type);
            Assert.Equal(101.5m, copy.Price);
            Assert.Equal(42, copy.Quantity);
            Assert.Equal(99m, copy.StopPrice);
            Assert.Equal(2.5m, copy.StopOffset);
            Assert.Equal(7, copy.Priority);
            Assert.Same(metadata, copy.ClientMetadata);
        }

        [Fact]
        public void Copy_WritingToTheCopyLeavesTheOriginalUntouched()
        {
            // The reason the copy exists: the broker writes its sized quantity and sizing offset onto the copy,
            // and the request a strategy holds and reuses across bars must not acquire either.
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 };

            OrderRequest copy = request.Copy();
            copy.Quantity = 100;
            copy.StopOffset = 5m;

            Assert.Equal(10, request.Quantity);
            Assert.Null(request.StopOffset);
        }

        [Fact]
        public void Copy_ReturnsADistinctInstance()
        {
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 };

            OrderRequest copy = request.Copy();

            Assert.NotSame(request, copy);
        }
    }
}
