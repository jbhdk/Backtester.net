using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Sizing;
using Backtester.ExecutionModels.Slippage;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Broker.Tests
{
    public class BrokerSimulatorTests
    {
        private static readonly DateTime T0 = new(2024, 1, 2, 9, 30, 0, DateTimeKind.Utc);

        private static MarketSlice SliceWithBar(string symbol, decimal close)
        {
            return new()
            {
                Timestamp = T0,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbol] = new Candle { Timestamp = T0, Open = close, High = close, Low = close, Close = close, Volume = 1000 }
                }
            };
        }

        private static OrderRequest MarketBuy(string symbol, int qty)
        {
            return new()
            {
                Symbol = symbol,
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Quantity = qty
            };
        }


        [Fact]
        public void ProcessBar_WithNoOrders_ReturnsEmpty()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            IEnumerable<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 150m));

            Assert.Empty(trades);
        }

        [Fact]
        public void SubmitOrder_DoesNotThrow()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            string id = broker.SubmitOrder(MarketBuy("AAPL", 10));

            Assert.NotNull(id);
        }

        [Fact]
        public void SubmitOrder_WithNegativeSize_RejectsAndReturnsNull()
        {
            // A sizing model that returns a negative share count must never reach the fill/position pipeline;
            // the submission gate rejects it exactly as it does a zero size (returns null, applies nothing).
            Portfolio portfolio = new(10_000m);
            ISizingModel sizing = A.Fake<ISizingModel>();
            A.CallTo(() => sizing.Size(A<OrderRequest>._, A<Portfolio>._)).Returns(-5);
            BrokerSimulator broker = new(portfolio, sizingModel: sizing);

            string id = broker.SubmitOrder(MarketBuy("AAPL", 10));

            Assert.Null(id);
            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();
            Assert.Empty(trades);
            Assert.Empty(portfolio.Positions);
        }

        [Fact]
        public void ProcessBar_MarketBuy_FillsAtClose_ReturnsTrade()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
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
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            Assert.Equal(9_000m, portfolio.Cash);
            Assert.Single(portfolio.Positions);
        }

        [Fact]
        public void ProcessBar_DrainsPendingOrders_DoesNotRefillNextBar()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));
            broker.SubmitOrder(MarketBuy("AAPL", 10));
            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            List<Trade> secondBarTrades = broker.ProcessBar(SliceWithBar("AAPL", 105m)).ToList();

            Assert.Empty(secondBarTrades);
        }

        [Fact]
        public void ProcessBar_MultipleOrders_FillsAll()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 5));
            broker.SubmitOrder(MarketBuy("AAPL", 3));

            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Equal(2, trades.Count);
        }

        [Fact]
        public void ProcessBar_WithCommissionAndSlippage_TradeCarriesNonZeroValues()
        {
            // Market buy at Open=100; 1% slippage → fill at 101; 0.5% commission on notional 101×10 = $5.05
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(
                portfolio,
                commissionModel: new PercentCommission { Percent = 0.005m },
                slippageModel: new PercentSlippage { Percent = 0.01m });

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 });
            MarketSlice slice = new()

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
            Portfolio portfolio = new(10_000m);
            PercentNotionalSizing sizing = new() { Percent = 0.10m };
            BrokerSimulator broker = new(portfolio, sizingModel: sizing);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Price = 100m, Quantity = 1 });
            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 100m)).ToList();

            Assert.Single(trades);
            Assert.Equal(10, trades[0].Quantity);
        }

        // --- Reg-T initial-margin gate ---

        [Fact]
        public void SubmitOrder_LongExceedsBuyingPower_ReturnsNull()
        {
            // Flat $10,000; Buy 500 @ 50 → notional 25,000, long margin 12,500 > 10,000 buying power
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 500 });

            Assert.Null(id);
        }

        [Fact]
        public void SubmitOrder_LongWithinBuyingPower_Accepted()
        {
            // Buy 300 @ 50 → notional 15,000, long margin 7,500 ≤ 10,000 buying power (2:1)
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 300 });

            Assert.NotNull(id);
        }

        [Fact]
        public void SubmitOrder_ShortExceedsBuyingPower_ReturnsNull()
        {
            // Sell 200 @ 50 → notional 10,000, short margin 15,000 > 10,000 buying power
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Limit, Price = 50m, Quantity = 200 });

            Assert.Null(id);
        }

        [Fact]
        public void SubmitOrder_ShortWithinBuyingPower_Accepted()
        {
            // Sell 100 @ 50 → notional 5,000, short margin 7,500 ≤ 10,000 buying power
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Limit, Price = 50m, Quantity = 100 });

            Assert.NotNull(id);
        }

        [Fact]
        public void SubmitOrder_ReducingOrder_AcceptedRegardlessOfBuyingPower()
        {
            // Long 100 @ 50 committed; a closing Sell opposes the position → commits no margin → always accepted
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "1", Symbol = "AAPL", Side = OrderSide.Buy, Price = 50m, Quantity = 100, Timestamp = T0 });
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Limit, Price = 50m, Quantity = 100 });

            Assert.NotNull(id);
        }

        [Fact]
        public void SubmitOrder_ConfigurableLongRate_TightensTheGate()
        {
            // At a 1.0 long rate, Buy 300 @ 50 → margin 15,000 > 10,000 — rejected where the 0.5 default accepts
            Portfolio portfolio = new(10_000m) { LongInitialMarginRate = 1.0m };
            BrokerSimulator broker = new(portfolio);

            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 300 });

            Assert.Null(id);
        }

        [Fact]
        public void SubmitOrder_RejectedByMarginGate_CapturedWithFullDetail()
        {
            // Process a bar first so the rejection is stamped with the bar's timestamp.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.ProcessBar(SliceWithBar("AAPL", 50m));

            // Buy 500 @ 50 → long margin 12,500 > 10,000 buying power → rejected.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 500 });

            RejectedOrder rejected = Assert.Single(broker.RejectedOrders);
            Assert.Equal("AAPL", rejected.Symbol);
            Assert.Equal(OrderSide.Buy, rejected.Side);
            Assert.Equal(500, rejected.Quantity);
            Assert.Equal(50m, rejected.Price);
            Assert.Equal(T0, rejected.Timestamp);
            Assert.Equal("Not enough funds", rejected.Reason);
        }

        [Fact]
        public void SubmitOrder_AcceptedOrder_RecordsNoRejection()
        {
            // Buy 300 @ 50 → long margin 7,500 ≤ 10,000 buying power → accepted, nothing rejected.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 300 });

            Assert.Empty(broker.RejectedOrders);
        }

        [Fact]
        public void SubmitOrder_ReducingOrder_RecordsNoRejection()
        {
            // A closing Sell opposes the open long → commits no margin → accepted, nothing rejected.
            Portfolio portfolio = new(10_000m);
            portfolio.ApplyTrade(new Trade { Id = "1", Symbol = "AAPL", Side = OrderSide.Buy, Price = 50m, Quantity = 100, Timestamp = T0 });
            BrokerSimulator broker = new(portfolio);

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Limit, Price = 50m, Quantity = 100 });

            Assert.Empty(broker.RejectedOrders);
        }

        [Fact]
        public void SubmitOrder_AfterProcessBar_SubmittedAtReflectsBarTimestamp()
        {
            DateTime barTime = new(2020, 6, 1, 9, 30, 0, DateTimeKind.Utc);
            MarketSlice slice = new()

            {
                Timestamp = barTime,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = barTime, Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1 }
                }
            };

            CapturingFillModel capture = new();
            BrokerSimulator broker = new(new Portfolio(10_000m), fillModel: capture);

            broker.ProcessBar(slice);
            broker.SubmitOrder(MarketBuy("AAPL", 1));
            broker.ProcessBar(slice);

            Assert.Single(capture.CapturedOrders);
            Assert.Equal(barTime, capture.CapturedOrders[0].SubmittedAt);
        }

        [Fact]
        public void BrokerSimulator_DefaultModel_MarketOrder_FillsAtOpen_NotClose()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 1));

            MarketSlice slice = new()

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

        private static MarketSlice SliceAt(string symbol, decimal open, decimal high, decimal low, decimal close, DateTime ts)
        {
            return new()
            {
                Timestamp = ts,
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    [symbol] = new Candle { Timestamp = ts, Open = open, High = high, Low = low, Close = close, Volume = 1000 }
                }
            };
        }


        [Fact]
        public void ProcessBar_StopNotTriggeredOnBar1_PersistsAndFillsOnBar2()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            // Bar 1: High=105, stop at 110 → no trigger
            List<Trade> bar1Trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();
            // Bar 2: High=115, stop at 110 → fills
            List<Trade> bar2Trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0.AddHours(1))).ToList();

            Assert.Empty(bar1Trades);
            Assert.Single(bar2Trades);
        }

        [Fact]
        public void ProcessBar_ForwardFilledStaleBar_DoesNotFill()
        {
            // A slice timestamped T1 but carrying AAPL's earlier T0 bar — forward-filled because AAPL has
            // no bar at T1 (another symbol drove the timestamp). A queued order must not fill against it,
            // which would stamp the trade at T1, a slot with no real AAPL bar (issue #56).
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            MarketSlice stale = new()
            {
                Timestamp = T0.AddHours(1),
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 105m, Low = 99m, Close = 103m, Volume = 1000 }
                }
            };

            List<Trade> trades = broker.ProcessBar(stale).ToList();

            Assert.Empty(trades);
        }

        [Fact]
        public void ProcessBar_OrderRestsThroughStaleBar_FillsAtNextFreshBarTimestamp()
        {
            // The order rests across a forward-filled stale slice and fills only at the symbol's next real
            // bar, taking that bar's open and timestamp (no phantom post-close fill).
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            MarketSlice stale = new()
            {
                Timestamp = T0.AddHours(1),
                BarsBySymbol = new Dictionary<string, Candle>
                {
                    ["AAPL"] = new Candle { Timestamp = T0, Open = 100m, High = 105m, Low = 99m, Close = 103m, Volume = 1000 }
                }
            };
            broker.ProcessBar(stale);

            DateTime freshTime = T0.AddHours(2);
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 110m, 112m, 108m, 111m, freshTime)).ToList();

            Trade trade = Assert.Single(trades);
            Assert.Equal(110m, trade.Price);
            Assert.Equal(freshTime, trade.Timestamp);
        }

        [Fact]
        public void Cancel_WorkingOrder_NeverFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));
            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            broker.Cancel(id);

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0)).ToList();
            Assert.Empty(trades);
        }

        [Fact]
        public void Modify_WorkingOrder_SubsequentFillUsesNewPrice()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
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

        // --- Bracket + OCO ---

        [Fact]
        public void SubmitBracket_EntryFills_StopSubsequentlyFills()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: Market entry fills at Open=100
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=85, stop at 90 triggers
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 95m, 98m, 85m, 88m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(OrderSide.Sell, trades[0].Side);
            Assert.Equal(90m, trades[0].Price);
        }

        /// <summary>
        /// The handle reports the bracket's own lifecycle, so a target-only bracket — which arms no stop leg
        /// and therefore never gets a stop order id — still says its entry filled and its leg rests.
        /// </summary>
        [Fact]
        public void SubmitBracket_TargetOnlyEntryFills_HandleReportsArmedWithNoStopOrderId()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Market entry fills at Open=100, arming the lone target leg.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.Equal(BracketState.Armed, handle.State);
            Assert.Null(handle.StopOrderId);
        }

        [Fact]
        public void SubmitBracket_ShortEntry_ArmsBuyProtectiveLegs_StopFillsAsBuyCover()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(110m),   // stop-loss ABOVE entry for a short
                targetLeg: BracketLegSpec.AtPrice(90m))); // take-profit BELOW entry

            // Bar 1: market short entry fills at Open=100
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: High=115 → stop at 110 triggers as a Buy that covers the short
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 108m, 115m, 107m, 112m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(OrderSide.Buy, trades[0].Side);
            Assert.Equal(110m, trades[0].Price);
        }

        [Fact]
        public void SubmitBracket_BarSpansBothLegs_ExactlyOneFillsAndSiblingCancelled()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: entry fills
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=80 (stop at 90 triggers), High=130 (target at 120 triggers) — spans both legs
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
        }

        [Fact]
        public void SubmitBracket_ModifyStop_TrailingStopFillsAtNewPrice()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: entry fills, stop (90) and target (120) armed
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Trail stop up to 95 (old price 90 would not trigger on Low=92, new price 95 does)
            broker.Modify(handle.StopOrderId, 95m);

            // Bar 2: Low=92 → stop@90 would not trigger; stop@95 does
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 96m, 98m, 92m, 94m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(OrderSide.Sell, trades[0].Side);
            Assert.Equal(95m, trades[0].Price);
        }

        [Fact]
        public void SubmitBracket_StopFills_StampsTradeWithStopLossLeg()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=85, stop at 90 triggers
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 95m, 98m, 85m, 88m, T0.AddHours(1))).ToList();

            Assert.Equal(BracketLeg.StopLoss, trades[0].Leg);
        }

        [Fact]
        public void SubmitBracket_TargetFills_StampsTradeWithTakeProfitLeg()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: High=125, target limit at 120 triggers
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 118m, 125m, 117m, 122m, T0.AddHours(1))).ToList();

            Assert.Equal(BracketLeg.TakeProfit, trades[0].Leg);
        }

        [Fact]
        public void SubmitBracket_EntryFills_StampsEntryTradeWithEntryStopPrice()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: the market entry fills at Open=100; its own bracket stop sits at 90.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(90m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_EntryAlsoCarriesSizingStop_BracketStopWins()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            // The entry declares a sizing stop at 85 but also arms a bracket whose stop sits at 90. The
            // armed bracket stop is the real protective level and must win over the sizing-stop fallback.
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10, StopPrice = 85m },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(90m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitOrder_PlainEntryFills_LeavesEntryStopPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitOrder(MarketBuy("AAPL", 10));

            // A plain market entry with no attached bracket declares no protective stop.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitOrder_EntryCarriesSizingStop_StampsEntryTradeWithEntryStopPrice()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            // A risk-sized entry declares the stop it sized against on the request, but arms no bracket.
            OrderRequest entry = MarketBuy("AAPL", 10);
            entry.StopPrice = 90m;
            broker.SubmitOrder(entry);

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(90m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_EntryFills_StampsEntryTradeWithEntryTargetPrice()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: the market entry fills at Open=100; its own bracket target sits at 120.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(120m, trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitBracket_StopOnly_EntryLeavesEntryTargetPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));

            // No target leg armed, so the entry declares no initial target.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_StampsEntryTradeWithEntryTargetPrice()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // The lone target leg defines the initial target even though there is no stop.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(120m, trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitOrder_PlainEntryFills_LeavesEntryTargetPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitOrder(MarketBuy("AAPL", 10));

            // A plain market entry arms no bracket, so it declares no target.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitOrder_EntryCarriesSizingStop_LeavesEntryTargetPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            // A risk-sized entry declares a sizing stop but arms no bracket — there is no sizing target.
            OrderRequest entry = MarketBuy("AAPL", 10);
            entry.StopPrice = 90m;
            broker.SubmitOrder(entry);

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SizingStopEntry_SignalExit_RoundTripCarriesInitialRisk()
        {
            // End-to-end: a risk-sized entry (stop 90, no bracket) opens 10 @ 100 → per-share distance 10.
            // A later signal exit flattens it, and the emitted round trip carries initial risk 10 * 10 = 100
            // — R is now defined where a signal-exit strategy previously showed a dash.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            OrderRequest entry = MarketBuy("AAPL", 10);
            entry.StopPrice = 90m;
            broker.SubmitOrder(entry);
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 120m, 125m, 119m, 122m, T0.AddHours(1)));

            Assert.Equal(100m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void SubmitBracket_FlattenedBySignalOrder_RestingLegsNeverFill()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: entry fills at Open=100; stop (90) and target (120) arm.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // The strategy flattens with its own market sell — a Signal exit, not a bracket leg.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });

            // Bar 2: the sell flattens the position (Low=98/High=104 trigger neither leg).
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(1)));

            // Bar 3: spans both former legs (Low=80 < stop 90, High=130 > target 120) — nothing must fill.
            List<Trade> bar3Trades = broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(2))).ToList();

            Assert.Empty(bar3Trades);
        }

        [Fact]
        public void SubmitBracket_FlattenedBySignalOrder_ProducesNoPhantomRoundTrip()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(1)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(2)));

            // Only the Signal exit closed the position; the cancelled legs add no second round trip.
            RoundTrip roundTrip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(ExitReason.Signal, roundTrip.ExitReason);
        }

        [Fact]
        public void SubmitBracket_SecondBracketOnSameSymbol_FlattenedBySignalOrder_NoLegRests()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));
            // Bar 1: the first bracket's entry fills at Open=100 and its legs (90/120) arm.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // A second bracket scales into the same symbol; its legs arm alongside the first bracket's.
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(92m),
                targetLeg: BracketLegSpec.AtPrice(118m)));
            // Bar 2: the second entry fills at Open=100; Low=99/High=105 trigger no leg of either bracket.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0.AddHours(1)));

            // A Signal exit flattens all 20 shares, so every resting leg of both brackets must be cancelled.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 20 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(2)));

            // Bar 4 spans every former leg (Low=80 < both stops, High=130 > both targets) — nothing must fill.
            List<Trade> laterTrades = broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(3))).ToList();

            Assert.Empty(laterTrades);
        }

        [Fact]
        public void SubmitBracket_SecondBracketOnSameSymbol_FlattenedBySignalOrder_OpensNoPhantomPosition()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(92m),
                targetLeg: BracketLegSpec.AtPrice(118m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0.AddHours(1)));
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 20 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(2)));

            broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(3)));

            // An orphaned leg filling from flat would open a position the strategy never asked for.
            Assert.Equal(0, Assert.Single(portfolio.Positions).Quantity);
        }

        [Fact]
        public void SubmitBracket_SecondBracketOnSameSymbol_FlattenedByOtherBracketsLeg_NoLegRests()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));
            // Bar 1: the first bracket's entry fills at Open=100 and its legs (90/120) arm.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // A second bracket scales in, with wider legs so one bar can trigger the first bracket's stop alone.
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(85m),
                targetLeg: BracketLegSpec.AtPrice(130m)));
            // Bar 2: the second entry fills at Open=100; the position is 20 shares under two brackets.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0.AddHours(1)));

            // A plain partial exit halves the position to 10 shares. Both brackets still rest against it.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 101m, T0.AddHours(2)));

            // Bar 4: Low=88 fills the first bracket's stop (90) but not the second's (85). Those 10 shares are
            // the whole remaining position, so this leg fill flattens the symbol — and the second bracket's
            // legs are now what rests against flat.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 88m, 95m, T0.AddHours(3)));

            // Bar 5 spans the second bracket's legs (Low=80 < 85, High=140 > 130) — nothing must fill.
            List<Trade> laterTrades = broker.ProcessBar(SliceAt("AAPL", 100m, 140m, 80m, 110m, T0.AddHours(4))).ToList();

            Assert.Empty(laterTrades);
        }

        [Fact]
        public void ProcessBar_PlainMarketOrderFill_LeavesLegNone()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            broker.SubmitOrder(MarketBuy("AAPL", 10));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(BracketLeg.None, trades[0].Leg);
        }

        // --- Single-leg brackets ---

        [Fact]
        public void SubmitBracket_StopOnly_HandlePopulatesStopButNotTarget()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));

            // Entry fills at Open=100; only the stop leg arms.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.NotNull(handle.StopOrderId);
            Assert.Null(handle.TargetOrderId);
        }

        [Fact]
        public void SubmitBracket_StopOnly_StopFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=85, stop at 90 triggers.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 95m, 98m, 85m, 88m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(BracketLeg.StopLoss, trades[0].Leg);
            Assert.Equal(90m, trades[0].Price);
        }

        [Fact]
        public void SubmitBracket_StopOnly_HighSpike_NoPhantomTargetFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: a large upward spike — a stop-only bracket has no take-profit leg to fill against it.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 110m, 200m, 108m, 190m, T0.AddHours(1))).ToList();

            Assert.Empty(trades);
        }

        [Fact]
        public void SubmitBracket_StopOnly_EntryStampsEntryStopPrice()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));

            // The armed stop defines initial risk exactly as a two-legged bracket does.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(90m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_StopOnly_RecordsOnlyStopLevel()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.Single(broker.BracketLevelChanges);
            Assert.Equal(BracketLeg.StopLoss, broker.BracketLevelChanges[0].Leg);
        }

        [Fact]
        public void SubmitBracket_StopOnly_FlattenedBySignal_RestingStopNeverFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m)));

            // Bar 1: entry fills at Open=100; the lone stop (90) arms.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // A Signal exit flattens the position; the resting stop must be cancelled.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(1)));

            // Bar 3: Low=80 would have triggered the former stop at 90 — nothing must fill from flat.
            List<Trade> bar3Trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 80m, 95m, T0.AddHours(2))).ToList();

            Assert.Empty(bar3Trades);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_HandlePopulatesTargetButNotStop()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.NotNull(handle.TargetOrderId);
            Assert.Null(handle.StopOrderId);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_TargetFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: High=125, target limit at 120 triggers.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 118m, 125m, 117m, 122m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(BracketLeg.TakeProfit, trades[0].Leg);
            Assert.Equal(120m, trades[0].Price);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_EntryLeavesEntryStopPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // No armed stop leg, so the round trip has no initial risk and no R.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_EntryCarriesSizingStop_StillLeavesEntryStopPriceNull()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            // The entry declares a sizing stop, but the bracket arms no protective stop leg. Per the
            // ADR 0023 amendment, a bracketed entry's initial risk comes from the armed stop only — the
            // sizing-stop fallback is for a non-bracketed entry — so this target-only bracket has no R.
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10, StopPrice = 85m },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Null(trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_RecordsOnlyTargetLevel()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.Single(broker.BracketLevelChanges);
            Assert.Equal(BracketLeg.TakeProfit, broker.BracketLevelChanges[0].Leg);
        }

        [Fact]
        public void SubmitBracket_TargetOnly_FlattenedBySignal_RestingTargetNeverFills()
        {
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: entry fills at Open=100; the lone target (120) arms.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // A Signal exit flattens the position; the resting target must be cancelled.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(1)));

            // Bar 3: High=130 would have triggered the former target at 120 — nothing must fill from flat.
            List<Trade> bar3Trades = broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 99m, 110m, T0.AddHours(2))).ToList();

            Assert.Empty(bar3Trades);
        }

        // --- Bracket level ledger ---

        [Fact]
        public void SubmitBracket_EntryFills_RecordsInitialStopAndTargetLevels()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Entry fills at Open=100 on this bar; the protective legs arm here, recording their initial levels.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            BracketLevelChange stop = Assert.Single(broker.BracketLevelChanges, change => change.Leg == BracketLeg.StopLoss);
            Assert.Equal("AAPL", stop.Symbol);
            Assert.Equal(90m, stop.Price);
            Assert.Equal(T0, stop.Timestamp);
            Assert.Equal(handle.StopOrderId, stop.OrderId);

            BracketLevelChange target = Assert.Single(broker.BracketLevelChanges, change => change.Leg == BracketLeg.TakeProfit);
            Assert.Equal(120m, target.Price);
            Assert.Equal(handle.TargetOrderId, target.OrderId);
        }

        [Fact]
        public void SubmitBracket_BeforeEntryFills_RecordsNoLevels()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            // A resting limit entry that the bar does not reach, so the entry never fills and no legs arm.
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(40m),
                targetLeg: BracketLegSpec.AtPrice(70m)));

            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.Empty(broker.BracketLevelChanges);
        }

        [Fact]
        public void Modify_TrailedStopLeg_RecordsNewStopLevelChange()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Bar 1: entry fills, stop (90) and target (120) armed at T0.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));
            // Bar 2: a later bar becomes current, then the strategy trails the stop up to 95.
            broker.ProcessBar(SliceAt("AAPL", 103m, 106m, 101m, 104m, T0.AddHours(1)));
            broker.Modify(handle.StopOrderId, 95m);

            // Two stop levels: the initial 90 at T0 and the trailed 95 at the second bar.
            List<BracketLevelChange> stopChanges = broker.BracketLevelChanges
                .Where(change => change.Leg == BracketLeg.StopLoss).ToList();
            Assert.Equal(2, stopChanges.Count);
            BracketLevelChange trailed = stopChanges[1];
            Assert.Equal(95m, trailed.Price);
            Assert.Equal(T0.AddHours(1), trailed.Timestamp);
            Assert.Equal(handle.StopOrderId, trailed.OrderId);
        }

        [Fact]
        public void Modify_NonBracketOrder_RecordsNoLevelChange()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);
            // A plain resting stop order, not part of any bracket — it carries no leg role.
            string id = broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Stop, Price = 110m, Quantity = 1 });

            broker.Modify(id, 120m);

            Assert.Empty(broker.BracketLevelChanges);
        }

        // --- Intra-bar fill priority ---

        [Fact]
        public void ProcessBar_SequencesHigherPriorityOrderFirst_RegardlessOfSubmissionOrder()
        {
            // Submit the low-priority order first, then a higher-priority one; the fill model must still see
            // the higher-priority order first, so its fill is applied to the portfolio ahead of the other's.
            CapturingFillModel capture = new();
            BrokerSimulator broker = new(new Portfolio(100_000m), fillModel: capture);
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 3, Priority = 0 });
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 7, Priority = 5 });

            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            Assert.Equal(7, capture.CapturedOrders[0].Quantity);
            Assert.Equal(3, capture.CapturedOrders[1].Quantity);
        }

        [Fact]
        public void ProcessBar_EqualPriorityOrders_PreserveSubmissionOrder()
        {
            // With no priority set (default 0), the sequence is a stable tie-break: orders are handed to the
            // fill model in submission order, preserving today's behaviour for strategies that never set it.
            CapturingFillModel capture = new();
            BrokerSimulator broker = new(new Portfolio(100_000m), fillModel: capture);
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 3 });
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 7 });

            broker.ProcessBar(SliceWithBar("AAPL", 100m));

            Assert.Equal(3, capture.CapturedOrders[0].Quantity);
            Assert.Equal(7, capture.CapturedOrders[1].Quantity);
        }

        [Fact]
        public void ProcessBar_SingleBarReversal_HigherPriorityFlattenLetsReversedEntryCarryItsStop()
        {
            // A single-bar stop-and-reverse: hold a long, then on one bar flatten it and open the reversed
            // short via a bracket. Both orders oppose the long and fill on the same bar. Giving the flatten
            // the higher priority guarantees it is applied first, so the bracket entry opens the short from
            // flat and freezes its protective stop — instead of the entry becoming the reducing fill and the
            // reversed position opening unprotected.
            Portfolio portfolio = new(100_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitOrder(MarketBuy("AAPL", 10));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10, Priority = 1 });
            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(110m),  // stop-loss ABOVE entry for a short
                targetLeg: BracketLegSpec.AtPrice(90m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0.AddHours(1)));

            Position position = Assert.Single(portfolio.Positions);
            Assert.Equal(-10, position.Quantity);
            Assert.Equal(110m, position.EntryStopPrice);
        }

        // --- Marketable-at-arm same-bar fill (#100) ---

        [Fact]
        public void SubmitBracket_LongEntryGapsBelowWrongSideStop_StopFillsOnArmingBar()
        {
            // A long computes its stop from a pre-fill reference (stop 95, below that reference), but the
            // market entry gaps DOWN to open 90 — below the stop. The stop is now on the wrong side of the
            // fill and is already marketable at the arming bar's open, so a live bracket would trigger it
            // right after the entry. It must fill on this same arming bar, not rest to the next.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(95m),
                targetLeg: BracketLegSpec.AtPrice(130m)));

            // Arming bar opens at 90 (gapped below the 95 stop); the entry fills at 90 and the stop with it.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 90m, 92m, 88m, 91m, T0)).ToList();

            Assert.Contains(trades, trade => trade.Side == OrderSide.Sell && trade.Leg == BracketLeg.StopLoss);
        }

        [Fact]
        public void SubmitBracket_ShortEntryGapsAboveWrongSideStop_ProducesZeroBarScratchRoundTrip()
        {
            // The short-SPY regression (issue #99). A short's stop belongs ABOVE the fill, but a gap-up
            // entry fills above the absolute stop, leaving it on the wrong side. Under the old next-bar
            // delay the stop rested a full bar and the gap ran on, manufacturing a ~-28R blowup. Filling
            // the already-marketable stop on the arming bar covers at ~entry: a zero-bar, ~0-loss scratch.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(103m),  // above the pre-fill reference, but the entry gaps up past it
                targetLeg: BracketLegSpec.AtPrice(90m)));

            // Arming bar opens at 105, above the 103 stop; the short fills at 105 and covers at 105.
            broker.ProcessBar(SliceAt("AAPL", 105m, 107m, 104m, 106m, T0));

            RoundTrip roundTrip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(PositionDirection.Short, roundTrip.Direction);
            Assert.Equal(0, roundTrip.BarsHeld);
            Assert.Equal(0m, roundTrip.RealizedPnL);
        }

        [Fact]
        public void SubmitBracket_LongEntryGapsAboveTarget_TargetFillsOnArmingBar()
        {
            // Symmetric to the wrong-side stop: a favourable gap. The long's entry gaps UP to open 125,
            // above its own 120 take-profit, so the target is already marketable at the arming bar's open.
            // A live bracket takes that profit right after the entry; it must fill on this same arming bar.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            // Arming bar opens at 125 (gapped above the 120 target); the entry fills at 125 and the target with it.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 125m, 127m, 123m, 126m, T0)).ToList();

            Assert.Contains(trades, trade => trade.Side == OrderSide.Sell && trade.Leg == BracketLeg.TakeProfit);
        }

        [Fact]
        public void SubmitBracket_ArmingBarTradesThroughStopButOpensAbove_StopKeepsNextBarTiming()
        {
            // The arming bar opens at 100 — between the 90 stop and 130 target, so the stop is NOT
            // marketable at the open. The bar later trades down through the stop (Low=85), but a leg the
            // bar merely reaches later keeps ordinary next-bar timing: only a leg already through the market
            // at the open fills same-bar. So the arming bar produces the entry alone.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(130m)));

            // Open=100 (between the legs), Low=85 (trades through the 90 stop after the open).
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 85m, 95m, T0)).ToList();

            Trade only = Assert.Single(trades);
            Assert.Equal(BracketLeg.None, only.Leg);
        }

        [Fact]
        public void SubmitBracket_SameBarStopFill_CancelsOcoSibling_LaterBarSpanningTargetFillsNothing()
        {
            // The stop fills on the arming bar, so its OCO sibling (the target) must be cancelled just as a
            // next-bar stop fill would cancel it. Otherwise the resting target would fill as a Sell from
            // flat on a later bar and open a phantom short.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(95m),
                targetLeg: BracketLegSpec.AtPrice(130m)));

            // Arming bar opens at 90 (below the 95 stop): entry and stop both fill here, cancelling the target.
            broker.ProcessBar(SliceAt("AAPL", 90m, 92m, 88m, 91m, T0));

            // Later bar spikes above the former 130 target — the cancelled sibling must not fill.
            List<Trade> laterTrades = broker.ProcessBar(SliceAt("AAPL", 128m, 140m, 127m, 138m, T0.AddHours(1))).ToList();

            Assert.Empty(laterTrades);
        }

        [Fact]
        public void SubmitBracket_SameBarStopFill_RecordsStopLevelChange()
        {
            // The same-bar fill runs back through the ordinary fill path, so the armed stop's level is still
            // recorded in the ledger even though it fills on the arming bar.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(95m),
                targetLeg: BracketLegSpec.AtPrice(130m)));

            broker.ProcessBar(SliceAt("AAPL", 90m, 92m, 88m, 91m, T0));

            Assert.Contains(broker.BracketLevelChanges, change => change.Leg == BracketLeg.StopLoss && change.Price == 95m);
        }

        [Fact]
        public void SubmitBracket_SameBarStopFill_RoundTripExitReasonIsStopLoss()
        {
            // A same-bar stop-out is still a stop-loss exit — the round trip's reason comes from the leg
            // that closed it, exactly as a next-bar stop fill would.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(95m),
                targetLeg: BracketLegSpec.AtPrice(130m)));

            broker.ProcessBar(SliceAt("AAPL", 90m, 92m, 88m, 91m, T0));

            Assert.Equal(ExitReason.StopLoss, Assert.Single(portfolio.RoundTrips).ExitReason);
        }

        [Fact]
        public void SubmitBracket_CorrectlySidedBracket_ArmingBarFillsOnlyEntry_LegsRest()
        {
            // The happy path: the entry opens between its legs (stop 90 below, target 120 above), so neither
            // is marketable at the open. The arming bar fills only the entry and the position stays open,
            // exactly as before — the same-bar rule adds nothing for a correctly-sided bracket.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.AtPrice(120m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Trade only = Assert.Single(trades);
            Assert.Equal(BracketLeg.None, only.Leg);
            Assert.Equal(10, Assert.Single(portfolio.Positions).Quantity);
        }

        [Fact]
        public void SubmitBracket_StopOnly_MarketableAtArm_FillsSameBarWithNoSibling()
        {
            // A single-leg (stop-only) bracket whose lone stop is marketable at the arming bar's open fills
            // on that same bar with no OCO sibling to cancel — a zero-bar stop-loss round trip.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(95m)));

            // Arming bar opens at 90 (below the 95 stop): entry and lone stop both fill here.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 90m, 92m, 88m, 91m, T0)).ToList();

            Assert.Contains(trades, trade => trade.Side == OrderSide.Sell && trade.Leg == BracketLeg.StopLoss);
            Assert.Equal(ExitReason.StopLoss, Assert.Single(portfolio.RoundTrips).ExitReason);
        }

        // --- Fill-relative offset legs (#101) ---

        [Fact]
        public void SubmitBracket_LongStopOffset_ResolvesStopBelowFill()
        {
            // A long submits its stop as a fill-relative offset of 5. The engine resolves it at fill time
            // against the actual fill (open 100), placing the stop 5 below the fill on the protective side.
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(95m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_ShortStopOffset_ResolvesStopAboveFill()
        {
            // A short's protective stop belongs ABOVE the fill; the offset is added to the fill.
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(105m, trades[0].EntryStopPrice);
        }

        [Fact]
        public void SubmitBracket_LongTargetOffset_ResolvesTargetAboveFill()
        {
            // A long's take-profit belongs ABOVE the fill; the target offset is added to the fill.
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                targetLeg: BracketLegSpec.OffsetFromFill(10m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(110m, trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitBracket_StopOffset_RoundTripInitialRiskEqualsOffsetTimesQuantity()
        {
            // The guarantee: resolving the stop against the real fill makes realized initial risk equal the
            // requested offset exactly. Offset 5 on 10 shares → initial risk 50, the R denominator (ADR 0023).
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            // Entry fills at 100; offset stop resolves to 95 (below), so it rests rather than firing.
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // A signal exit flattens the position and realizes the round trip.
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 110m, 112m, 108m, 111m, T0.AddHours(1)));

            Assert.Equal(50m, Assert.Single(portfolio.RoundTrips).InitialRisk);
        }

        [Fact]
        public void SubmitBracket_LongStopOffset_ArmedLegRestsAtResolvedPrice()
        {
            // Resolution feeds the armed leg, not just the entry stamp: the stop rests at the resolved 95
            // and fills there on a later bar that reaches it.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=94 reaches the resolved 95 stop.
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 98m, 99m, 94m, 96m, T0.AddHours(1))).ToList();

            Trade stopFill = Assert.Single(trades);
            Assert.Equal(OrderSide.Sell, stopFill.Side);
            Assert.Equal(BracketLeg.StopLoss, stopFill.Leg);
            Assert.Equal(95m, stopFill.Price);
        }

        [Fact]
        public void SubmitBracket_AbsoluteStopAndTargetOffset_BothFormsCoexist()
        {
            // A bracket may mix forms: an absolute stop and a fill-relative target. Each resolves by its
            // own rule — the absolute stop to itself (90), the target offset to fill + 10 (110).
            BrokerSimulator broker = new(new Portfolio(10_000m));

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.AtPrice(90m),
                targetLeg: BracketLegSpec.OffsetFromFill(10m)));

            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(90m, trades[0].EntryStopPrice);
            Assert.Equal(110m, trades[0].EntryTargetPrice);
        }

        [Fact]
        public void SubmitBracket_StopOffsetOnly_ArmsStopLegNoTarget()
        {
            // A single-leg bracket given only as an offset is valid (the zero-leg check considers both
            // forms) and arms just the stop leg.
            BrokerSimulator broker = new(new Portfolio(10_000m));

            BracketHandle handle = broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.NotNull(handle.StopOrderId);
            Assert.Null(handle.TargetOrderId);
        }

        [Fact]
        public void SubmitBracket_RiskSizedOffsetEntry_SizesFromOffset()
        {
            // Regression for the sizing gap: a fill-relative bracket entry carries no absolute stop, but a
            // risk-sizing model must still size it. The broker surfaces the bracket's offset to the sizer, so a
            // 1%-of-$10,000 = $100 budget over a $5 offset fills 20 shares instead of a rejected zero-size entry.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new RiskPerTradeSizing { RiskFraction = 0.01m });

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0)).ToList();

            Assert.Equal(20, trades[0].Quantity);
        }

        [Fact]
        public void SubmitOrder_QuantitylessReducingOrder_FlattensFullPositionThoughRiskSizerWouldSizeZero()
        {
            // A close is not risk-sized: the flatten market order carries no stop, so RiskPerTradeSizing alone
            // would size it to zero and drop it, stranding the position. The broker instead flattens the whole
            // open position — the 20-share long the offset bracket opened — so the reducing fill closes exactly.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new RiskPerTradeSizing { RiskFraction = 0.01m });

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market });
            List<Trade> exitTrades = broker.ProcessBar(SliceAt("AAPL", 103m, 104m, 101m, 102m, T0.AddHours(1))).ToList();

            Assert.Equal(20, Assert.Single(exitTrades).Quantity);
        }

        [Fact]
        public void SubmitOrder_ExplicitReducingQuantity_PerformsPartialReduce_NotResizedBySizingModel()
        {
            // An explicit reducing quantity is a deliberate partial exit and must be respected, not overwritten
            // by the sizing model. A FixedSizeModel of 100 would otherwise resize the scale-out to a full close;
            // the broker keeps the requested 30, leaving 70 of the 100-share long open.
            Portfolio portfolio = new(100_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new FixedSizeModel { FixedSize = 100 });

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market });
            broker.ProcessBar(SliceAt("AAPL", 50m, 51m, 49m, 50m, T0));

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 30 });
            broker.ProcessBar(SliceAt("AAPL", 50m, 51m, 49m, 50m, T0.AddHours(1)));

            Assert.Equal(70, portfolio.OpenQuantity("AAPL"));
        }

        [Fact]
        public void SubmitOrder_WithSizingModel_LeavesCallerRequestedQuantityUnchanged()
        {
            // The broker sizes onto its own copy. A strategy that holds a request and reuses it across bars
            // must not find the sized quantity written back into it: the caller asked for 10 and still has 10,
            // even though the fixed-size model sized the submission to 100.
            BrokerSimulator broker = new(new Portfolio(100_000m), sizingModel: new FixedSizeModel { FixedSize = 100 });
            OrderRequest request = MarketBuy("AAPL", 10);

            broker.SubmitOrder(request);

            Assert.Equal(10, request.Quantity);
        }

        [Fact]
        public void SubmitOrder_WithSizingModel_WorkingOrderStillCarriesSizedQuantity()
        {
            // The other half of the copy: the submission the broker works is the sized one, so the fill is 100
            // even though the caller's object still reads 10.
            BrokerSimulator broker = new(new Portfolio(100_000m), sizingModel: new FixedSizeModel { FixedSize = 100 });

            broker.SubmitOrder(MarketBuy("AAPL", 10));
            List<Trade> trades = broker.ProcessBar(SliceWithBar("AAPL", 50m)).ToList();

            Assert.Equal(100, Assert.Single(trades).Quantity);
        }

        [Fact]
        public void SubmitBracket_OffsetStop_LeavesCallerEntryWithoutSizingOffset()
        {
            // The bracket's stop offset reaches the sizer through the entry (ADR 0025 amendment), but it is the
            // broker's annotation, not the caller's. The strategy's entry request comes back as it went in.
            BrokerSimulator broker = new(new Portfolio(10_000m));
            OrderRequest entry = new() { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 };

            broker.SubmitBracket(new BracketRequest(entry, stopLeg: BracketLegSpec.OffsetFromFill(5m)));

            Assert.Null(entry.StopOffset);
        }

        [Fact]
        public void SubmitOrder_QuantitylessReducingOrder_LeavesCallerRequestedQuantityUnchanged()
        {
            // A quantity-less close is filled out to the whole open position on the broker's copy. The caller's
            // request keeps the zero it declared, so the same object can flatten again on a later bar.
            Portfolio portfolio = new(100_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new FixedSizeModel { FixedSize = 100 });
            broker.SubmitOrder(MarketBuy("AAPL", 100));
            broker.ProcessBar(SliceAt("AAPL", 50m, 51m, 49m, 50m, T0));

            OrderRequest flatten = new() { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market };
            broker.SubmitOrder(flatten);
            List<Trade> exitTrades = broker.ProcessBar(SliceAt("AAPL", 50m, 51m, 49m, 50m, T0.AddHours(1))).ToList();

            Assert.Equal(0, flatten.Quantity);
            Assert.Equal(100, Assert.Single(exitTrades).Quantity);
        }

        [Fact]
        public void SubmitBracket_RiskSizedOffsetEntry_RoundTripInitialRiskEqualsRiskBudget()
        {
            // End to end through the copy: the offset sizes the entry to 20 shares (1% of $10,000 over a $5
            // offset), and the stop resolved against the fill freezes initial risk at 20 × $5 = $100 — the risk
            // budget the sizer was given. Sizing and the armed stop stay wired together across the copy.
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio, sizingModel: new RiskPerTradeSizing { RiskFraction = 0.01m });

            broker.SubmitBracket(new BracketRequest(
                new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market },
                stopLeg: BracketLegSpec.OffsetFromFill(5m)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market });
            broker.ProcessBar(SliceAt("AAPL", 110m, 112m, 108m, 111m, T0.AddHours(1)));

            Assert.Equal(100m, Assert.Single(portfolio.RoundTrips).InitialRisk);
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
