using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;
using Backtester.ExecutionModels.Commission;
using Backtester.ExecutionModels.Sizing;
using Backtester.ExecutionModels.Slippage;
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

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

            // Bar 1: Market entry fills at Open=100
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: Low=85, stop at 90 triggers
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 95m, 98m, 85m, 88m, T0.AddHours(1))).ToList();

            Assert.Single(trades);
            Assert.Equal(OrderSide.Sell, trades[0].Side);
            Assert.Equal(90m, trades[0].Price);
        }

        [Fact]
        public void SubmitBracket_ShortEntry_ArmsBuyProtectiveLegs_StopFillsAsBuyCover()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 110m,   // stop-loss ABOVE entry for a short
                TargetPrice = 90m   // take-profit BELOW entry
            });

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

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

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

            BracketHandle handle = broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

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

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });
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

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });
            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            // Bar 2: High=125, target limit at 120 triggers
            List<Trade> trades = broker.ProcessBar(SliceAt("AAPL", 118m, 125m, 117m, 122m, T0.AddHours(1))).ToList();

            Assert.Equal(BracketLeg.TakeProfit, trades[0].Leg);
        }

        [Fact]
        public void SubmitBracket_FlattenedBySignalOrder_RestingLegsNeverFill()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

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

            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));
            broker.SubmitOrder(new OrderRequest { Symbol = "AAPL", Side = OrderSide.Sell, Type = OrderType.Market, Quantity = 10 });
            broker.ProcessBar(SliceAt("AAPL", 100m, 104m, 98m, 101m, T0.AddHours(1)));
            broker.ProcessBar(SliceAt("AAPL", 100m, 130m, 80m, 110m, T0.AddHours(2)));

            // Only the Signal exit closed the position; the cancelled legs add no second round trip.
            RoundTrip roundTrip = Assert.Single(portfolio.RoundTrips);
            Assert.Equal(ExitReason.Signal, roundTrip.ExitReason);
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

        // --- Bracket level ledger ---

        [Fact]
        public void SubmitBracket_EntryFills_RecordsInitialStopAndTargetLevels()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BracketHandle handle = broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

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
            broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Limit, Price = 50m, Quantity = 10 },
                StopPrice = 40m,
                TargetPrice = 70m
            });

            broker.ProcessBar(SliceAt("AAPL", 100m, 105m, 99m, 103m, T0));

            Assert.Empty(broker.BracketLevelChanges);
        }

        [Fact]
        public void Modify_TrailedStopLeg_RecordsNewStopLevelChange()
        {
            Portfolio portfolio = new(10_000m);
            BrokerSimulator broker = new(portfolio);

            BracketHandle handle = broker.SubmitBracket(new BracketRequest
            {
                Entry = new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 10 },
                StopPrice = 90m,
                TargetPrice = 120m
            });

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
