namespace Backtester.ExecutionModels.Commission
{
    /// <summary>
    /// Charges commission as a percentage of the trade's notional value.
    /// </summary>
    public class PercentCommission : ICommissionModel
    {
        /// <summary>Gets or sets the commission rate (e.g. 0.001 for 0.1%).</summary>
        public decimal Percent { get; set; }

        /// <summary>Returns <paramref name="notional"/> multiplied by the configured rate.</summary>
        public decimal Calculate(decimal notional, int quantity)
        {
            return notional * Percent;
        }
    }
}
