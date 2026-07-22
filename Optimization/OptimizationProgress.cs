namespace Backtester.Optimization
{
    /// <summary>
    /// A progress update reported as an Optimization advances: how many Trials have finished
    /// (<see cref="Completed"/>) out of the total in the sweep (<see cref="Total"/>). One update is
    /// reported per completed Trial; because Trials run in parallel, updates can arrive out of order, but
    /// the last one for a sweep carries <see cref="Completed"/> equal to <see cref="Total"/>.
    /// </summary>
    public class OptimizationProgress
    {
        /// <summary>Initializes a new progress update for the given completed count out of the total.</summary>
        public OptimizationProgress(int completed, int total)
        {
            Completed = completed;
            Total = total;
        }

        /// <summary>Gets the number of Trials that have finished so far.</summary>
        public int Completed { get; }

        /// <summary>Gets the total number of Trials in the sweep.</summary>
        public int Total { get; }
    }
}
