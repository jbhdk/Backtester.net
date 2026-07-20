# AI analysis ships as a package pair with the vendor seam cut low

The report should be able to carry an **Analysis** — a machine-generated critique of a run, as a
summary plus a list of Findings. Producing one requires calling a live AI service, which no existing
package may do: `Report` is a pure projection (ADR 0008) and `Report.Toolkit` references `Report` only
and touches no network (ADR 0017). The feature is also expected to grow several AI back ends (Ollama,
OpenAI, Gemini, OpenRouter), so where the vendor abstraction is cut decides whether back ends two
through four are transport or duplication.

We decided the feature ships as **two packages**. `backtester.net.analysis`
(`Backtester.Analysis`) references `Report` only and **makes no outbound call, ever**: it owns the
Analysis digest, the instructions, the required output shape, the validation, and the `IAnalysisClient`
seam. `backtester.net.analysis.ollama` (`Backtester.Analysis.Ollama`) is the first Analysis client and
holds the only network code. The seam is cut **low**: a client takes a prompt and a schema and returns
raw text, and knows nothing about digests, Findings, or validity. `ReportAnalysis` and
`ReportFinding` themselves live in `Report` alongside `ReportCard`, because the template renders them
and `Report` must not depend on the analyzer.

This follows **ADR 0009**'s network-vs-offline line exactly. An Ollama client is BCL-only and calls a
live service — precisely the case ADR 0009 reclassified when it moved Yahoo out of core after the
third-party-vs-BCL rule proved unstable. Putting the client inside `backtester.net.analysis` would mean
referencing the analysis package pulls in outbound calls, which is the property ADR 0009 exists to
prevent. It also follows **ADR 0016/0017**'s shape on the report side: the Analysis is *caller-supplied*
and layered onto the model after projection, so `ReportModelBuilder.Build(BacktestResult)` stays pure
and leaves `Analysis` null.

## Considered options

- **One package containing the analyzer and the first client.** Fewer projects for a feature with one
  back end today. Rejected: it puts a network call inside the package every consumer references, and
  adding the second back end forces the split anyway — as a breaking change, once the package is on the
  feed.
- **Cut the seam high (`IReportAnalyzer`: `ReportModel` in, `ReportAnalysis` out, per vendor).** Lets a
  back end use its own agentic loop or vendor-specific reasoning. Rejected: the digest, the instructions
  and the validation would be copied into every client and drift, which destroys the property the
  feature is for — an Analysis reading the same whichever AI produced it. A client must not be able to
  change what the Analysis says, only how it is transported.
- **Depend on a vendor-abstraction library (Microsoft.Extensions.AI, Semantic Kernel).** Every back end
  for free. Rejected: it puts a third-party dependency and its version churn in the analysis package,
  and hands control of structured-output fidelity — the thing the whole contract rests on — to someone
  else's abstraction.
- **Have the report generate the Analysis during projection.** Simplest call site. Rejected: it breaks
  ADR 0008's pure projection and would make writing a file a network operation.

## Consequences

- The transitive closure is `analysis.ollama → analysis → report → backtester`. The analysis package's
  published nuspec declares exactly one dependency, `backtester.net.report`; a consumer running a local
  model never pulls a hosted vendor's package, and vice versa.
- Both projects version and pack independently with their own `buildnumber.txt` and
  `GeneratePackageOnBuild`, through the shared `Directory.Build.targets` target — the same machinery as
  `Report`, `Report.Toolkit`, and the data providers.
- The call site is **model-first and explicit**: build the model, attach configuration, await the
  analyzer, attach the analysis, write. Configuration must be attached *before* analysis, because the
  digest includes it — the analyzer cannot comment on a setting it was never shown. The existing
  one-call `Write(result, path, configuration)` convenience does not extend to analysis, deliberately:
  it builds the model internally where the caller cannot reach it.
- Because the analyzer consumes a `ReportModel` and a `ReportModel` is serializable, an Analysis can be
  regenerated from a saved model without re-running the backtest.
- A future back end's home is decided by one question, mirroring ADR 0009: does it call a live service?
  Network → its own `backtester.net.analysis.<vendor>` package; the analyzer stays untouched.
