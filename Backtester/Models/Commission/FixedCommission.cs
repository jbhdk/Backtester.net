namespace Backtester.Models.Commission
{
    public class FixedCommission : ICommissionModel
    {
        public decimal Amount { get; set; }

        public decimal Calculate(decimal notional, int quantity)
        {
            return Amount;
        }
    }
}
