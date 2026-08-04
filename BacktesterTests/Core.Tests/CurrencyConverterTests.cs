using System.Collections.Generic;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class CurrencyConverterTests
    {
        [Fact]
        public void ToAccountCurrency_DeclaredConversion_DividesByObservedRate()
        {
            // EUR_JPY quotes in JPY; the account is USD. USD_JPY's observed close (150) is the conversion
            // rate: JPY units per 1 USD. 15,000 JPY therefore converts to 15,000/150 = 100 USD.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            CurrencyConverter converter = new("USD", instruments);
            converter.ObserveRate("USD_JPY", 150m);

            decimal converted = converter.ToAccountCurrency("EUR_JPY", 15_000m);

            Assert.Equal(100m, converted);
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
