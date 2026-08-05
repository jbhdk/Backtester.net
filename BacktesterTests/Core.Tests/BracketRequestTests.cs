using System;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class BracketRequestTests
    {
        private static OrderRequest Entry()
        {
            return new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 };
        }

        [Fact]
        public void Construction_CarriesTheEntryAndTheLegsGiven_LeavingAnUnrequestedLegNull()
        {
            OrderRequest entry = Entry();
            BracketLegSpec stop = BracketLegSpec.AtPrice(95m);

            BracketRequest request = new(entry, stopLeg: stop);

            Assert.Same(entry, request.Entry);
            Assert.Same(stop, request.StopLeg);
            Assert.Null(request.TargetLeg);
        }

        [Fact]
        public void Construction_NoLegAtAll_Throws()
        {
            // A bracket with neither a stop nor a target is caller misuse — an unprotected entry is a
            // plain Submit, not a bracket (ADR 0002).
            ArgumentException exception = Assert.Throws<ArgumentException>(() => new BracketRequest(Entry()));

            Assert.StartsWith("A bracket must have at least one leg (a stop-loss and/or a take-profit).", exception.Message);
        }

        [Fact]
        public void Construction_NoEntry_Throws()
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => new BracketRequest(null, stopLeg: BracketLegSpec.AtPrice(95m)));

            Assert.Equal("entry", exception.ParamName);
        }
    }
}
