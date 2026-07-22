using System.Collections.Generic;
using System.Linq;
using Backtester.Optimization;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of <see cref="ParameterSpace"/>: declaring axes and expanding them into the exhaustive
    /// grid of Parameter sets (the cartesian product of every axis's values).
    /// </summary>
    public class ParameterSpaceTests
    {
        [Fact]
        public void Expand_SingleIntAxis_YieldsOneSetPerValueInclusive()
        {
            IReadOnlyList<ParameterSet> sets = new ParameterSpace().AddInt("qty", from: 1, to: 3, step: 1).Expand();

            Assert.Equal(new[] { 1, 2, 3 }, sets.Select(set => set.Int("qty")));
        }

        [Fact]
        public void Expand_TwoAxes_YieldsCartesianProduct()
        {
            IReadOnlyList<ParameterSet> sets = new ParameterSpace()
                .AddInt("a", from: 1, to: 2, step: 1)
                .AddInt("b", from: 10, to: 20, step: 10)
                .Expand();

            IEnumerable<(int A, int B)> combinations = sets.Select(set => (set.Int("a"), set.Int("b")));
            Assert.Equal(new[] { (1, 10), (1, 20), (2, 10), (2, 20) }, combinations);
        }

        [Fact]
        public void Expand_BoolAxis_YieldsFalseAndTrue()
        {
            IReadOnlyList<ParameterSet> sets = new ParameterSpace().AddBool("flag").Expand();

            Assert.Equal(new[] { false, true }, sets.Select(set => set.Bool("flag")));
        }

        [Fact]
        public void Expand_DecimalAxis_IsSteppedAndInclusive()
        {
            IReadOnlyList<ParameterSet> sets = new ParameterSpace().AddDecimal("x", from: 0.5m, to: 1.5m, step: 0.5m).Expand();

            Assert.Equal(new[] { 0.5m, 1.0m, 1.5m }, sets.Select(set => set.Decimal("x")));
        }

        [Fact]
        public void Expand_NoAxes_YieldsASingleEmptySet()
        {
            IReadOnlyList<ParameterSet> sets = new ParameterSpace().Expand();

            Assert.Single(sets);
        }
    }
}
