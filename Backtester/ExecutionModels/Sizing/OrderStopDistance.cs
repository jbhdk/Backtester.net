using System;
using Backtester.Core;

namespace Backtester.ExecutionModels.Sizing
{
    /// <summary>
    /// Resolves the per-share stop distance a risk-based sizing model divides its budget into, shared by
    /// the risk sizing models so the offset-first, absolute-fallback, else-zero rule lives in one place.
    /// </summary>
    internal static class OrderStopDistance
    {
        /// <summary>
        /// Returns the per-share stop distance for an order: the fill-relative
        /// <see cref="OrderRequest.StopOffset"/> when set (a bracket entry whose absolute stop resolves
        /// against the fill later, so no absolute anchor exists at submit time), else the absolute distance
        /// <c>|Price - StopPrice|</c>. Returns zero when neither is available, so the caller sizes nothing
        /// rather than risking an unknown amount.
        /// </summary>
        public static decimal For(OrderRequest request)
        {
            if (request.StopOffset is decimal offset && offset > 0m)
            {
                return offset;
            }

            if (request.Price is null or 0m || request.StopPrice is null)
            {
                return 0m;
            }

            return Math.Abs(request.Price.Value - request.StopPrice.Value);
        }
    }
}
