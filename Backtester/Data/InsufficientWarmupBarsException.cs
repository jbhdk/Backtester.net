using System;

namespace Backtester.Data
{
    /// <summary>
    /// Thrown when a bar-count warmup asks for more lead-in bars than a symbol has available above its
    /// Coverage floor. Below the floor the Cache's lack of bars is unknown (that window was never requested),
    /// so the run is refused rather than served a silently short lead-in, mirroring
    /// <see cref="DataCoverageException"/>'s refuse-don't-serve-short stance (ADR 0021 / ADR 0022).
    /// </summary>
    public class InsufficientWarmupBarsException : Exception
    {
        /// <summary>The symbol whose available bars fall short of the requested warmup.</summary>
        public string Symbol { get; }

        /// <summary>The number of warmup bars requested before the Test range start.</summary>
        public int RequestedBars { get; }

        /// <summary>The number of bars actually available above the Coverage floor before the Test range start.</summary>
        public int AvailableBars { get; }

        /// <summary>The bar interval of the requested warmup.</summary>
        public string Interval { get; }

        /// <summary>Initializes a new exception describing the shortfall and the remedy.</summary>
        public InsufficientWarmupBarsException(string symbol, int requestedBars, int availableBars, string interval)
            : base(BuildMessage(symbol, requestedBars, availableBars, interval))
        {
            Symbol = symbol;
            RequestedBars = requestedBars;
            AvailableBars = availableBars;
            Interval = interval;
        }

        private static string BuildMessage(string symbol, int requestedBars, int availableBars, string interval)
        {
            return $"{symbol} {interval}: warmup requested {requestedBars} bars before the Test range start but only " +
                $"{availableBars} are available above the Coverage floor. Prime an earlier range via IDataPrimer.PrimeAsync " +
                $"so at least {requestedBars} warmup bars exist, or request fewer warmup bars.";
        }
    }
}
