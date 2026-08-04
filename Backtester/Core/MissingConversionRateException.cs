using System;

namespace Backtester.Core
{
    /// <summary>
    /// Thrown when a symbol's declared conversion is applied before any rate has been observed for its
    /// Conversion symbol. The Currency converter refuses loudly rather than serve the native, unconverted
    /// amount — a run configured slightly wrong must fail rather than present quote-currency numbers as
    /// Account currency.
    /// </summary>
    public class MissingConversionRateException : Exception
    {
        /// <summary>The symbol whose amount could not be converted.</summary>
        public string Symbol { get; }

        /// <summary>The Conversion symbol the symbol declares, for which no rate has been observed.</summary>
        public string ConversionSymbol { get; }

        /// <summary>
        /// Initializes a new exception naming the symbol, its Conversion symbol, and the remedy.
        /// </summary>
        public MissingConversionRateException(string symbol, string conversionSymbol)
            : base(BuildMessage(symbol, conversionSymbol))
        {
            Symbol = symbol;
            ConversionSymbol = conversionSymbol;
        }

        private static string BuildMessage(string symbol, string conversionSymbol)
        {
            return $"{symbol} converts through {conversionSymbol}, but no rate for {conversionSymbol} has been " +
                $"observed yet. Fetch {conversionSymbol} over the same range as {symbol} and feed its closes to " +
                "CurrencyConverter.ObserveRate before converting.";
        }
    }
}
