using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;
using Xunit;

namespace BacktesterTests.Core.Tests
{
    public class PortfolioTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static Trade Buy(string symbol, decimal price, int qty, decimal commission = 0m)
        {
            return new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = OrderSide.Buy,
                Price = price,
                Quantity = qty,
                Commission = commission,
                Timestamp = T0
            };
        }

        private static Trade Sell(string symbol, decimal price, int qty, decimal commission = 0m)
        {
            return new()
            {
                Id = Guid.NewGuid().ToString(),
                Symbol = symbol,
                Side = OrderSide.Sell,
                Price = price,
                Quantity = qty,
                Commission = commission,
                Timestamp = T0
            };
        }


        [Fact]
        public void Constructor_NoAccountCurrencyGiven_DefaultsToUsd()
        {
            Portfolio portfolio = new(10_000m);

            Assert.Equal("USD", portfolio.AccountCurrency);
        }

        [Fact]
        public void Constructor_AccountCurrencyGiven_SetsAccountCurrency()
        {
            Portfolio portfolio = new(10_000m, "JPY");

            Assert.Equal("JPY", portfolio.AccountCurrency);
        }

        [Fact]
        public void SnapshotAt_FreshPortfolio_ReturnsCashAndTimestamp()
        {
            Portfolio portfolio = new(10_000m);

            PortfolioSnapshot snapshot = portfolio.SnapshotAt(T0);

            Assert.Equal(10_000m, snapshot.Cash);
            Assert.Equal(T0, snapshot.Timestamp);
            Assert.Empty(snapshot.Positions);
        }

        [Fact]
        public void ApplyTrade_Buy_ReducesCashByNotional()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            Assert.Equal(9_000m, portfolio.Cash);
        }

        [Fact]
        public void ApplyTrade_Buy_DeductsCashByNotionalPlusCommission()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Buy("AAPL", 100m, 10, commission: 5m));

            Assert.Equal(8_995m, portfolio.Cash);
        }

        [Fact]
        public void OpenQuantity_FlatSymbol_ReturnsZero()
        {
            Portfolio portfolio = new(10_000m);

            Assert.Equal(0, portfolio.OpenQuantity("AAPL"));
        }

        [Fact]
        public void OpenQuantity_ShortPosition_ReturnsSignedQuantity()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10));

            Assert.Equal(-10, portfolio.OpenQuantity("AAPL"));
        }

        [Fact]
        public void ReducesOpenPosition_SellAgainstLong_IsReducing()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            bool reduces = portfolio.ReducesOpenPosition(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market });

            Assert.True(reduces);
        }

        [Fact]
        public void ReducesOpenPosition_BuyAgainstShort_IsReducing()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 100m, 10));

            bool reduces = portfolio.ReducesOpenPosition(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market });

            Assert.True(reduces);
        }

        [Fact]
        public void ReducesOpenPosition_BuyAddingToLong_IsNotReducing()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            bool reduces = portfolio.ReducesOpenPosition(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market });

            Assert.False(reduces);
        }

        [Fact]
        public void ReducesOpenPosition_OrderAgainstFlatSymbol_IsNotReducing()
        {
            Portfolio portfolio = new(10_000m);

            bool reduces = portfolio.ReducesOpenPosition(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market });

            Assert.False(reduces);
        }

        [Fact]
        public void ApplyTrade_Buy_CreatesPosition()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            Assert.Single(portfolio.Positions);
            Assert.Equal("AAPL", portfolio.Positions[0].Symbol);
            Assert.Equal(10, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void ApplyTrade_SecondBuySameSymbol_UpdatesExistingPosition()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.ApplyTrade(Buy("AAPL", 110m, 5));

            Assert.Single(portfolio.Positions);
            Assert.Equal(15, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void ApplyTrade_TwoDifferentSymbols_CreatesTwoPositions()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));
            portfolio.ApplyTrade(Buy("MSFT", 200m, 5));

            Assert.Equal(2, portfolio.Positions.Count);
        }

        [Fact]
        public void ApplyTrade_Sell_IncreasesCashByNotionalMinusCommission()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 10, commission: 5m));

            // 10000 - 1000 (buy) + 1200 - 5 (sell)
            Assert.Equal(10_195m, portfolio.Cash);
        }

        [Fact]
        public void ApplyTrade_Sell_ReducesPositionQuantity()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 5));

            Assert.Equal(5, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void SnapshotAt_AfterTrade_IncludesPosition()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            PortfolioSnapshot snapshot = portfolio.SnapshotAt(T0);

            Assert.Single(snapshot.Positions);
            Assert.Equal(9_000m, snapshot.Cash);
        }

        // --- Shorting ---

        [Fact]
        public void ApplyTrade_SellFromFlat_OpensShortPosition()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Sell("AAPL", 150m, 10));

            Assert.Single(portfolio.Positions);
            Assert.Equal(-10, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void ApplyTrade_SellFromFlat_CreditsCashByProceeds()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.ApplyTrade(Sell("AAPL", 150m, 10, commission: 5m));

            // 10000 + 1500 (proceeds) - 5 (commission)
            Assert.Equal(11_495m, portfolio.Cash);
        }

        [Fact]
        public void ApplyTrade_BuyCoveringShort_DebitsCashByCoverCost()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10));   // Cash = 11500, short 10@150

            portfolio.ApplyTrade(Buy("AAPL", 140m, 10, commission: 5m));

            // 11500 - 1400 (cover) - 5 (commission)
            Assert.Equal(10_095m, portfolio.Cash);
        }

        [Fact]
        public void ApplyTrade_BuyCoveringShort_RealizesShortPnL()
        {
            // Short 10@150, cover 10@140 → realized = (150-140)*10 = 100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10));

            portfolio.ApplyTrade(Buy("AAPL", 140m, 10));

            Assert.Equal(100m, portfolio.RealizedPnL);
        }

        [Fact]
        public void ApplyTrade_BuyLargerThanShort_ClampedToOpenQuantity_SignNeverFlips()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 5));

            portfolio.ApplyTrade(Buy("AAPL", 140m, 10));

            Assert.Equal(0, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void RecordEquitySnapshot_ShortPosition_MarkedEquityRisesAsPriceFalls()
        {
            // Short 10@150 → Cash = 11500. Mark at 140 → position value = -1400 → MarkedEquity = 10100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Sell("AAPL", 150m, 10));

            portfolio.RecordEquitySnapshot(SliceWithBar("AAPL", 140m, T0));

            Assert.Equal(10_100m, portfolio.EquityHistory[0].MarkedEquity);
        }

        // --- Buying power / margin ---

        [Fact]
        public void BuyingPower_FlatAccount_EqualsCash()
        {
            Portfolio portfolio = new(10_000m);

            Assert.Equal(10_000m, portfolio.BuyingPower);
        }

        [Fact]
        public void BuyingPower_WithOpenLong_ReflectsCommittedInitialMargin()
        {
            // Long 100 @ 50 → MarkedEquity 10,000; committed margin 0.5 * 5,000 = 2,500 → buying power 7,500
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 50m, 100));

            Assert.Equal(7_500m, portfolio.BuyingPower);
        }

        // --- Per-instrument margin rate (ADR 0030) ---

        [Fact]
        public void InitialMarginForOrder_InstrumentWithMarginRate_AppliesSameRateToLongAndShort()
        {
            // 50:1 leverage (2%) on a 10,000-unit order @ 1.10: margin = 0.02 * 11,000 = 220, both directions.
            Instrument[] instruments = { new() { Symbol = "EUR_USD", MarginRate = 0.02m } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            OrderRequest buy = new() { Symbol = "EUR_USD", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10_000, Price = 1.10m };
            OrderRequest sell = new() { Symbol = "EUR_USD", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10_000, Price = 1.10m };

            decimal longMargin = portfolio.InitialMarginForOrder(buy);
            decimal shortMargin = portfolio.InitialMarginForOrder(sell);

            Assert.Equal(220m, longMargin);
            Assert.Equal(220m, shortMargin);
        }

        [Fact]
        public void InitialMarginForOrder_InstrumentWithoutMarginRate_FallsBackToRegTSplit()
        {
            // No MarginRate override declared for AAPL → existing Reg-T split applies unchanged.
            Instrument[] instruments = { new() { Symbol = "EUR_USD", MarginRate = 0.02m } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            OrderRequest buy = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 100, Price = 50m };
            OrderRequest sell = new() { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 100, Price = 50m };

            decimal longMargin = portfolio.InitialMarginForOrder(buy);
            decimal shortMargin = portfolio.InitialMarginForOrder(sell);

            Assert.Equal(2_500m, longMargin);
            Assert.Equal(7_500m, shortMargin);
        }

        [Fact]
        public void BuyingPower_MixedPortfolio_AppliesEachInstrumentsOwnMarginRateIndependently()
        {
            // EUR_USD long 10,000 @ 1.10 at 2% → margin 220. AAPL long 100 @ 50 (no override) → Reg-T 0.5 * 5,000 = 2,500.
            // MarkedEquity stays at StartingCash (both marked at entry price, no P&L) → BuyingPower = 20,000 - 220 - 2,500 = 17,280.
            Instrument[] instruments = { new() { Symbol = "EUR_USD", MarginRate = 0.02m }, new() { Symbol = "AAPL" } };
            Portfolio portfolio = new(20_000m, "USD", instruments);
            portfolio.ApplyTrade(Buy("EUR_USD", 1.10m, 10_000));
            portfolio.ApplyTrade(Buy("AAPL", 50m, 100));

            Assert.Equal(17_280m, portfolio.BuyingPower);
        }

        // --- Long-only guard (no-flip invariant) ---

        [Fact]
        public void ApplyTrade_SellLargerThanLong_ClampedToOpenQuantity_QuantityNeverNegative()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 5));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 10));

            Assert.Equal(0, portfolio.Positions[0].Quantity);
        }

        [Fact]
        public void ApplyTrade_SellLargerThanLong_CashReflectsClamped()
        {
            // Buy 5@100 → Cash=9500; oversell 10, clamped to 5@120 → Cash=9500+600=10100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 5));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 10));

            Assert.Equal(10_100m, portfolio.Cash);
        }

        // --- Equity naming ---

        [Fact]
        public void SnapshotAt_ExposesCostBasisEquity_ExcludingUnrealizedPnL()
        {
            // Buy 10@100 → Cash=9000, cost basis = 9000+1000 = 10000 (not mark-to-market)
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            PortfolioSnapshot snapshot = portfolio.SnapshotAt(T0);

            Assert.Equal(10_000m, snapshot.CostBasisEquity);
        }

        // --- EquityHistory / RecordEquitySnapshot ---

        private static MarketSlice EmptySlice(DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>()
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

        private static MarketSlice SliceWithTwoBars(string symbolA, decimal closeA, string symbolB, decimal closeB, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbolA] = new Candle { Timestamp = ts, Open = closeA, High = closeA, Low = closeA, Close = closeA, Volume = 1000 },
                    [symbolB] = new Candle { Timestamp = ts, Open = closeB, High = closeB, Low = closeB, Close = closeB, Volume = 1000 }
                }
            };
        }


        [Fact]
        public void EquityHistory_IsEmptyOnConstruction()
        {
            Portfolio portfolio = new(10_000m);

            Assert.Empty(portfolio.EquityHistory);
        }

        [Fact]
        public void RecordEquitySnapshot_AppendsOneEntryWithCorrectTimestamp()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.RecordEquitySnapshot(EmptySlice(T0));

            Assert.Single(portfolio.EquityHistory);
            Assert.Equal(T0, portfolio.EquityHistory[0].Timestamp);
        }

        [Fact]
        public void RecordEquitySnapshot_ExposesMarkedEquity_IncludingUnrealizedPnL()
        {
            // Buy 10@100 → Cash=9000; mark at 110 → position value=1100; MarkedEquity=10100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.RecordEquitySnapshot(SliceWithBar("AAPL", 110m, T0));

            Assert.Equal(10_100m, portfolio.EquityHistory[0].MarkedEquity);
        }

        [Fact]
        public void RecordEquitySnapshot_NoPositions_CashAndMarkedEquityEqualStartingCash()
        {
            Portfolio portfolio = new(10_000m);

            portfolio.RecordEquitySnapshot(EmptySlice(T0));

            Assert.Equal(10_000m, portfolio.EquityHistory[0].Cash);
            Assert.Equal(10_000m, portfolio.EquityHistory[0].MarkedEquity);
            Assert.Equal(0m, portfolio.EquityHistory[0].UnrealizedPnL);
        }

        [Fact]
        public void RecordEquitySnapshot_WithOpenPosition_UnrealizedPnLIsMarketValue()
        {
            // Buy 10 @ $100 → Cash = $9,000; position market value at $110 = $1,100
            // MarkedEquity = $9,000 + $1,100 = $10,100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.RecordEquitySnapshot(SliceWithBar("AAPL", 110m, T0));

            EquitySnapshot snap = portfolio.EquityHistory[0];
            Assert.Equal(9_000m, snap.Cash);
            Assert.Equal(1_100m, snap.UnrealizedPnL);
            Assert.Equal(10_100m, snap.MarkedEquity);
        }

        [Fact]
        public void RecordEquitySnapshot_SymbolNotInSlice_FallsBackToAveragePrice()
        {
            // Buy 10 @ $100; slice has no bar for AAPL → mark at avg price, UnrealizedPnL = $1,000, MarkedEquity = $10,000
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.RecordEquitySnapshot(EmptySlice(T0));

            EquitySnapshot snap = portfolio.EquityHistory[0];
            Assert.Equal(1_000m, snap.UnrealizedPnL);
            Assert.Equal(10_000m, snap.MarkedEquity);
        }

        // --- RealizedPnL ---

        // --- Multi-currency conversion (ADR 0029) ---

        [Fact]
        public void ApplyTrade_Buy_CrossCurrencyInstrument_ConvertsCashThroughConversionRate()
        {
            // EUR_JPY quotes in JPY; the account is USD. USD_JPY's last observed close (150) is the
            // conversion rate: JPY units per 1 USD. Buying 1 unit at 15,000 JPY costs 15,000/150 = 100 USD.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));

            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 1));

            Assert.Equal(9_900m, portfolio.Cash);
        }

        [Fact]
        public void ApplyTrade_Buy_CrossCurrencyInstrument_PositionAveragePriceStaysNative()
        {
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));

            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 1));

            Assert.Equal(15_000m, portfolio.Positions.Single().AveragePrice);
        }

        [Fact]
        public void ApplyTrade_SellClosingCrossCurrencyPosition_ConvertsRealizedPnL_ButKeepsRoundTripPricesNative()
        {
            // Buy 10 @ 15,000 JPY, sell 10 @ 15,300 JPY at a constant 150 JPY-per-USD rate: native gain =
            // (15,300-15,000)*10 = 3,000 JPY -> converted = 3,000/150 = 20 USD.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 10));

            portfolio.ApplyTrade(Sell("EUR_JPY", 15_300m, 10));

            Assert.Equal(20m, portfolio.RealizedPnL);
            RoundTrip roundTrip = portfolio.RoundTrips.Single();
            Assert.Equal(20m, roundTrip.RealizedPnL);
            Assert.Equal(15_000m, roundTrip.EntryPrice);
            Assert.Equal(15_300m, roundTrip.ExitPrice);
        }

        [Fact]
        public void RecordEquitySnapshot_CrossCurrencyInstrument_MarkedEquityReflectsConvertedMarketValue()
        {
            // Buy 10 @ 15,000 JPY (rate 150) -> Cash = 10,000 - 1,000 = 9,000. Mark at 15,300 (still rate
            // 150): native market value = 15,300*10 = 153,000 JPY -> converted = 1,020 USD.
            // MarkedEquity = Cash(9,000) + convertedValue(1,020) = 10,020.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 10));

            portfolio.RecordEquitySnapshot(SliceWithTwoBars("EUR_JPY", 15_300m, "USD_JPY", 150m, T0.AddDays(1)));

            Assert.Equal(10_020m, portfolio.EquityHistory.Last().MarkedEquity);
        }

        [Fact]
        public void MarkedEquity_CrossCurrencyInstrument_AtBreakeven_EqualsStartingCash()
        {
            // Same scenario, but read via the live MarkedEquity property (no RecordEquitySnapshot for the
            // mark) - MarkPrice falls back to AveragePrice (15,000, unchanged from entry), so the position's
            // converted value exactly offsets the converted cash debit: MarkedEquity == StartingCash.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));

            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 10));

            Assert.Equal(10_000m, portfolio.MarkedEquity);
        }

        [Fact]
        public void BuyingPower_CrossCurrencyInstrument_CommittedMarginConvertedToSameCurrencyAsMarkedEquity()
        {
            // Buy 10 @ 15,000 JPY (rate 150): converted notional = 1,000 USD. CommittedMargin =
            // 0.5 * 1,000 = 500. MarkedEquity at breakeven == StartingCash (10,000) per the test above, so
            // BuyingPower = 10,000 - 500 = 9,500.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));

            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 10));

            Assert.Equal(9_500m, portfolio.BuyingPower);
        }

        [Fact]
        public void InitialMarginForOrder_CrossCurrencyInstrument_ConvertsNotionalBeforeApplyingRate()
        {
            // A 10-unit buy priced at 15,000 JPY with rate 150: converted notional = 1,000 USD, so initial
            // margin at the 0.5 long rate is 500 USD - not 7,500 (0.5 * 15,000) if conversion were skipped.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            OrderRequest request = new() { Symbol = "EUR_JPY", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10, Price = 15_000m };

            decimal margin = portfolio.InitialMarginForOrder(request);

            Assert.Equal(500m, margin);
        }

        [Fact]
        public void RecordEquitySnapshot_CrossCurrencyInstrument_IsolatedEquityBySymbolReflectsConvertedUnrealizedPnL()
        {
            // Buy 10 @ 15,000 JPY (rate 150), mark at 15,300 (still rate 150): native unrealized =
            // (15,300-15,000)*10 = 3,000 JPY -> converted = 20 USD. Isolated equity = StartingCash(10,000)
            // + realized(0) + convertedUnrealized(20) = 10,020.
            Instrument[] instruments = { new() { Symbol = "EUR_JPY", QuoteCurrency = "JPY", ConversionSymbol = "USD_JPY" } };
            Portfolio portfolio = new(10_000m, "USD", instruments);
            portfolio.RecordEquitySnapshot(SliceWithBar("USD_JPY", 150m, T0));
            portfolio.ApplyTrade(Buy("EUR_JPY", 15_000m, 10));

            portfolio.RecordEquitySnapshot(SliceWithTwoBars("EUR_JPY", 15_300m, "USD_JPY", 150m, T0.AddDays(1)));

            Assert.Equal(10_020m, portfolio.EquityHistory.Last().EquityBySymbol["EUR_JPY"]);
        }

        [Fact]
        public void ApplyTrade_Sell_AccumulatesRealizedPnL()
        {
            // Buy 10 @ $100, sell 5 @ $120 → realized gain = (120-100)*5 = $100
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 5));

            Assert.Equal(100m, portfolio.RealizedPnL);
        }

        [Fact]
        public void ApplyTrade_MultipleSells_AccumulatesRealizedPnL()
        {
            Portfolio portfolio = new(20_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));

            portfolio.ApplyTrade(Sell("AAPL", 120m, 3));  // gain = 60
            portfolio.ApplyTrade(Sell("AAPL", 130m, 3));  // gain = 90

            Assert.Equal(150m, portfolio.RealizedPnL);
        }

        [Fact]
        public void RecordEquitySnapshot_AfterSell_SnapshotIncludesRealizedPnL()
        {
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(Buy("AAPL", 100m, 10));
            portfolio.ApplyTrade(Sell("AAPL", 120m, 5));  // Cash = 9000+600=9600, realized=100, remaining 5@100

            portfolio.RecordEquitySnapshot(SliceWithBar("AAPL", 120m, T0));

            EquitySnapshot snap = portfolio.EquityHistory[0];
            Assert.Equal(100m, snap.RealizedPnL);
        }
    }
}
