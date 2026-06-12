namespace Backtester.Models.Slippage
{
    public interface ISlippageModel
    {
        decimal Apply(decimal price);
    }
}
