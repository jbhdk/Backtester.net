namespace Backtester.Models.Slippage
{
    using Backtester.Core;

    public interface ISlippageModel
    {
        decimal Apply(decimal price, OrderSide side);
    }
}
