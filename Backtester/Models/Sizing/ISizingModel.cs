namespace Backtester.Models.Sizing
{
    using Backtester.Core;

    public interface ISizingModel
    {
        int Size(OrderRequest request, Portfolio portfolio);
    }
}
