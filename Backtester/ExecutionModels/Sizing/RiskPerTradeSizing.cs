using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions so that a stop-out loses a fixed fraction of realized (cost-basis) equity.
    /// Formula: shares = floor(RiskFraction * <see cref="Portfolio.RealizedEquity"/> / stopDistance), where
    /// the per-share stop distance is the fill-relative <see cref="OrderRequest.StopOffset"/> when set (a
    /// bracket entry whose absolute stop is not yet known), else the absolute <c>|entryPrice - stopPrice|</c>,
    /// converted into the portfolio's account currency (ADR 0029) so a cross-currency instrument's stop
    /// distance shares units with the equity budget before dividing. The budget itself is the Portfolio's
    /// own translated figure (ADR 0032), so an open cross-currency position cannot inflate it.
    /// Returns zero when no stop distance is available.
    /// </summary>
    public class RiskPerTradeSizing : ISizingModel
    {
        /// <summary>Gets or sets the fraction of realized equity to risk per trade (e.g. 0.01 for 1%).</summary>
        public decimal RiskFraction { get; set; }

        /// <summary>
        /// Returns the number of shares that limits the stop-out loss to <see cref="RiskFraction"/> of realized equity.
        /// </summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            decimal stopDistance = OrderStopDistance.For(request);
            if (stopDistance <= 0m)
            {
                return 0;
            }

            decimal convertedStopDistance = portfolio.ToAccountCurrency(request.Symbol, stopDistance);
            return (int)(RiskFraction * portfolio.RealizedEquity / convertedStopDistance);
        }
    }
}
