# The Analysis contract is enforced, not trusted

An Analysis is produced by an AI, which is an **untrusted source**: it will occasionally name a
severity the contract does not define, omit a recommendation, wrap its answer in a markdown fence, or
answer in prose. The report renders Findings with severity-dependent styling and ordering, so it needs
the shape to actually hold — and the reader, looking at a rendered section, has no way to tell which
parts the AI produced and which the code invented on its behalf.

We decided the Analyzer **validates strictly and never repairs**. The request goes out using each
service's native structured-output mode (Ollama's `format`, OpenAI's `response_format: json_schema`
with `strict: true`, Gemini's `responseSchema`) rather than asking for JSON in prose, so violations are
rare by construction. What comes back is parsed and validated hard: an unknown severity or category is
a violation, a missing required field is a violation, anything not well-formed is a violation. On a
violation the Analyzer **retries exactly once** with the validation error fed back, and if that fails
it throws. A run gets a valid Analysis or no Analysis; the report renders no section when `Analysis` is
null.

The Analyzer also owns the instructions. A caller may append **Guidance** — strategy intent or a focus
the digest cannot carry — but cannot replace the system prompt, because the prompt defines the Finding
vocabulary and category set and is therefore the other half of the contract.

## Considered options

- **Repair leniently** (strip fences, match enums case-insensitively, map unknown severities to Medium,
  drop malformed Findings). Nearly always yields something. Rejected: it silently degrades the Analysis
  and the reader cannot tell it happened. This is the same instinct as the report's handling of engine
  decisions — reject rather than clamp, and surface what was decided rather than quietly normalising it.
- **Strict with no retry.** Fully deterministic and simplest to reason about. Rejected: a single
  transient formatting slip costs the entire Analysis and a full re-request, which on a hosted vendor is
  a real cost and on a local model is a real wait, for no gain in correctness.
- **Let the caller supply the whole prompt.** Maximum room to experiment. Rejected: Analyses across runs
  stop being comparable, and a prompt that never mentions the category set produces Findings that are
  all one category — the contract holds structurally while becoming meaningless.
- **Have Findings cite the metrics they rest on**, so the report can link a Finding to its stat tile.
  Attractive, and deferred rather than rejected: it adds a field the model can populate with invented
  metric names, which the Analyzer would then have to validate against the digest. Worth revisiting once
  the base contract has proven itself.

## Consequences

- Digest overflow is governed by the same instinct: a run with more round trips than the digest admits
  is **rejected** with a message naming the count and the cap. Sampling is available only when the
  caller asks for it explicitly, and a sampled digest declares its own sampling inside the payload so
  the AI is told it is reasoning over part of a run.
- `Category` and `Severity` are enums on the public types and serialize to strings for the page,
  matching how `ExitReason` and `Direction` are already page-friendly strings in the report model.
  Adding a category or severity later is a contract change affecting every client.
- The Analysis section renders its **Provenance** — service, model, timestamp — as its
  subtitle. A reader six months on must be able to tell whether a claim came from a 7B local model or a
  frontier one, and the section must be unmistakably machine-generated.
- The Analyzer is tested against a faked `IAnalysisClient`, following the existing precedent of faking
  the `IHistoricalDataProvider` network seam in `HistoricalDataFetcherTests`. Contract violations are
  testable without a running AI service.
- Strict validation makes a weak local model's failure **loud**. That is intended: the first client is
  Ollama precisely so the pipeline is proven against the hardest structured-output target before a
  hosted vendor makes violations rare enough to hide.
