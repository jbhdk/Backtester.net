namespace Backtester.Report
{
    /// <summary>
    /// The area of the run a Finding concerns. Adding a category is a contract change affecting every
    /// Analysis client, because the category set is half of the Analysis contract (ADR 0019).
    /// </summary>
    public enum FindingCategory
    {
        /// <summary>Drawdown, loss streaks, and how much of the account a run puts at stake.</summary>
        Risk,

        /// <summary>Position size: how much capital each trade commits, and how consistently.</summary>
        Sizing,

        /// <summary>Fills, rejected orders, commission and slippage — how the run actually traded.</summary>
        Execution,

        /// <summary>Whether the result survives a change of period, symbol, or parameter.</summary>
        Robustness,

        /// <summary>Gaps, stale bars, or too small a sample to conclude anything from.</summary>
        DataQuality
    }
}
