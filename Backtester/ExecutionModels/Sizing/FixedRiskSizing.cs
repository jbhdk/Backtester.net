using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions so that a stop-out loses a fixed currency amount that does not scale with the
    /// account. Formula: shares = floor(RiskAmount / stopDistance), where stopDistance is converted into
    /// the portfolio's account currency (ADR 0029) so a cross-currency instrument's stop distance shares
    /// units with RiskAmount before dividing.
    /// </summary>
    public class FixedRiskSizing : ISizingModel
    {
        /// <summary>Gets or sets the currency amount to risk per trade (e.g. 500 for $500).</summary>
        public decimal RiskAmount { get; set; }

        /// <summary>
        /// Returns the number of shares that limits the stop-out loss to <see cref="RiskAmount"/>.
        /// </summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            if (RiskAmount <= 0m)
            {
                return 0;
            }

            decimal stopDistance = OrderStopDistance.For(request);
            if (stopDistance <= 0m)
            {
                return 0;
            }

            decimal convertedStopDistance = portfolio.ToAccountCurrency(request.Symbol, stopDistance);
            return (int)(RiskAmount / convertedStopDistance);
        }
    }
}
