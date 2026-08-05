using System;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class BracketLegSpecTests
    {
        [Fact]
        public void AtPrice_ResolvesToItsOwnPrice_WhateverTheFillWas()
        {
            BracketLegSpec spec = BracketLegSpec.AtPrice(95m);

            decimal level = spec.Resolve(101.25m, -1m);

            Assert.Equal(95m, level);
        }

        [Fact]
        public void OffsetFromFill_ResolvesBelowTheFill_WhenTheProtectiveSideIsDown()
        {
            BracketLegSpec spec = BracketLegSpec.OffsetFromFill(2m);

            decimal level = spec.Resolve(101.25m, -1m);

            Assert.Equal(99.25m, level);
        }

        [Fact]
        public void OffsetFromFill_ResolvesAboveTheFill_WhenTheProtectiveSideIsUp()
        {
            BracketLegSpec spec = BracketLegSpec.OffsetFromFill(2m);

            decimal level = spec.Resolve(101.25m, 1m);

            Assert.Equal(103.25m, level);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        public void OffsetFromFill_NonPositiveOffset_Throws(decimal offset)
        {
            ArgumentOutOfRangeException exception =
                Assert.Throws<ArgumentOutOfRangeException>(() => BracketLegSpec.OffsetFromFill(offset));

            Assert.Equal("offset", exception.ParamName);
        }
    }
}
