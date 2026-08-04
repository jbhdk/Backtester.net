using System;

namespace Backtester.Core
{
    /// <summary>
    /// A complete entry-to-exit cycle for a position: one or more buys paired with a closing sell.
    /// </summary>
    public class RoundTrip
    {
        /// <summary>Gets or sets the ticker symbol traded in this round trip.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the direction of this round trip (long: buy then sell; short: sell then buy).</summary>
        public PositionDirection Direction { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal EntryPrice { get; set; }

        /// <summary>Gets or sets the exit fill price.</summary>
        public decimal ExitPrice { get; set; }

        /// <summary>Gets or sets the number of shares exited.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the realized profit/loss for this round trip, excluding commission and slippage.</summary>
        public decimal RealizedPnL { get; set; }

        /// <summary>
        /// Gets or sets the Account-currency amount this round trip stood to lose if its entry stop had been
        /// hit, before any trailing: the per-share stop distance frozen at entry — translated at the rate in
        /// force when the position opened from flat (ADR 0032) — times this trip's quantity. A rate move
        /// between entry and exit therefore shows up in the R-multiple rather than in this figure, which
        /// stays what a broker would have said was at risk as the trip was entered. Null when the entry
        /// declared no protective stop, in which case no R-multiple is defined.
        /// </summary>
        public decimal? InitialRisk { get; set; }

        /// <summary>
        /// Gets or sets the initial stop: the entry-time level of the stop-loss, frozen when the position
        /// opened from flat and unchanged by any later trailing. It is the declared entry stop — the armed
        /// bracket stop leg, or the sizing stop of a risk-sized entry that armed no bracket. Null when the
        /// entry declared no stop (a target-only bracket or a plain entry).
        /// </summary>
        public decimal? EntryStopPrice { get; set; }

        /// <summary>
        /// Gets or sets the initial target: the entry-time level of the take-profit, frozen when the
        /// position opened from flat. A target exists only through a bracket, so this is null for a
        /// stop-only bracket or a plain entry.
        /// </summary>
        public decimal? EntryTargetPrice { get; set; }

        /// <summary>
        /// Gets or sets the Account-currency capital this round trip committed when it opened: its share of
        /// the running cost basis its position accumulated fill by fill, each fill converted at the rate in
        /// force as it filled (ADR 0032). A trip that scaled in across a rate move therefore carries what
        /// actually left the account, not a blended entry price translated afterwards. The numerator of the
        /// trip's leverage — divided by <see cref="EntryEquity"/>, two figures in the same currency.
        /// </summary>
        public decimal EntryNotional { get; set; }

        /// <summary>
        /// Gets or sets the Account-currency initial margin this round trip committed when it opened: the
        /// portfolio's own margin rate for the symbol and side — the Instrument's declared rate when it has
        /// one, else the Reg-T long/short split (ADR 0030) — applied to <see cref="EntryNotional"/>, and so
        /// already converted at the rates in force as the trip filled (ADR 0032). Frozen at entry rather
        /// than re-marked; the account's live committed margin is the aggregate Avg/Peak margin's business.
        /// </summary>
        public decimal EntryMargin { get; set; }

        /// <summary>
        /// Gets or sets the account's marked equity on the bar this round trip's position opened from flat.
        /// The denominator for the trip's leverage (its entry notional over this equity); preserved across
        /// same-direction adds and partial exits so each slice divides by the opening bar's equity.
        /// </summary>
        public decimal EntryEquity { get; set; }

        /// <summary>Gets or sets the number of bars the position was held before exit.</summary>
        public int BarsHeld { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the entry trade that opened this round trip.</summary>
        public DateTime EntryTime { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the exit trade that closed this round trip.</summary>
        public DateTime ExitTime { get; set; }

        /// <summary>Gets or sets why this round trip closed, derived from the bracket leg of its exit trade.</summary>
        public ExitReason ExitReason { get; set; }
    }
}
