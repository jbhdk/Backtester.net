# backtester.net.report.toolkit

Report-side convenience helpers for [backtester.net](https://github.com/jbhdk/Backtester.net).

Reflects an attributed settings object into the report's caller-supplied configuration
cards, so a strategy's parameters render at the top of the HTML report without hand-writing
settings tables.

## Usage

Decorate your parameter properties with `ReportSettingAttribute`, giving each a display
label and a group:

```csharp
public class MyParameters
{
    [ReportSetting("Fast period", "MACD")]
    public int FastPeriod { get; set; }

    [ReportSetting("Slow period", "MACD")]
    public int SlowPeriod { get; set; }

    [ReportSetting("Risk fraction", "Risk")]
    public decimal RiskFraction { get; set; }
}
```

Hand the object to `ConfigurationCardBuilder.Build`, and pass the resulting cards to the
report writer:

```csharp
ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
IReadOnlyList<ReportCard> cards = builder.Build(parameters);
```

The builder produces one card per group, ordered by the declaration order of each group's
first member. Values are formatted with invariant culture.

This package depends only on the `backtester.net.report` package — it pulls in no engine,
network, or vendor dependency.
