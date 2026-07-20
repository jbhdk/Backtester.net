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

## Prerequisite

`ANTHROPIC_API_KEY` must be set in the environment. It is the feature's only credential and its only
prerequisite — create a key at [console.anthropic.com](https://console.anthropic.com/settings/keys).
The key is read from the environment, never passed by the caller, and a missing one fails **before**
anything is sent, with a message naming the variable, so it is never mistaken for the service
rejecting a key.

## Quick start

```csharp
IAnalysisClient client = new ClaudeAnalysisClient("claude-sonnet-5");
AnalysisOptions options = new AnalysisOptions { ModelName = "claude-sonnet-5" };

ReportAnalyzer analyzer = new ReportAnalyzer(client, options);
ReportAnalysis analysis = await analyzer.AnalyzeAsync(model, CancellationToken.None);
```

The model name is passed twice on purpose: the client asks it, and the Analyzer records it in the
Analysis's Provenance so a reader can tell what produced a claim.

## Choosing a model

**The minimum viable model is Sonnet-class.** Six models were evaluated by hand against a real run's
digest before this package was written. Above that floor, Findings are specific to the run and cite
figures that are actually in the digest; below it, they misattribute real digest values to the wrong
symbol, row or concept — plausible, checkable-looking, and wrong. Locally hosted models were part of
that evaluation and none of them came close, on accuracy or on runtime; there is **no local option**
for this feature.

## Notes

- **Thinking configuration is model-dependent**, and the client derives it from the configured model
  so the whole range works without a code change. Adaptive thinking exists on 4.6-and-later models;
  older ones — `claude-haiku-4-5`, for instance — reject adaptive and require an explicit token
  budget. Naming a model the client cannot read a version from is treated as current.
- **Structured output.** The contract's schema travels in `output_config.format`, so the shape
  constrains generation rather than being requested in prose and repaired afterwards.
- **Failures name their cause.** A request the service rejected and a request that never arrived are
  reported as different `ClaudeAnalysisException` messages, never as the same thing.
