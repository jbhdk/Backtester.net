using Backtester.Core;

namespace Backtester.Models.Sizing
{
    /// <summary>
    /// Always returns a fixed position size regardless of the order or portfolio state.
    /// </summary>
    public class FixedSizeModel : ISizingModel
    {
        /// <summary>Gets or sets the fixed number of shares or contracts to use per order.</summary>
        public int FixedSize { get; set; }

        /// <summary>Returns the configured fixed size.</summary>
        public int Size(OrderRequest request, Portfolio portfolio)
        {
            return FixedSize;
        }
    }
}
