using System;

namespace Backtester.Report
{
    /// <summary>
    /// One order the broker declined, projected for the report's trade log: the instrument, attempted
    /// direction, size, price, time, and the reason it was rejected (e.g. insufficient buying power).
    /// </summary>
    public class ReportRejectedOrder
    {
        /// <summary>Gets or sets the ticker symbol the rejected order targeted.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the attempted direction as a page-friendly string (<c>"Long"</c> for a Buy, <c>"Short"</c> for a Sell).</summary>
        public string Direction { get; set; }

        /// <summary>Gets or sets the bar timestamp at which the order was attempted.</summary>
        public DateTime Time { get; set; }

        /// <summary>Gets or sets the price the order was valued at when it was rejected.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the (already-sized) quantity the rejected order requested.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the reason the order was rejected (e.g. <c>"Not enough funds"</c>).</summary>
        public string Reason { get; set; }
    }
}
