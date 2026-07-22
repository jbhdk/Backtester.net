using Backtester.Core;
using Backtester.Optimization;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of the named <see cref="Objectives"/> presets: each reads the intended Performance metric
    /// and ranks in the intended direction. Scores are asserted against hand-built stats so the metric each
    /// preset reads is pinned exactly.
    /// </summary>
    public class ObjectivesTests
    {
        [Fact]
        public void Sharpe_MaximizesTheSharpeRatio()
        {
            PerformanceStats stats = new PerformanceStats { Sharpe = 1.4m };

            Assert.Equal(OptimizationDirection.Maximize, Objectives.Sharpe.Direction);
            Assert.Equal(1.4m, Objectives.Sharpe.Score(stats));
        }

        [Fact]
        public void NetProfit_MaximizesNetProfit()
        {
            PerformanceStats stats = new PerformanceStats { NetProfit = 1234m };

            Assert.Equal(OptimizationDirection.Maximize, Objectives.NetProfit.Direction);
            Assert.Equal(1234m, Objectives.NetProfit.Score(stats));
        }

        [Fact]
        public void Calmar_MaximizesTheCalmarRatio()
        {
            PerformanceStats stats = new PerformanceStats { Calmar = 2.1m };

            Assert.Equal(OptimizationDirection.Maximize, Objectives.Calmar.Direction);
            Assert.Equal(2.1m, Objectives.Calmar.Score(stats));
        }

        [Fact]
        public void MinDrawdown_MinimizesTheMaximumDrawdown()
        {
            PerformanceStats stats = new PerformanceStats { MaxDrawdown = 0.3m };

            Assert.Equal(OptimizationDirection.Minimize, Objectives.MinDrawdown.Direction);
            Assert.Equal(0.3m, Objectives.MinDrawdown.Score(stats));
        }

        [Fact]
        public void ProfitFactor_MaximizesTheProfitFactor()
        {
            PerformanceStats stats = new PerformanceStats { ProfitFactor = 1.8m };

            Assert.Equal(OptimizationDirection.Maximize, Objectives.ProfitFactor.Direction);
            Assert.Equal(1.8m, Objectives.ProfitFactor.Score(stats));
        }
    }
}
