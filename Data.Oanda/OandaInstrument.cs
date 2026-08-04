using System;
using Backtester.Core;

namespace Backtester.Data.Oanda
{
    /// <summary>
    /// Builds a fully-declared <see cref="Instrument"/> from an Oanda pair symbol, inferring the quote
    /// currency, the Conversion symbol, and the Conversion operation from Oanda's underscore pair naming
    /// and its pair-ordering convention. Provider naming conventions stay in the provider package: a
    /// caller never hand-writes currency metadata, and the engine core never parses a symbol.
    /// </summary>
    public static class OandaInstrument
    {
        // Oanda's base-currency precedence, highest-ranked first: the pair between two currencies is named
        // with the higher-ranked one first, so EUR_USD and USD_JPY exist while USD_EUR and JPY_USD do not.
        // JPY is deliberately absent — see UniversalQuoteCurrency. The majors lead in the conventional
        // order; the rest follow Oanda's own instrument list, which is why SGD sits above CHF (SGD_CHF and
        // CAD_SGD both exist) rather than below it with the other non-majors.
        private static readonly string[] Precedence =
        {
            "EUR", "GBP", "AUD", "NZD", "USD", "CAD", "SGD", "CHF",
            "NOK", "SEK", "DKK", "CZK", "HUF", "PLN", "TRY", "ZAR",
            "MXN", "CNH", "HKD", "THB", "SAR", "INR"
        };

        // Oanda's universal quote currency: it is named second against every other currency, including
        // ones this table has never heard of (SGD_JPY, TRY_JPY and ZAR_JPY all exist). So it ranks below
        // an unrecognized currency rather than sitting at the bottom of the table alongside one.
        private const string UniversalQuoteCurrency = "JPY";

        /// <summary>
        /// Returns an Instrument for the Oanda pair <paramref name="symbol"/> held in an account
        /// denominated in <paramref name="accountCurrency"/>. A pair quoting in a currency other than the
        /// account's declares the Conversion symbol between those two currencies and the operation its
        /// quotation implies; a pair already quoting in the account's currency declares neither.
        /// </summary>
        public static Instrument For(string symbol, string accountCurrency)
        {
            string quoteCurrency = QuoteCurrencyOf(symbol);

            Instrument instrument = new()
            {
                Symbol = symbol,
                QuoteCurrency = quoteCurrency
            };

            if (QuotesInAccountCurrency(quoteCurrency, accountCurrency))
            {
                return instrument;
            }

            // Which way Oanda names the pair decides both halves of the declaration at once, so they are
            // read from a single ordering decision and can never contradict each other.
            string account = accountCurrency.ToUpperInvariant();
            string quote = quoteCurrency.ToUpperInvariant();
            bool accountFirst = AccountCurrencyIsQuotedFirst(quoteCurrency, accountCurrency);

            instrument.ConversionSymbol = accountFirst ? $"{account}_{quote}" : $"{quote}_{account}";
            instrument.ConversionOperation = accountFirst
                ? ConversionOperation.Divide
                : ConversionOperation.Multiply;

            return instrument;
        }

        /// <summary>
        /// Returns the currency an Oanda pair quotes in — the second of its two underscore-separated ISO
        /// codes, <c>JPY</c> for <c>EUR_JPY</c>.
        /// </summary>
        /// <exception cref="InstrumentDeclarationException">
        /// <paramref name="symbol"/> is not two ISO codes joined by an underscore.
        /// </exception>
        private static string QuoteCurrencyOf(string symbol)
        {
            string[] currencies = symbol == null ? new string[0] : symbol.Split('_');

            if (currencies.Length != 2 || !IsIsoCurrencyCode(currencies[0]) || !IsIsoCurrencyCode(currencies[1]))
            {
                throw new InstrumentDeclarationException(
                    symbol,
                    "is not an Oanda pair. Expected two ISO currency codes joined by an underscore, " +
                    "e.g. EUR_USD.");
            }

            return currencies[1];
        }

        /// <summary>Returns whether <paramref name="currency"/> is shaped like an ISO 4217 code.</summary>
        private static bool IsIsoCurrencyCode(string currency)
        {
            if (currency.Length != 3)
            {
                return false;
            }

            foreach (char character in currency)
            {
                if (!char.IsLetter(character))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether a pair quoting in <paramref name="quoteCurrency"/> already quotes in the
        /// account's own currency, compared case-insensitively so an ISO code's casing never decides it —
        /// matching how the <see cref="CurrencyConverter"/>'s cross-check compares them.
        /// </summary>
        private static bool QuotesInAccountCurrency(string quoteCurrency, string accountCurrency)
        {
            return string.Equals(quoteCurrency, accountCurrency, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether Oanda names the pair between these two currencies account-currency-first, which
        /// it does when the account currency outranks the quote currency in the precedence order.
        /// </summary>
        private static bool AccountCurrencyIsQuotedFirst(string quoteCurrency, string accountCurrency)
        {
            return RankOf(accountCurrency) < RankOf(quoteCurrency);
        }

        /// <summary>
        /// Returns a currency's position in Oanda's base-currency precedence order. A currency the table
        /// does not list ranks below every one it does: Oanda's base-side currencies are fully enumerated,
        /// so anything unrecognized is a currency Oanda quotes second.
        /// </summary>
        private static int RankOf(string currency)
        {
            if (string.Equals(currency, UniversalQuoteCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return Precedence.Length + 1;
            }

            int rank = Array.IndexOf(Precedence, currency.ToUpperInvariant());

            return rank >= 0 ? rank : Precedence.Length;
        }
    }
}
