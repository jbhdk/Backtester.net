namespace Backtester.Models.Sizing
{
    using Backtester.Core;

    public class FixedSizeModel : ISizingModel
    {
        public int FixedSize { get; set; }

        public int Size(OrderRequest request, Portfolio portfolio)
        {
            return FixedSize;
        }
    }
}
