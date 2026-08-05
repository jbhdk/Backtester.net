using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class BracketTests
    {
        private static Bracket AttachedBracket(BracketRequest request, int quantity = 100)
        {
            Bracket bracket = Bracket.Create(request);
            bracket.AttachEntry("entry-1", quantity);
            return bracket;
        }

        private static BracketLegPlacement LegOf(IReadOnlyList<BracketLegPlacement> placements, BracketLeg leg)
        {
            return placements.FirstOrDefault(placement => placement.Leg == leg);
        }

        [Fact]
        public void Arm_AbsoluteStopLeg_RestsAtItsOwnPrice()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(101.25m, OrderSide.Buy);

            Assert.Equal(95m, LegOf(legs, BracketLeg.StopLoss).Price);
        }

        [Fact]
        public void Arm_OffsetStopLegOnLongEntry_RestsBelowTheFillByTheOffset()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopOffset = 2m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(101.25m, OrderSide.Buy);

            Assert.Equal(99.25m, LegOf(legs, BracketLeg.StopLoss).Price);
        }

        [Fact]
        public void Arm_OffsetStopLegOnShortEntry_RestsAboveTheFillByTheOffset()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopOffset = 2m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(101.25m, OrderSide.Sell);

            Assert.Equal(103.25m, LegOf(legs, BracketLeg.StopLoss).Price);
        }

        [Fact]
        public void Arm_OffsetTargetLegOnLongEntry_RestsAboveTheFillByTheOffset()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { TargetOffset = 3m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(101.25m, OrderSide.Buy);

            Assert.Equal(104.25m, LegOf(legs, BracketLeg.TakeProfit).Price);
        }

        [Fact]
        public void Arm_OffsetTargetLegOnShortEntry_RestsBelowTheFillByTheOffset()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { TargetOffset = 3m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(101.25m, OrderSide.Sell);

            Assert.Equal(98.25m, LegOf(legs, BracketLeg.TakeProfit).Price);
        }

        [Fact]
        public void Arm_LongEntry_ArmsSellLegs()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m, TargetPrice = 110m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.All(legs, placement => Assert.Equal(OrderSide.Sell, placement.Side));
        }

        [Fact]
        public void Arm_ShortEntry_ArmsBuyLegs()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 105m, TargetPrice = 90m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Sell);

            Assert.All(legs, placement => Assert.Equal(OrderSide.Buy, placement.Side));
        }

        [Fact]
        public void Arm_StopLeg_IsAStopOrder()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.Equal(OrderType.Stop, LegOf(legs, BracketLeg.StopLoss).Type);
        }

        [Fact]
        public void Arm_TargetLeg_IsALimitOrder()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { TargetPrice = 110m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.Equal(OrderType.Limit, LegOf(legs, BracketLeg.TakeProfit).Type);
        }

        [Fact]
        public void Arm_StopOnlyBracket_PlacesOnlyTheStopLeg()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.Equal(BracketLeg.StopLoss, Assert.Single(legs).Leg);
        }

        [Fact]
        public void Arm_TargetOnlyBracket_PlacesOnlyTheTargetLeg()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { TargetPrice = 110m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.Equal(BracketLeg.TakeProfit, Assert.Single(legs).Leg);
        }

        [Fact]
        public void Arm_LegsCoverTheAttachedEntryQuantity()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m, TargetPrice = 110m }, quantity: 37);

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.All(legs, placement => Assert.Equal(37, placement.Quantity));
        }

        [Fact]
        public void Arm_TwoLeggedBracket_GivesEachLegItsOwnOrderId()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m, TargetPrice = 110m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.NotEqual(LegOf(legs, BracketLeg.StopLoss).OrderId, LegOf(legs, BracketLeg.TakeProfit).OrderId);
        }

        [Fact]
        public void Arm_PopulatesTheHandleWithTheLegOrderIds()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m, TargetPrice = 110m });

            IReadOnlyList<BracketLegPlacement> legs = bracket.Arm(100m, OrderSide.Buy);

            Assert.Equal(LegOf(legs, BracketLeg.StopLoss).OrderId, bracket.Handle.StopOrderId);
            Assert.Equal(LegOf(legs, BracketLeg.TakeProfit).OrderId, bracket.Handle.TargetOrderId);
        }

        [Fact]
        public void Arm_StopOnlyBracket_LeavesTheHandlesTargetOrderIdNull()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            bracket.Arm(100m, OrderSide.Buy);

            Assert.Null(bracket.Handle.TargetOrderId);
        }

        [Fact]
        public void AttachEntry_PutsTheEntryOrderIdOnTheHandle()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            Assert.Equal("entry-1", bracket.Handle.EntryOrderId);
        }

        [Fact]
        public void Owns_PendingBracket_OwnsItsEntryOrderId()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            Assert.True(bracket.Owns("entry-1"));
        }

        [Fact]
        public void Owns_PendingBracket_DoesNotOwnAnotherOrderId()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            Assert.False(bracket.Owns("some-other-order"));
        }

        [Fact]
        public void Owns_ArmedBracket_NoLongerOwnsItsEntryOrderId()
        {
            Bracket bracket = AttachedBracket(new BracketRequest { StopPrice = 95m });

            bracket.Arm(100m, OrderSide.Buy);

            Assert.False(bracket.Owns("entry-1"));
        }

        [Fact]
        public void Create_StopLegGivenInBothForms_Throws()
        {
            BracketRequest request = new() { StopPrice = 95m, StopOffset = 2m };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.StartsWith("The stop leg cannot be given as both an absolute price and an offset.", exception.Message);
        }

        [Fact]
        public void Create_TargetLegGivenInBothForms_Throws()
        {
            BracketRequest request = new() { TargetPrice = 110m, TargetOffset = 3m };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.StartsWith("The target leg cannot be given as both an absolute price and an offset.", exception.Message);
        }

        [Fact]
        public void Create_NonPositiveStopOffset_Throws()
        {
            BracketRequest request = new() { StopOffset = 0m };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.StartsWith("The stop offset must be greater than zero.", exception.Message);
        }

        [Fact]
        public void Create_NonPositiveTargetOffset_Throws()
        {
            BracketRequest request = new() { TargetOffset = -1m };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.StartsWith("The target offset must be greater than zero.", exception.Message);
        }

        [Fact]
        public void Create_RequestWithNoLeg_Throws()
        {
            BracketRequest request = new();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.StartsWith("A bracket must have at least one leg (a stop-loss and/or a take-profit).", exception.Message);
        }

        [Fact]
        public void Create_IllegalRequest_NamesTheRequestParameter()
        {
            BracketRequest request = new();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Bracket.Create(request));

            Assert.Equal("request", exception.ParamName);
        }
    }
}
