using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Models.Commission;
using Backtester.Models.Risk;
using Backtester.Models.Sizing;
using Backtester.Models.Slippage;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class BrokerSimulatorTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static MarketSlice SliceWithBar(string symbol, decimal close) => new()
        {
            Timestamp = T0,
            BarsBySymbol = new Dictionary<string, Candle>
            {
                [symbol] = new Candle { Timestamp = T0, Open = close, High = close, Low = close, Close = close, Volume = 1000 }
            }
        };

        private static OrderRequest MarketBuy(string symbol, int qty) => new()
        {
            Symbol = symbol,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = qty
        };

        [Fact]
        public void ProcessBar_WithNoOrders_ReturnsEmpty()
        {
            BrokerSimulator broker = new BrokerSimulator(new Portfolio(10_000m));

            IEnumerable<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 150m));

            Assert.Empty(trades);
        }

        [Fact]
        public void SubmitOrder_DoesNotThrow()
        {
            BrokerSimulator broker = new BrokerSimulator(new Portfolio(10_000m));

            string id = broker.SubmitOrder(MarketBuy("AAPL", 10));

            Assert.NotNull(id);
        }

        [Fact]
        public void ProcessBar_MarketBuy_FillsAtClose_ReturnsTrade()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 102m)).ToList();

            Assert.Single(trades);
            Assert.Equal(102m, trades[0].Price);
            Assert.Equal(10, trades[0].Quantity);
            Assert.Equal("AAPL", trades[0].Symbol);
            Assert.Equal(OrderSide.Buy, trades[0].Side);
        }

        [Fact]
        public void ProcessBar_AppliesTrade_PortfolioUpdated()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            Assert.Equal(9_000m, portfolio.Cash);
            Assert.Single(portfolio.Positions);
        }

        [Fact]
        public void ProcessBar_DrainsPendingOrders_DoesNotRefillNextBar()
        {
            BrokerSimulator broker = new BrokerSimulator(new Portfolio(10_000m));
            broker.SubmitOrder(MarketBuy("AAPL", 10));
            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            List<Trade> secondBarTrades = broker.ProcessBar(SliceWithBar("AAPL", 105m)).ToList();

            Assert.Empty(secondBarTrades);
        }

        [Fact]
        public void ProcessBar_MultipleOrders_FillsAll()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 5));
            broker.SubmitOrder(MarketBuy("AAPL", 3));

            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Equal(2, trades.Count);
        }

        [Fact]
        public void ProcessBar_WithCommissionAndSlippage_TradeCarriesNonZeroValues()
        {
            // Market buy at Open=100; 1% slippage → fill at 101; 0.5% commission on notional 101×10 = $5.05
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(
                portfolio,
                commissionModel: new PercentCommission { Percent = 0.005m },
                slippageModel: new PercentSlippage { Percent = 0.01m });

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
            MarketSlice slice = new MarketSlice
            {
                Timestamp = T0,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 110m, Low = 90m, Close = 105m, Volume = 1000 }
                }
            };

            List<Trade> trades = broker.ProcessBar(slice).ToList();

            // fill: Open=100 + 1% slippage → price=101, slippage=1
            // commission: 0.5% × (101 × 10) = 5.05
            Assert.Single(trades);
            Assert.Equal(101m, trades[0].Price);
            Assert.Equal(1m, trades[0].Slippage);
            Assert.Equal(5.05m, trades[0].Commission);
        }

        [Fact]
        public void SubmitOrder_WithSizingModel_OverridesRequestedQuantity()
        {
            // 10% of $10,000 at $100/share = 10 shares, regardless of the 1 in the request
            Portfolio portfolio = new Portfolio(10_000m);
            PercentNotionalSizing sizing = new PercentNotionalSizing { Percent = 0.10m };
            BrokerSimulator broker = new BrokerSimulator(portfolio, sizingModel: sizing);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Price = 100m, Quantity = 1 });
            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Single(trades);
            Assert.Equal(10, trades[0].Quantity);
        }

        [Fact]
        public void SubmitOrder_RejectedByRiskModel_ProducesNoTrade()
        {
            // portfolio $500, risk model blocks orders costing > cash, buying 10@$100 = $1000 → rejected
            Portfolio portfolio = new Portfolio(500m);
            PortfolioRiskModel risk = new PortfolioRiskModel { MaxPortfolioHeatPercent = 1.0m };
            BrokerSimulator broker = new BrokerSimulator(portfolio, riskModel: risk);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Price = 100m, Quantity = 10 });
            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Empty(trades);
        }

        [Fact]
        public void SubmitOrder_AfterProcessBar_SubmittedAtReflectsBarTimestamp()
        {
            DateTime barTime = new DateTime(2020, 6, 1, 9, 30, 0, DateTimeKind.Utc);
            MarketSlice slice = new MarketSlice
            {
                Timestamp = barTime,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = barTime, Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1 }
                }
            };

            CapturingFillModel capture = new CapturingFillModel();
            BrokerSimulator broker = new BrokerSimulator(new Portfolio(10_000m), fillModel: capture);

            broker.ProcessBar(slice);
            broker.SubmitOrder(MarketBuy("AAPL", 1));
            broker.ProcessBar(slice);

            Assert.Single(capture.CapturedOrders);
            Assert.Equal(barTime, capture.CapturedOrders[0].SubmittedAt);
        }

        [Fact]
        public void BrokerSimulator_DefaultModel_MarketOrder_FillsAtOpen_NotClose()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 1));

            MarketSlice slice = new MarketSlice
            {
                Timestamp = T0,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 115m, Low = 95m, Close = 110m, Volume = 1000 }
                }
            };
            List<Trade> trades = broker.ProcessBar(slice).ToList();

            Assert.Single(trades);
            Assert.Equal(100m, trades[0].Price);
        }

        // --- Resting order book ---

        private static MarketSlice SliceAt(string symbol, decimal open, decimal high, decimal low, decimal close, DateTime ts) => new()
        {
            Timestamp = ts,
            BarsBySymbol = new Dictionary<string, Candle>
            {
                [symbol] = new Candle { Timestamp = ts, Open = open, High = high, Low = low, Close = close, Volume = 1000 }
            }
        };

        [Fact]
        public void ProcessBar_StopNotTriggeredOnBar1_PersistsAndFillsOnBar2()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            // Bar 1: High=105, stop at 110 → no trigger
            List<Trade> bar1Trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();
            // Bar 2: High=115, stop at 110 → fills
            List<Trade> bar2Trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0.AddHours(1))).ToList();

            Assert.Empty(bar1Trades);
            Assert.Single(bar2Trades);
        }

        [Fact]
        public void Cancel_WorkingOrder_NeverFills()
        {
            BrokerSimulator broker = new BrokerSimulator(new Portfolio(10_000m));
            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            broker.Cancel(id);

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0)).ToList();
            Assert.Empty(trades);
        }

        [Fact]
        public void Modify_WorkingOrder_SubsequentFillUsesNewPrice()
        {
            Portfolio portfolio = new Portfolio(10_000m);
            BrokerSimulator broker = new BrokerSimulator(portfolio);
            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            broker.Modify(id, 120m);

            // High=115: would fill old price 110, but not new price 120 → no trade
            List<Trade> bar1Trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0)).ToList();
            // High=125: fills at new price 120
            List<Trade> bar2Trades = broker.ProcessBar(SliceAt("AAPL", 118m, 125m, 117m, 122m, T0.AddHours(1))).ToList();

            Assert.Empty(bar1Trades);
            Assert.Single(bar2Trades);
            Assert.Equal(120m, bar2Trades[0].Price);
        }

        /// <summary>Captures every order passed to DetermineFills for inspection; never produces fills.</summary>
        private class CapturingFillModel : IFillModel
        {
            public List<Order> CapturedOrders { get; } = new();

            public IEnumerable<FillResult> DetermineFills(IEnumerable<Order> orders, Candle bar)
            {
                CapturedOrders.AddRange(orders);
                return Enumerable.Empty<FillResult>();
            }
        }
    }
}
