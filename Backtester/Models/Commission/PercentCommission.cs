namespace Backtester.Models.Commission
{
    public class PercentCommission : ICommissionModel
    {
        public decimal Percent { get; set; }

        public decimal Calculate(decimal notional, int quantity)
        {
            return notional * Percent;
        }
    }
}
