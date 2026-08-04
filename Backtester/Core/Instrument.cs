namespace Backtester.Core
{
    /// <summary>
    /// Caller-supplied per-symbol metadata: the currency an Instrument's price quotes in (required), the
    /// exact provider symbol (if any) to fetch for converting that quote currency into the account's own
    /// currency and which way that series is quoted, and an optional symmetric long/short margin rate
    /// overriding Portfolio's Reg-T default. The currency declaration is cross-checked against the account
    /// currency when the <see cref="CurrencyConverter"/> is built.
    /// </summary>
    public class Instrument
    {
        /// <summary>Gets or sets the ticker symbol this Instrument describes.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the ISO currency code this Instrument's price is quoted in.</summary>
        public string QuoteCurrency { get; set; }

        /// <summary>
        /// Gets or sets the exact provider symbol to fetch for converting this Instrument's quote
        /// currency into the account's currency, stated explicitly by the caller — never computed or
        /// guessed by the engine. Null when the Instrument already quotes in the account's currency.
        /// </summary>
        public string ConversionSymbol { get; set; }

        /// <summary>
        /// Gets or sets whether converting this Instrument's amounts into the account's currency divides or
        /// multiplies by the Conversion symbol's rate — which way that pair is quoted. Defaults to
        /// <see cref="Core.ConversionOperation.Divide"/>, the account-first quotation. Ignored when the
        /// Instrument declares no Conversion symbol.
        /// </summary>
        public ConversionOperation ConversionOperation { get; set; }

        /// <summary>
        /// Gets or sets a symmetric long/short initial-margin rate for this Instrument, overriding
        /// Portfolio's Reg-T default when set. Null to use Portfolio's default rates.
        /// </summary>
        public decimal? MarginRate { get; set; }
    }
}
