namespace Backtester.Models.Commission
{
    public interface ICommissionModel
    {
        decimal Calculate(decimal notional, int quantity);
    }
}
