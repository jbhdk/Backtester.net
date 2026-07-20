# backtester.net.analysis.claude

The [Claude](https://www.anthropic.com/claude) Analysis client for the
[backtester.net](https://www.nuget.org/packages/backtester.net) engine.

Kept in its own package because it is the first code in this feature that touches the network, and
because it takes a third-party dependency (the official `Anthropic` SDK). Referencing
`backtester.net.analysis` therefore never pulls in an outbound call.

## What it is

`ClaudeAnalysisClient` implements the analysis package's `IAnalysisClient` seam. It carries an
`AnalysisRequest` — system prompt, user prompt, and the Analysis contract's JSON schema — to Claude
and returns the raw answer. It knows nothing about digests, Findings, or validity: the `ReportAnalyzer`
owns the contract, so an Analysis reads the same whichever AI produced it.

This is an **Analysis client**, never a Provider. A Provider fetches bars.

## Quick start

Set `ANTHROPIC_API_KEY` in the environment, then:

```csharp
IAnalysisClient client = new ClaudeAnalysisClient("claude-opus-4-8");
AnalysisOptions options = new AnalysisOptions { ModelName = "claude-opus-4-8" };

ReportAnalyzer analyzer = new ReportAnalyzer(client, options);
ReportAnalysis analysis = await analyzer.AnalyzeAsync(model, CancellationToken.None);
```

The documented minimum model is Sonnet-class. Smaller models were evaluated against a real run's
digest and every one of them misread it.

## Notes

- **Credential.** The key is read from `ANTHROPIC_API_KEY`, never passed by the caller. When it is
  missing the client fails with a message naming the variable and pointing at where to create a key.
- **Structured output.** The contract's schema travels in `output_config.format`, so the shape
  constrains generation rather than being requested in prose and repaired afterwards.
- **Thinking configuration** is derived from the configured model, so adaptive-capable models and
  older ones both work without a code change.
