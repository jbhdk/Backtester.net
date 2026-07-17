# backtester.net.report.toolkit

Report-side convenience helpers for [backtester.net](https://github.com/jbhdk/Backtester.net).

Reflects an attributed settings object into the report's caller-supplied configuration
cards, so a strategy's parameters render at the top of the HTML report without hand-writing
settings tables.

## Usage

Decorate your parameter properties with `ReportSettingAttribute`, giving each a display
label and a group. Within a group, use the optional `Order` to promote the most important
settings — rows sort by `Order` ascending, and equal-`Order` rows keep their declaration
order (a stable sort):

```csharp
public class MyParameters
{
    [ReportSetting("Fast period", "MACD", Order = 1)]
    public int FastPeriod { get; set; }

    [ReportSetting("Slow period", "MACD", Order = 2)]
    public int SlowPeriod { get; set; }

    [ReportSetting("Risk fraction", "Risk")]
    public decimal RiskFraction { get; set; }

    // Excluded from the cards entirely — for computed or redundant convenience properties.
    [ReportSettingIgnore]
    public int SlowMinusFast => SlowPeriod - FastPeriod;
}
```

Hand the object to `ConfigurationCardBuilder.Build`, then pass the resulting cards to the
report writer's configuration overload:

```csharp
ConfigurationCardBuilder builder = new ConfigurationCardBuilder();
IReadOnlyList<ReportCard> cards = builder.Build(parameters);

new HtmlReportWriter().Write(result, path, cards);
```

The builder returns a plain list, so you compose it with your own hand-built cards
(portfolio, broker, …) yourself — concatenate before passing them to `Write`. The toolkit
reflects exactly one object and never assembles the final list for you.

## Grouping and ordering

- **One card per group.** Properties sharing a `Group` render together in a single card
  titled with the group name.
- **Group order follows declaration order** — groups appear in the order their first member
  is declared on the settings object, so you control layout by ordering properties
  top-to-bottom.
- **Row order within a group** follows `Order` ascending, ties broken by declaration order.
  The default `Order` of `0` leaves rows in declaration order.

## Drift safety: the "Other" catch-all

Every public property is shown unless you mark it with `ReportSettingIgnore`. A property
**without** `ReportSettingAttribute` still appears — in a catch-all group named `"Other"`,
labelled by its property name. So a parameter you add but forget to attribute is never
silently omitted from the report; it shows up as a visible-but-untidy row signalling it
needs attention. The `"Other"` card always renders **last**, keeping your curated groups at
the top and the catch-all clearly a leftover bucket.

`ReportSettingIgnore` wins over everything: an ignored property appears in no card, not even
`"Other"`, even when it also carries a `ReportSettingAttribute`.

## Value formatting

Values are rendered raw and honestly — exactly what you would paste back into code to
reproduce the run — with no percentage or unit prettifying:

- Every value is formatted with `Convert.ToString(value, CultureInfo.InvariantCulture)`, so
  a decimal like `0.006` reads the same on a Danish machine as in a report shared with
  anyone else.
- Booleans render as invariant `True` / `False`.
- A `null` value renders as an empty string.

## Dependencies

This package depends only on the `backtester.net.report` package — it pulls in no engine,
network, or vendor dependency.
