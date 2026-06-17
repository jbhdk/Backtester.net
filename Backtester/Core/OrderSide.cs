namespace Backtester.Core
{
    /// <summary>
    /// Indicates whether an order is a buy or sell.
    /// </summary>
    public enum OrderSide
    {
        /// <summary>Buy (long entry or short cover).</summary>
        Buy,

        /// <summary>Sell (long exit or short entry).</summary>
        Sell
    }
}
