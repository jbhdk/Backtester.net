namespace Backtester.Core
{
    /// <summary>
    /// Whether translating a native amount into the Account currency divides or multiplies by the
    /// Conversion symbol's rate, determined by which way that pair is quoted.
    /// </summary>
    public enum ConversionOperation
    {
        /// <summary>
        /// Divide by the rate: the Conversion symbol's first currency is the Account currency, so its price
        /// is quote-currency units per 1 account-currency unit (<c>USD_JPY</c> in a USD account). The
        /// default, so an Instrument declaring no operation behaves as every declaration did before.
        /// </summary>
        Divide = 0,

        /// <summary>
        /// Multiply by the rate: the Conversion symbol's first currency is the Instrument's quote currency,
        /// so its price is account-currency units per 1 quote-currency unit (<c>GBP_USD</c> for a
        /// GBP-quoted symbol in a USD account, where no account-first pair exists).
        /// </summary>
        Multiply
    }
}
