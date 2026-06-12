namespace Backtester.Models.Risk
{
    using Backtester.Core;

    public interface IRiskModel
    {
        bool Accept(OrderRequest request, Portfolio portfolio);
    }
}
