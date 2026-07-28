using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Sizes positions so that a stop-out loses a fixed currency amount that does not scale with the
    /// account. Formula: shares = floor(RiskAmount / stopDistance).
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

            return (int)(RiskAmount / stopDistance);
        }
    }
}
