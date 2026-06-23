using System;
using System.Collections.Generic;
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
