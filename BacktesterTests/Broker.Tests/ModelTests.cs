using System;
using System.Collections.Generic;
using Backtester.Core;
using Backtester.ExecutionModels.Sizing;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class ModelTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static OrderRequest BuyRequest(string symbol, decimal price, int qty = 1)
        {
            return new()
            {
                Symbol = symbol,
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Price = price,
                Quantity = qty
            };
        }

        private static MarketSlice SliceWithBar(string symbol, decimal close, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbol] = new Candle { Timestamp = ts, Open = close, High = close, Low = close, Close = close, Volume = 1000 }
                }
            };
        }

        // --- RiskPerTradeSizing ---


        [Fact]
        public void RiskPerTradeSizing_ReturnsExpectedShares()
        {
            // 1% of $10,000 = $100 risk budget; stop distance = |$50 - $45| = $5 → floor($100/$5) = 20 shares
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_WiderStop_YieldsSmallerSize()
        {
            // Same equity and fraction; stop distance = |$50 - $40| = $10 → floor($100/$10) = 10 shares
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(10, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_UsesRealizedEquity_NotCashAlone()
        {
            // Buy 10@$100 → Cash=$9,000; cost-basis equity=$9,000+$1,000=$10,000
            // 2% of $10,000=$200 budget; stop distance=|$50-$40|=$10 → floor($200/$10)=20
            // If using cash only: floor(0.02×$9,000/$10)=18 — proves cost-basis base
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.02m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 10, Timestamp = DateTime.UtcNow });
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_SizesFromStopOffset_WhenNoAbsoluteStop()
        {
            // A fill-relative bracket entry has no absolute Price/StopPrice at submit time; the per-share risk
            // is the offset. 1% of $10,000 = $100 budget; offset $5 → floor($100/$5) = 20 shares.
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, StopOffset = 5m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_PrefersStopOffset_OverAbsoluteStop()
        {
            // With both present the fill-relative offset wins: it is the risk the fill will actually realize
            // (ADR 0025), so offset $5 → 20 shares, not the $10 absolute distance's 10.
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, StopOffset = 5m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_ReturnsZero_WhenNoStopDistanceAtAll()
        {
            // A plain market order carries neither an offset nor an absolute stop, so there is no risk to
            // divide the budget by and nothing to size — the model declines rather than guessing a size.
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_ConvertsStopDistance_ForCrossCurrencyInstrument()
        {
            // EUR_JPY quotes in JPY; account is USD, rate 150 JPY-per-USD (seeded via USD_JPY close).
            // 1% of $10,000 = $100 budget. Native stop distance = |15,000-14,250| = 750 JPY, converted
            // through the 150 rate = 5 USD -> floor($100/$5) = 20 shares. Left unconverted (750 JPY)
            // this would floor($100/$750) = 0.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            OrderRequest request = new() { Symbol = "EUR_JPY", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 15_000m, StopPrice = 14_250m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_UnaffectedBySameCurrencyInstrument()
        {
            // AAPL's Instrument declares QuoteCurrency == AccountCurrency (no ConversionSymbol), so
            // ToAccountCurrency is an identity conversion (effectively 1:1) — same inputs as
            // RiskPerTradeSizing_ReturnsExpectedShares must yield the same 20 shares.
            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD" } };
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void RiskPerTradeSizing_SizesFromTranslatedEquity_WhileACrossCurrencyPositionIsOpen()
        {
            // A EUR_JPY long is open: 10 @ 15,000 JPY at rate 150 left Cash at $9,000 against a $1,000
            // cost basis, so realized equity is still $10,000 and a 1% budget on a $5 stop sizes the next
            // (USD) trade at 20 shares. Adding the position's native 150,000 JPY basis to account-currency
            // cash would budget against $159,000 and size 318 - the open forex position inflating the
            // account by roughly the exchange rate.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            RiskPerTradeSizing sizing = new() { RiskFraction = 0.01m };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "EUR_JPY", Side = OrderSide.Buy, Price = 15_000m, Quantity = 10, Timestamp = T0 });
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        // --- PercentNotionalSizing ---

        [Fact]
        public void PercentNotionalSizing_ReturnsCorrectQuantity()
        {
            // 10% of $10,000 = $1,000; at $50/share = 20 shares
            PercentNotionalSizing sizing = new() { Percent =0.10m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void PercentNotionalSizing_ReturnsZero_WhenCashTooLowForOneShare()
        {
            // 1% of $100 = $1; at $50/share = 0 shares
            PercentNotionalSizing sizing = new() { Percent =0.01m };
            Portfolio portfolio = new(100m);
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        [Fact]
        public void PercentNotionalSizing_SizesOffBuyingPower_NotCashAlone()
        {
            // Buy 100@$100 spends all cash (Cash=$0) but leaves buying power: MarkedEquity $10,000 −
            // committed margin (0.5×$10,000=$5,000) = $5,000. 10% of $5,000=$500 at $50/share = 10 shares.
            // If sized off cash ($0) this would be 0 — proving the buying-power base.
            PercentNotionalSizing sizing = new() { Percent = 0.10m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 100, Timestamp = T0 });
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0m, portfolio.Cash);
            Assert.Equal(10, qty);
        }

        [Fact]
        public void PercentNotionalSizing_ReturnsZero_WhenBuyingPowerExhausted()
        {
            // Buy 200@$100 deploys the full 2:1 long margin: Cash=−$10,000, MarkedEquity=$10,000,
            // committed margin=0.5×$20,000=$10,000 → BuyingPower=$0. With nothing left to open on, sizing
            // must return zero (never a negative share count from the net-borrowed cash balance).
            PercentNotionalSizing sizing = new() { Percent = 0.20m };
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "t1", Symbol = "MSFT", Side = OrderSide.Buy, Price = 100m, Quantity = 200, Timestamp = T0 });
            OrderRequest request = BuyRequest("AAPL", price: 50m);

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0m, portfolio.BuyingPower);
            Assert.True(portfolio.Cash < 0m);
            Assert.Equal(0, qty);
        }

        // --- FixedRiskSizing ---

        [Fact]
        public void FixedRiskSizing_ReturnsExpectedShares()
        {
            // $100 fixed risk budget; stop distance = |$50 - $45| = $5 → floor($100/$5) = 20 shares
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void FixedRiskSizing_SizesFromStopOffset_WhenNoAbsoluteStop()
        {
            // A fill-relative bracket entry has no absolute Price/StopPrice at submit time; the per-share risk
            // is the offset. $100 budget; offset $5 → floor($100/$5) = 20 shares.
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, StopOffset = 5m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void FixedRiskSizing_ReturnsZero_WhenNoStopDistanceAtAll()
        {
            // A plain market order carries neither an offset nor an absolute stop, so there is no risk to
            // divide the budget by — the model declines rather than risking an unknown amount.
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        [Fact]
        public void FixedRiskSizing_ReturnsZero_WhenRiskAmountNonPositive()
        {
            // A zero or negative risk budget must decline rather than emit a negative share count that would
            // open the wrong side and corrupt Position/RoundTrip state.
            FixedRiskSizing sizing = new() { RiskAmount = -100m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }

        [Fact]
        public void FixedRiskSizing_PrefersStopOffset_OverAbsoluteStop()
        {
            // With both present the fill-relative offset wins: it is the risk the fill will actually realize
            // (ADR 0025), so offset $5 → 20 shares, not the $10 absolute distance's 10.
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 40m, StopOffset = 5m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void FixedRiskSizing_ConvertsStopDistance_ForCrossCurrencyInstrument()
        {
            // EUR_JPY quotes in JPY; account is USD, rate 150 JPY-per-USD (seeded via USD_JPY close).
            // $100 fixed budget. Native stop distance = |15,000-14,250| = 750 JPY, converted through the
            // 150 rate = 5 USD -> floor($100/$5) = 20 shares. Left unconverted (750 JPY) this would
            // floor($100/$750) = 0.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            OrderRequest request = new() { Symbol = "EUR_JPY", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 15_000m, StopPrice = 14_250m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void FixedRiskSizing_UnaffectedBySameCurrencyInstrument()
        {
            // AAPL's Instrument declares QuoteCurrency == AccountCurrency (no ConversionSymbol), so
            // ToAccountCurrency is an identity conversion (effectively 1:1) — same inputs as
            // FixedRiskSizing_ReturnsExpectedShares must yield the same 20 shares.
            Instrument[] instruments = { new() { Symbol = "AAPL", QuoteCurrency = "USD" } };
            FixedRiskSizing sizing = new() { RiskAmount = 100m };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(20, qty);
        }

        [Fact]
        public void FixedRiskSizing_ReturnsZero_WhenBudgetBelowOneShareOfRisk()
        {
            // A single share would already lose more than the budget: $3 budget, $5 stop distance →
            // floor($3/$5) = 0. The model declines rather than over-risking on one share.
            FixedRiskSizing sizing = new() { RiskAmount = 3m };
            Portfolio portfolio = new(10_000m);
            OrderRequest request = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 50m, StopPrice = 45m, Quantity = 1 };

            int qty = sizing.Size(request, portfolio);

            Assert.Equal(0, qty);
        }
    }
}
