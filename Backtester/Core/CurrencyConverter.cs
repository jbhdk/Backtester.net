using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// Translates quote-currency amounts into the account's own currency. It holds each Instrument's
    /// conversion declaration, observes Conversion-symbol closes as bars arrive, and applies the
    /// conversion — identity for an Instrument declaring no Conversion symbol.
    /// </summary>
    public class CurrencyConverter
    {
        private readonly string _accountCurrency;

        // Key: symbol/ticker -> the exact provider symbol whose price converts that symbol's quote
        // currency into the account currency. Entries exist only for Instruments declaring a Conversion
        // symbol; a symbol absent here already quotes in the account currency.
        private readonly Dictionary<string, string> _conversionSymbolBySymbol = new();

        // Key: Conversion symbol -> its most recently observed close, the rate conversion divides by.
        private readonly Dictionary<string, decimal> _rateByConversionSymbol = new();

        // The distinct Conversion symbols declared at construction, held apart from the observed rates so
        // the series a caller must fetch is known before any rate has printed.
        private readonly HashSet<string> _conversionSymbols = new();

        /// <summary>
        /// Initializes a converter for an account denominated in <paramref name="accountCurrency"/>,
        /// holding the conversion declarations of <paramref name="instruments"/>. Null or empty
        /// instruments yield a converter that converts nothing, as every symbol then quotes in the
        /// account's own currency.
        /// </summary>
        public CurrencyConverter(string accountCurrency, Instrument[] instruments)
        {
            _accountCurrency = accountCurrency;

            if (instruments == null)
            {
                return;
            }

            foreach (Instrument instrument in instruments)
            {
                if (instrument.ConversionSymbol != null)
                {
                    _conversionSymbolBySymbol[instrument.Symbol] = instrument.ConversionSymbol;
                    _conversionSymbols.Add(instrument.ConversionSymbol);
                }
            }
        }

        /// <summary>
        /// Gets the distinct Conversion symbols declared by the Instruments this converter holds — exactly
        /// the extra series a caller must fetch and feed to <see cref="ObserveRate"/>. Empty when every
        /// Instrument already quotes in the account currency.
        /// </summary>
        public IReadOnlyCollection<string> ConversionSymbols => _conversionSymbols;

        /// <summary>
        /// Records <paramref name="close"/> as the latest rate for <paramref name="conversionSymbol"/>.
        /// The feed primitive: a caller stepping through bars calls this for each Conversion symbol that
        /// printed, and the rate stands until the next observation.
        /// </summary>
        public void ObserveRate(string conversionSymbol, decimal close)
        {
            _rateByConversionSymbol[conversionSymbol] = close;
        }

        /// <summary>
        /// Returns <paramref name="nativeAmount"/> — denominated in <paramref name="symbol"/>'s own quote
        /// currency — converted into the account currency by dividing by the latest observed close of the
        /// symbol's declared Conversion symbol, which quotes quote-currency units per 1 account-currency
        /// unit (e.g. <c>USD_JPY</c> for a JPY-quoted symbol in a USD account). Returns the amount
        /// unchanged when the symbol declares no conversion.
        /// </summary>
        public decimal ToAccountCurrency(string symbol, decimal nativeAmount)
        {
            if (_conversionSymbolBySymbol.TryGetValue(symbol, out string conversionSymbol)
                && _rateByConversionSymbol.TryGetValue(conversionSymbol, out decimal rate)
                && rate != 0m)
            {
                return nativeAmount / rate;
            }

            return nativeAmount;
        }
    }
}
