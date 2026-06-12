namespace Backtester.Models.Commission
{
    public class PerShareCommission : ICommissionModel
    {
        public decimal PerShare { get; set; }

        public decimal Calculate(decimal notional, int quantity)
        {
            return PerShare * quantity;
        }
    }
}
