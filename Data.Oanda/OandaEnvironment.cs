namespace Backtester.Data.Oanda
{
    /// <summary>
    /// The Oanda v20 REST host to send requests to. Both environments serve identical real market
    /// candles — Oanda's Practice environment is not simulated data — so this only selects which
    /// account type's API token is accepted, not data quality.
    /// </summary>
    public enum OandaEnvironment
    {
        /// <summary>The Practice (demo account) host, <c>https://api-fxpractice.oanda.com</c>.</summary>
        Practice,

        /// <summary>The Live (funded account) host, <c>https://api-fxtrade.oanda.com</c>.</summary>
        Live
    }
}
