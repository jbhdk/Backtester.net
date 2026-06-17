namespace Backtester.Core
{
    /// <summary>
    /// Holds the order IDs for a submitted bracket. Stop and target IDs are populated when the entry fills.
    /// </summary>
    public class BracketHandle
    {
        /// <summary>Gets or sets the entry order ID assigned at submission time.</summary>
        public string EntryOrderId { get; set; }

        /// <summary>Gets or sets the stop-loss order ID; non-null once the entry fills.</summary>
        public string StopOrderId { get; set; }

        /// <summary>Gets or sets the take-profit order ID; non-null once the entry fills.</summary>
        public string TargetOrderId { get; set; }
    }
}
