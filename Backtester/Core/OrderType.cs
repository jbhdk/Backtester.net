namespace Backtester.Core
{
    /// <summary>
    /// Specifies the execution style of an order.
    /// </summary>
    public enum OrderType
    {
        /// <summary>Execute immediately at the best available price.</summary>
        Market,

        /// <summary>Execute only at the specified price or better.</summary>
        Limit,

        /// <summary>Trigger a market order once price reaches the stop level.</summary>
        Stop
    }
}
