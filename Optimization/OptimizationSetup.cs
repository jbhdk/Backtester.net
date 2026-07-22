using System;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Strategies;

namespace Backtester.Optimization
{
    /// <summary>
    /// The product of an authoring path: the <see cref="ParameterSpace"/> to sweep paired with the Trial
    /// factory that realizes each <see cref="ParameterSet"/> into a strategy and broker. It is the bundle
    /// the <see cref="Optimizer"/> consumes, so the attributes-first path (and future paths) can build the
    /// two together and hand them over as one unit.
    /// </summary>
    public class OptimizationSetup
    {
        /// <summary>Initializes a new setup over the Parameter space and the Trial factory that realizes each set.</summary>
        public OptimizationSetup(
            ParameterSpace space,
            Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> trialFactory)
        {
            Space = space;
            TrialFactory = trialFactory;
        }

        /// <summary>Gets the Parameter space the Optimizer expands into the grid of Parameter sets.</summary>
        public ParameterSpace Space { get; }

        /// <summary>Gets the factory that builds a Trial's strategy and broker from a Parameter set.</summary>
        public Func<ParameterSet, Portfolio, (IStrategy Strategy, IBrokerSimulator Broker)> TrialFactory { get; }
    }
}
