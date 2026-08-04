using System.Collections.Generic;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class CurrencyConverterTests
    {
        [Fact]
        public void ToAccountCurrency_DeclaringNoConversionOperation_DividesByObservedRate()
        {
            // EUR_JPY quotes in JPY; the account is USD. USD_JPY's observed close (150) is the conversion
            // rate: JPY units per 1 USD. 15,000 JPY therefore converts to 15,000/150 = 100 USD. The
            // Instrument declares no Conversion operation, so it gets Divide — every pre-existing
            // declaration keeps its meaning.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            CurrencyConverter converter = new("USD", instruments);
            converter.ObserveRate("USD_JPY", 150m);

            decimal converted = converter.ToAccountCurrency("EUR_JPY", 15_000m);

            Assert.Equal(100m, converted);
        }

        [Fact]
        public void ToAccountCurrency_ConversionOperationMultiply_MultipliesByObservedRate()
        {
            // EUR_GBP quotes in GBP; the account is USD and no USD-first pair exists, so it converts through
            // GBP_USD — USD per 1 GBP. At 1.25, 800 GBP is 800 * 1.25 = 1,000 USD.
            Instrument[] instruments =
            {
                new()
                {
                    Symbol = "EUR_GBP",
                    QuoteCurrency = "GBP",
                    ConversionSymbol = "GBP_USD",
                    ConversionOperation = ConversionOperation.Multiply
                }
            };
            CurrencyConverter converter = new("USD", instruments);
            converter.ObserveRate("GBP_USD", 1.25m);

            decimal converted = converter.ToAccountCurrency("EUR_GBP", 800m);

            Assert.Equal(1_000m, converted);
        }

        [Fact]
        public void ToAccountCurrency_DeclaredConversionWithNoObservedRate_Throws()
        {
            // The whole point of the module: a run configured slightly wrong must fail rather than present
            // JPY numbers as USD. The message names both the symbol and the series that never printed.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            CurrencyConverter converter = new("USD", instruments);

            MissingConversionRateException exception =
                Assert.Throws<MissingConversionRateException>(() => converter.ToAccountCurrency("EUR_JPY", 15_000m));

            Assert.Contains("EUR_JPY", exception.Message);
            Assert.Contains("USD_JPY", exception.Message);
        }

        [Fact]
        public void ToAccountCurrency_SymbolDeclaringNoConversionSymbol_ReturnsAmountUnchanged()
        {
            // AAPL quotes in the account's own currency, so it declares no Conversion symbol and needs no
            // conversion machinery at all — even with an unrelated rate observed.
            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD" } };
            CurrencyConverter converter = new("USD", instruments);
            converter.ObserveRate("USD_JPY", 150m);

            decimal converted = converter.ToAccountCurrency("AAPL", 1_500m);

            Assert.Equal(1_500m, converted);
        }

        [Fact]
        public void ToAccountCurrency_NoConversionSymbolAndNoRateEverObserved_ReturnsAmountUnchanged()
        {
            // The fail-loud rule is confined to declared conversions: a symbol quoting in the account's own
            // currency converts as identity on the very first bar, before any rate could have printed.
            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD" } };
            CurrencyConverter converter = new("USD", instruments);

            decimal converted = converter.ToAccountCurrency("AAPL", 1_500m);

            Assert.Equal(1_500m, converted);
        }

        [Fact]
        public void Construction_QuoteCurrencyDiffersFromAccountCurrencyWithNoConversionSymbol_Throws()
        {
            // A forgotten Conversion symbol is caught the moment the Portfolio is built, not silently
            // carried into a run that would then report JPY amounts as USD.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY" } };

            InstrumentDeclarationException exception =
                Assert.Throws<InstrumentDeclarationException>(() => new CurrencyConverter("USD", instruments));

            Assert.Contains("EUR_JPY", exception.Message);
        }

        [Fact]
        public void Construction_QuoteCurrencyEqualsAccountCurrencyButDeclaresAConversionSymbol_Throws()
        {
            // A symbol already quoting in USD has nothing to convert, so a Conversion symbol on it means the
            // declaration contradicts itself — fetching and dividing by USD_JPY would corrupt every amount.
            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD", ConversionSymbol = "USD_JPY" } };

            InstrumentDeclarationException exception =
                Assert.Throws<InstrumentDeclarationException>(() => new CurrencyConverter("USD", instruments));

            Assert.Contains("AAPL", exception.Message);
        }

        [Fact]
        public void Construction_InstrumentDeclaringNoQuoteCurrency_Throws()
        {
            // Quote currency is required on every Instrument: it is what the cross-check compares against
            // the account currency, so an undeclared one leaves the conversion rule unverifiable.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", ConversionSymbol = "USD_JPY" } };

            InstrumentDeclarationException exception =
                Assert.Throws<InstrumentDeclarationException>(() => new CurrencyConverter("USD", instruments));

            Assert.Contains("EUR_JPY", exception.Message);
        }

        [Fact]
        public void ConversionSymbols_InstrumentsSharingAConversionSymbol_ReportsItOnce()
        {
            // Two JPY-quoted pairs both convert through USD_JPY and AAPL converts through nothing, so the
            // series a caller must fetch is exactly one: USD_JPY.
            Instrument[] instruments =
            {
                new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" },
                new() { Symbol = "GBP_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" },
                new() { Symbol = "AAPL", QuoteCurrency = "USD" }
            };
            CurrencyConverter converter = new("USD", instruments);

            IReadOnlyCollection<string> conversionSymbols = converter.ConversionSymbols;

            Assert.Equal(new[] { "USD_JPY" }, conversionSymbols);
        }
    }
}
