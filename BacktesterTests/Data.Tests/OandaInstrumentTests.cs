using Backtester.Core;
using Backtester.Data.Oanda;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class OandaInstrumentTests
    {
        [Fact]
        public void For_PairQuotedInJpyWithUsdAccount_DeclaresAccountFirstPairAndDivide()
        {
            // EUR_JPY quotes in JPY; the account is USD. Oanda names the USD/JPY pair USD_JPY — the
            // account currency first — so its price is JPY per 1 USD and converting JPY amounts into USD
            // divides by it.
            Instrument instrument = OandaInstrument.For("EUR_JPY", "USD");

            Assert.Equal("JPY", instrument.QuoteCurrency);
            Assert.Equal("USD_JPY", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Divide, instrument.ConversionOperation);
        }

        [Fact]
        public void For_CrossQuotedInGbpWithUsdAccount_DeclaresQuoteFirstPairAndMultiply()
        {
            // EUR_GBP quotes in GBP; the account is USD. GBP outranks USD, so Oanda names the pair
            // GBP_USD — the quote currency first — and its price is USD per 1 GBP, which converting GBP
            // amounts into USD multiplies by. No USD-first pair exists to divide by.
            Instrument instrument = OandaInstrument.For("EUR_GBP", "USD");

            Assert.Equal("GBP", instrument.QuoteCurrency);
            Assert.Equal("GBP_USD", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Multiply, instrument.ConversionOperation);
        }

        [Fact]
        public void For_PairQuotedInAccountCurrency_DeclaresNoConversion()
        {
            // EUR_USD already quotes in USD, so a USD account has nothing to translate: no Conversion
            // symbol to fetch and no rate to apply.
            Instrument instrument = OandaInstrument.For("EUR_USD", "USD");

            Assert.Equal("USD", instrument.QuoteCurrency);
            Assert.Null(instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Divide, instrument.ConversionOperation);
        }

        [Fact]
        public void For_AnyPair_CarriesTheSymbolThroughVerbatim()
        {
            // The provider passes the symbol straight into Oanda's {instrument} path segment, so the
            // factory must not normalize or rewrite what it was handed.
            Instrument instrument = OandaInstrument.For("EUR_JPY", "USD");

            Assert.Equal("EUR_JPY", instrument.Symbol);
        }

        [Fact]
        public void For_SymbolThatIsNotAnOandaPair_ThrowsNamingTheSymbol()
        {
            // Rejected at declaration time rather than mid-run, and the message names the symbol so the
            // caller knows which of their declarations to fix.
            InstrumentDeclarationException exception =
                Assert.Throws<InstrumentDeclarationException>(() => OandaInstrument.For("EURUSD", "USD"));

            Assert.Equal("EURUSD", exception.Symbol);
            Assert.Contains("EURUSD", exception.Message);
        }

        [Fact]
        public void For_QuoteCurrencyOutsideThePrecedenceTable_RanksItBelowEveryKnownCurrency()
        {
            // Oanda's base-side currencies are fully enumerated, so a currency the table has never heard
            // of is one Oanda quotes second: the pair is named account-currency-first and converting
            // divides by it.
            Instrument instrument = OandaInstrument.For("USD_XYZ", "USD");

            Assert.Equal("USD_XYZ", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Divide, instrument.ConversionOperation);
        }

        [Fact]
        public void For_JpyQuotedPairInAnAccountCurrencyOutsideTheTable_RanksJpyBelowTheUnknownCurrency()
        {
            // JPY is Oanda's universal quote currency — SGD_JPY, TRY_JPY and ZAR_JPY all exist — so it
            // ranks below even a currency the table has never heard of, and the pair is named with that
            // currency first.
            Instrument instrument = OandaInstrument.For("EUR_JPY", "XYZ");

            Assert.Equal("XYZ_JPY", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Divide, instrument.ConversionOperation);
        }

        [Fact]
        public void For_ChfQuotedPairInAnSgdAccount_OrdersTheNonMajorsAsOandaListsThem()
        {
            // SGD outranks CHF at Oanda — SGD_CHF exists, CHF_SGD does not — even though CHF is a major
            // and SGD is not. The precedence order follows Oanda's own instrument list, not a
            // majors-before-minors rule of thumb.
            Instrument instrument = OandaInstrument.For("EUR_CHF", "SGD");

            Assert.Equal("SGD_CHF", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Divide, instrument.ConversionOperation);
        }

        [Fact]
        public void For_UsdQuotedPairInAJpyAccount_DeclaresQuoteFirstPairAndMultiply()
        {
            // A JPY account inverts the common case: JPY is Oanda's universal quote currency, so the
            // account currency is the one named second and every conversion multiplies rather than divides.
            Instrument instrument = OandaInstrument.For("EUR_USD", "JPY");

            Assert.Equal("USD_JPY", instrument.ConversionSymbol);
            Assert.Equal(ConversionOperation.Multiply, instrument.ConversionOperation);
        }

        [Fact]
        public void For_LowercaseAccountCurrency_DerivesAnUppercaseConversionSymbol()
        {
            // The Conversion symbol is derived, not passed through, and Oanda's instrument names are
            // uppercase — so an account currency's casing must not leak into a symbol the provider would
            // then fail to fetch.
            Instrument instrument = OandaInstrument.For("EUR_JPY", "usd");

            Assert.Equal("USD_JPY", instrument.ConversionSymbol);
        }

        [Theory]
        [InlineData("EUR_JPY", "USD_JPY")]  // converts through an account-first pair
        [InlineData("EUR_GBP", "GBP_USD")]  // converts through a quote-first pair
        public void For_PairNeedingConversion_ProducesADeclarationTheConverterRegisters(
            string symbol, string expectedConversionSymbol)
        {
            // The whole point of the factory is that a caller cannot forget a Conversion symbol, so what it
            // returns must survive the converter's construction cross-check and leave the converter asking
            // for exactly the series that declaration named.
            Instrument[] instruments = { OandaInstrument.For(symbol, "USD") };

            CurrencyConverter converter = new("USD", instruments);

            Assert.Equal(new[] { expectedConversionSymbol }, converter.ConversionSymbols);
        }

        [Fact]
        public void For_PairQuotedInAccountCurrency_ProducesADeclarationNeedingNoSeries()
        {
            // The mirror of the above: the factory cannot declare a Conversion symbol that isn't needed
            // either, which the cross-check would reject just as loudly. Nothing extra gets fetched.
            Instrument[] instruments = { OandaInstrument.For("EUR_USD", "USD") };

            CurrencyConverter converter = new("USD", instruments);

            Assert.Empty(converter.ConversionSymbols);
        }
    }
}
