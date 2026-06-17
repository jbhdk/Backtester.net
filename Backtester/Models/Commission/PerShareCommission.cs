namespace Backtester.Models.Commission
{
    /// <summary>
    /// Charges a fixed fee per share or contract filled.
    /// </summary>
    public class PerShareCommission : ICommissionModel
    {
        /// <summary>Gets or sets the fee charged per share or contract.</summary>
        public decimal PerShare { get; set; }

        /// <summary>Returns the per-share rate multiplied by the number of shares filled.</summary>
        public decimal Calculate(decimal notional, int quantity)
        {
            return PerShare * quantity;
        }
    }
}
