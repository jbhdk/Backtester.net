namespace Backtester.Models.Commission
{
    /// <summary>
    /// Computes the commission charged on a trade.
    /// </summary>
    public interface ICommissionModel
    {
        /// <summary>
        /// Calculates the commission amount given the total notional value and number of shares filled.
        /// </summary>
        decimal Calculate(decimal notional, int quantity);
    }
}
