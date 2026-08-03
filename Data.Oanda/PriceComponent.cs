namespace Backtester.Data.Oanda
{
    /// <summary>
    /// The Oanda v20 price component to fetch candles for: the midpoint between bid and ask, or one
    /// side of the spread. Maps to Oanda's <c>price</c> query parameter (<c>M</c>/<c>B</c>/<c>A</c>)
    /// and the response sub-object (<c>mid</c>/<c>bid</c>/<c>ask</c>) OHLC values are read from.
    /// </summary>
    public enum PriceComponent
    {
        /// <summary>The midpoint between bid and ask.</summary>
        Mid,

        /// <summary>The bid (sell) side of the spread.</summary>
        Bid,

        /// <summary>The ask (buy) side of the spread.</summary>
        Ask
    }
}
