using System;

namespace Backtester.Engine
{
    using Backtester.Broker;
    using Backtester.Core;
    using Backtester.Data;
    using Backtester.Strategies;

    public class Engine : IEngine
    {
        private readonly IMarketDataFeed _feed;
        private readonly IStrategy _strategy;
        private readonly IBrokerSimulator _broker;
        private readonly Portfolio _portfolio;
        private bool _stopRequested;

        public Engine(IMarketDataFeed feed, IStrategy strategy, IBrokerSimulator broker, Portfolio portfolio)
        {
            _feed = feed;
            _strategy = strategy;
            _broker = broker;
            _portfolio = portfolio;
        }

        public void Start()
        {
            _stopRequested = false;
            while (!_stopRequested && _feed.Advance())
                RunOnce();
        }

        public void Stop()
        {
            _stopRequested = true;
        }

        public void RunOnce()
        {
            var slice = _feed.GetCurrentSlice();
            var snapshot = _portfolio.SnapshotAt(slice.Timestamp);

            foreach (var (symbol, bar) in slice.BarsBySymbol)
            {
                if (bar == null) continue;
                var orders = _strategy.OnBar(symbol, bar, snapshot);
                foreach (var order in orders)
                    _broker.SubmitOrder(order);
            }

            _broker.ProcessBar(slice);
            _portfolio.RecordEquitySnapshot(slice);
        }
    }
}
