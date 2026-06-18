namespace Backtester.ExecutionModels.Commission
{
    /// <summary>
    /// Charges a flat commission amount per trade regardless of size.
    /// </summary>
    public class FixedCommission : ICommissionModel
    {
        /// <summary>Gets or sets the flat fee charged per trade.</summary>
        public decimal Amount { get; set; }

        /// <summary>Returns the fixed commission amount regardless of notional or quantity.</summary>
        public decimal Calculate(decimal notional, int quantity)
        {
            return Amount;
        }
    }
}
