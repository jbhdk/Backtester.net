# backtester.net.analysis

AI-agnostic report analysis for [backtester.net](https://github.com/jbhdk/Backtester.net).

Reduces a report model to an **Analysis digest** — the run context, the Performance stats,
the per-symbol stats, the round trips, the rejected orders, and your configuration cards,
rendered as compact markdown with every value formatted the way the report displays it —
and hands that digest to an **Analysis client** for critique.

This package **makes no outbound call, ever**. It references `backtester.net.report` and
nothing else. The network lives in a separate client package, one per AI service.

## Usage

The analysis path is model-first: build the model, attach configuration, then analyse.
Configuration must be attached *before* analysing, or the digest cannot show the settings
the run actually used.

```csharp
ReportModelBuilder modelBuilder = new ReportModelBuilder();
ReportModel model = modelBuilder.Build(result);
model.Configuration = cards;

AnalysisOptions options = new AnalysisOptions { ModelName = "qwen2.5:14b" };
ReportAnalyzer analyzer = new ReportAnalyzer(client, options);
string answer = await analyzer.AnalyzeAsync(model);
```

### Guidance

The instructions are owned by the analyzer and cannot be replaced. You may append free-text
**guidance** carrying the strategy's intent or a focus for this particular run, which the
digest itself cannot express:

```csharp
AnalysisOptions options = new AnalysisOptions
{
    ModelName = "qwen2.5:14b",
    Guidance = "This is a mean-reversion strategy. I care most about whether the stop is too tight."
};
```

### Digest size

The digest is bounded by **round-trip count**, not by a token estimate. A run with more
round trips than `RoundTripCap` (500 by default) is **rejected** with an
`AnalysisDigestOverflowException` naming both the actual count and the cap:

```csharp
AnalysisOptions options = new AnalysisOptions
{
    ModelName = "qwen2.5:14b",
    RoundTripCap = 200,
    OverflowPolicy = AnalysisOverflowPolicy.Sample
};
```

With `Sample`, the digest keeps evenly spaced round trips spanning the whole run and
**declares its own sampling inside the payload** — included count, total count, and
selection basis — so the AI is told explicitly that it is reasoning over part of a run.

### The contract is enforced, not trusted

The AI is an untrusted source, so its answer is validated strictly and **never repaired**. An
unknown severity, an unknown category, a missing required field, an answer that is not
well-formed, and an answer wrapped in a markdown fence are all violations. Values are never
coerced to a nearest match, and a malformed Finding is never silently dropped — a reader
looking at a rendered section has no way to tell which parts the AI produced and which the
code invented on its behalf.

A violation costs **exactly one retry**, carrying the validation error back to the model as a
`## Correction` section appended to the user prompt. A second violation throws an
`AnalysisFormatException` naming it. A run gets a valid Analysis or no Analysis; the report
renders no section when `Analysis` is null, so the caller decides what a failure means:

```csharp
try
{
    model.Analysis = await analyzer.AnalyzeAsync(model, cancellationToken);
}
catch (AnalysisFormatException exception)
{
    // Write the report without an Analysis section.
}
```

Unknown extra fields in an otherwise valid answer are ignored rather than rejected.

### Writing your own Analysis client

`IAnalysisClient` is deliberately cut low: it takes a request carrying the instructions,
the digest, and the required output shape, and returns the service's raw answer. It decides
nothing about what is asked or what is acceptable.

```csharp
public class MyAnalysisClient : IAnalysisClient
{
    public Task<string> AskAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        // Carry request.UserPrompt to your service and return its raw response text.
    }
}
```

The same seam makes the digest dumpable for human review: pass a client that writes
`request.UserPrompt` to a file instead of calling anything.

## Documentation

- [ADR 0018](https://github.com/jbhdk/Backtester.net/blob/main/docs/adr/0018-analysis-package-pair-and-client-seam.md) — the package pair and the low client seam
- [ADR 0019](https://github.com/jbhdk/Backtester.net/blob/main/docs/adr/0019-analysis-contract-is-enforced-not-trusted.md) — the Analysis contract is enforced, not trusted
