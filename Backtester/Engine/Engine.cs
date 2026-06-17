using System;
using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;
using Backtester.Data;
using Backtester.Strategies;

namespace Backtester.Engine
{
    /// <summary>
    /// Orchestrates the bar-by-bar backtest loop: feeds market data to the strategy,
    /// submits resulting orders to the broker, and records portfolio equity after each bar.
    /// </summary>
    public class Engine : IEngine
    {
        private readonly IMarketDataFeed _feed;
        private readonly IStrategy _strategy;
        private readonly IBrokerSimulator _broker;
        private readonly Portfolio _portfolio;
        private bool _stopRequested;

        /// <summary>
        /// Initializes a new engine with the required data feed, strategy, broker, and portfolio.
        /// </summary>
        public Engine(IMarketDataFeed feed, IStrategy strategy, IBrokerSimulator broker, Portfolio portfolio)
        {
            _feed = feed;
            _strategy = strategy;
            _broker = broker;
            _portfolio = portfolio;
        }

        /// <summary>Begins the backtest loop, processing bars until the feed is exhausted or <see cref="Stop"/> is called.</summary>
        public void Start()
        {
            _stopRequested = false;
            while (!_stopRequested && _feed.Advance())
                RunOnce();
        }

        /// <summary>Signals the engine to halt after completing the current bar.</summary>
        public void Stop()
        {
            _stopRequested = true;
        }

        /// <summary>
        /// Processes a single bar: fills orders queued on the previous bar, records equity, then invokes the strategy
        /// and queues any new orders for the next bar. This ordering prevents lookahead bias (ADR 0001).
        /// </summary>
        public void RunOnce()
        {
            MarketSlice slice = _feed.GetCurrentSlice();
            _broker.ProcessBar(slice);
            _portfolio.RecordEquitySnapshot(slice);

            PortfolioSnapshot snapshot = _portfolio.SnapshotAt(slice.Timestamp);
            foreach ((string symbol, Candle bar) in slice.BarsBySymbol)
            {
                if (bar == null) continue;
                IEnumerable<OrderRequest> orders = _strategy.OnBar(symbol, bar, snapshot);
                foreach (OrderRequest order in orders)
                    _broker.SubmitOrder(order);
            }
        }
    }
}
