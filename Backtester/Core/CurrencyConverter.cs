using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// Translates quote-currency amounts into the account's own currency. It holds each Instrument's
    /// conversion declaration, cross-checks it against the account currency at construction, observes
    /// Conversion-symbol closes as bars arrive, and applies the declared Conversion operation — identity
    /// for an Instrument declaring no Conversion symbol, and a loud refusal for a declared conversion
    /// whose rate has never been observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Public because multi-currency forex accounting (ADR 0029, ADR 0030) is a shipped, documented
    /// capability whose rate state an app can own directly — constructing the converter from its
    /// Instruments, feeding it closes through <see cref="ObserveRate"/> and translating with
    /// <see cref="ToAccountCurrency"/>. That capability is what keeps the type public, not any app
    /// currently known to name it.
    /// </para>
    /// <para>
    /// <b>Fill-timing invariant.</b> A fill translates at the conversion pair's <em>last completed
    /// close</em> — never a rate that was not yet knowable while the fill's own bar was trading — while an
    /// end-of-bar equity mark translates at the pair's <em>current</em> close, the freshest known rate.
    /// The converter's rate state is what carries this: a caller stepping through bars must apply that
    /// bar's fills before calling <see cref="ObserveRate"/> with the same bar's Conversion-symbol close,
    /// so a fill converts against the prior observation and the mark that follows converts against the new
    /// one. Reversing those two steps is currency lookahead. The engine's bar loop upholds this by
    /// filling orders before recording the equity snapshot that feeds the rate.
    /// </para>
    /// <para>
    /// Translation moves money, never execution semantics: the fill price recorded for an order stays
    /// exactly the gap-aware fill price in the Instrument's own quote currency (ADR 0024), whatever the
    /// rate does.
    /// </para>
    /// <para>
    /// Intra-bar rate precision — translating a fill at the conversion bar's open rather than the previous
    /// close — was considered and rejected: a negligible realism gain for extra per-bar state. It remains
    /// revisitable, and this is the one place it would be revised.
    /// </para>
    /// </remarks>
    public class CurrencyConverter
    {
        private readonly string _accountCurrency;

        // Key: symbol/ticker -> the Conversion symbol whose price converts that symbol's quote currency
        // into the account currency, paired with the operation to apply to that rate. Entries exist only
        // for Instruments declaring a Conversion symbol; a symbol absent here already quotes in the
        // account currency.
        private readonly Dictionary<string, ConversionDeclaration> _declarationBySymbol = new();

        // Key: Conversion symbol -> its most recently observed close, the rate conversion divides or
        // multiplies by according to the converting symbol's declared Conversion operation.
        private readonly Dictionary<string, decimal> _rateByConversionSymbol = new();

        // The distinct Conversion symbols declared at construction, held apart from the observed rates so
        // the series a caller must fetch is known before any rate has printed.
        private readonly HashSet<string> _conversionSymbols = new();

        /// <summary>
        /// Initializes a converter for an account denominated in <paramref name="accountCurrency"/>,
        /// holding the conversion declarations of <paramref name="instruments"/> after cross-checking each
        /// against that currency. Null or empty instruments yield a converter that converts nothing, as
        /// every symbol then quotes in the account's own currency.
        /// </summary>
        /// <exception cref="InstrumentDeclarationException">
        /// An Instrument declares no quote currency, or its quote currency and Conversion symbol
        /// contradict each other against the account currency.
        /// </exception>
        public CurrencyConverter(string accountCurrency, Instrument[] instruments)
        {
            _accountCurrency = accountCurrency;

            if (instruments == null)
            {
                return;
            }

            foreach (Instrument instrument in instruments)
            {
                ValidateDeclaration(instrument);

                if (instrument.ConversionSymbol != null)
                {
                    _declarationBySymbol[instrument.Symbol] =
                        new ConversionDeclaration(instrument.ConversionSymbol, instrument.ConversionOperation);
                    _conversionSymbols.Add(instrument.ConversionSymbol);
                }
            }
        }

        /// <summary>
        /// Throws unless <paramref name="instrument"/>'s currency declaration is consistent with the account
        /// currency: a quote currency is required, one differing from the account's requires a Conversion
        /// symbol, and one equal to it forbids one. This is what turns the quote currency declaration into
        /// a load-bearing cross-check rather than an unread annotation.
        /// </summary>
        private void ValidateDeclaration(Instrument instrument)
        {
            if (string.IsNullOrWhiteSpace(instrument.QuoteCurrency))
            {
                throw new InstrumentDeclarationException(
                    instrument.Symbol,
                    "no quote currency declared. Every Instrument must state the currency its price is " +
                    "quoted in, so it can be checked against the account currency.");
            }

            if (QuotesInAccountCurrency(instrument))
            {
                if (instrument.ConversionSymbol != null)
                {
                    throw new InstrumentDeclarationException(
                        instrument.Symbol,
                        $"quote currency {instrument.QuoteCurrency} is already the account currency, so it must " +
                        $"declare no Conversion symbol, but declares {instrument.ConversionSymbol}.");
                }

                return;
            }

            if (instrument.ConversionSymbol == null)
            {
                throw new InstrumentDeclarationException(
                    instrument.Symbol,
                    $"quote currency {instrument.QuoteCurrency} differs from the account currency " +
                    $"{_accountCurrency}, so it must declare the Conversion symbol to translate through.");
            }
        }

        /// <summary>
        /// Returns whether <paramref name="instrument"/>'s price already quotes in the account's own
        /// currency, compared case-insensitively so an ISO code's casing never decides the cross-check.
        /// </summary>
        private bool QuotesInAccountCurrency(Instrument instrument)
        {
            return string.Equals(instrument.QuoteCurrency, _accountCurrency, StringComparison.OrdinalIgnoreCase);
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
        /// printed, and the rate stands until the next observation. Call it <em>after</em> applying the
        /// same bar's fills — that ordering is the fill-timing invariant documented on this class.
        /// </summary>
        public void ObserveRate(string conversionSymbol, decimal close)
        {
            _rateByConversionSymbol[conversionSymbol] = close;
        }

        /// <summary>
        /// Returns <paramref name="nativeAmount"/> — denominated in <paramref name="symbol"/>'s own quote
        /// currency — converted into the account currency by applying the symbol's Conversion operation to
        /// the latest observed close of its declared Conversion symbol: dividing by an account-first rate
        /// (e.g. <c>USD_JPY</c> for a JPY-quoted symbol in a USD account), multiplying by a quote-first one
        /// (e.g. <c>GBP_USD</c> for a GBP-quoted symbol). Returns the amount unchanged when the symbol
        /// declares no conversion.
        /// </summary>
        /// <exception cref="MissingConversionRateException">
        /// The symbol declares a Conversion symbol for which no rate has been observed.
        /// </exception>
        public decimal ToAccountCurrency(string symbol, decimal nativeAmount)
        {
            if (!_declarationBySymbol.TryGetValue(symbol, out ConversionDeclaration declaration))
            {
                return nativeAmount;
            }

            if (!_rateByConversionSymbol.TryGetValue(declaration.ConversionSymbol, out decimal rate))
            {
                throw new MissingConversionRateException(symbol, declaration.ConversionSymbol);
            }

            return declaration.Operation == ConversionOperation.Multiply
                ? nativeAmount * rate
                : nativeAmount / rate;
        }

        /// <summary>
        /// One Instrument's conversion declaration as the converter needs it: which series carries the rate
        /// and which way that series is quoted. Kept together so the two can never drift apart.
        /// </summary>
        private sealed record ConversionDeclaration(string ConversionSymbol, ConversionOperation Operation);
    }
}
