using System;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;

namespace Backtester.Optimization
{
    /// <summary>
    /// Entry point for the attributes-first authoring path. <see cref="For{TParameters}"/> binds a
    /// parameters instance to the factory that builds a Trial's strategy and broker from it; the returned
    /// builder reflects the <c>[Optimize]</c>-decorated Parameters into an <see cref="OptimizationSetup"/>.
    /// </summary>
    public static class Optimize
    {
        /// <summary>
        /// Begins authoring an Optimization from an attributed parameters instance. Each Trial's factory
        /// receives a strongly-typed clone of <paramref name="instance"/> with the swept Parameters set, so
        /// it reads them directly (e.g. <c>parameters.RiskFraction</c>).
        /// </summary>
        /// <param name="instance">The parameters instance whose <c>[Optimize]</c> properties define the axes.</param>
        /// <param name="factory">Builds a Trial's strategy and broker from a swept clone and its Portfolio.</param>
        public static OptimizeBuilder<TParameters> For<TParameters>(
            TParameters instance,
            Func<TParameters, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> factory)
        {
            return new OptimizeBuilder<TParameters>(instance, factory);
        }
    }
}
