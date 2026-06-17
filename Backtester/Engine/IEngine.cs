using System;

namespace Backtester.Engine
{
    /// <summary>
    /// Controls the execution lifecycle of a backtest run.
    /// </summary>
    public interface IEngine
    {
        /// <summary>Begins processing bars in a loop until the feed is exhausted or <see cref="Stop"/> is called.</summary>
        void Start();

        /// <summary>Signals the engine to halt after completing the current bar.</summary>
        void Stop();

        /// <summary>Processes a single bar: runs the strategy, submits orders, processes fills, and records equity.</summary>
        void RunOnce();
    }
}
