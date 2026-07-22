using Backtester.Core;
using Backtester.Optimization;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of <see cref="Objective"/>: the <see cref="Objective.Maximize"/> and
    /// <see cref="Objective.Minimize"/> factories, the direction they carry, and the raw Score they read
    /// from a Trial's combined <see cref="PerformanceStats"/>.
    /// </summary>
    public class ObjectiveTests
    {
        [Fact]
        public void Maximize_CarriesTheMaximizeDirection()
        {
            Objective objective = Objective.Maximize(stats => stats.Sharpe);

            Assert.Equal(OptimizationDirection.Maximize, objective.Direction);
        }

        [Fact]
        public void Minimize_CarriesTheMinimizeDirection()
        {
            Objective objective = Objective.Minimize(stats => stats.MaxDrawdown);

            Assert.Equal(OptimizationDirection.Minimize, objective.Direction);
        }

        [Fact]
        public void Score_ReturnsTheRawMetricValue_RegardlessOfDirection()
        {
            PerformanceStats stats = new PerformanceStats { MaxDrawdown = 0.25m };
            Objective objective = Objective.Minimize(candidate => candidate.MaxDrawdown);

            Assert.Equal(0.25m, objective.Score(stats));
        }
    }
}
