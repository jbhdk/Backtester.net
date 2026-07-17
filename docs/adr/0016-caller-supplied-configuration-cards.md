# Configuration cards are caller-supplied and bypass the report projection

The report needs to show a run's **configuration** — how the strategy was set up (its parameters,
the chosen execution models, and any other run-declared context) — with the motivating case being
"record the settings of a specific strategy test." This information does not exist in a
`BacktestResult`: the engine never sees a strategy's parameters generically and cannot derive them.
So the feature cannot be a projection; it needs an input channel the projection does not have.

We decided the report carries **`ReportModel.Configuration`**, a caller-supplied
`IReadOnlyList<ReportCard>` (each a `{ Title, Headers, Rows }` table of pre-formatted strings), that
**`ReportModelBuilder` never touches**. The builder's `Build(BacktestResult)` path stays a pure
projection and leaves `Configuration` null; the caller attaches cards through new
`HtmlReportWriter.BuildHtml(result, configuration)` / `Write(result, path, configuration)` overloads,
which build the model and then set the property. The template renders the cards in a dedicated
section above the equity curve, reusing the `.stat-group` card chrome with an inner `<table>`. This
is a deliberate, narrow hole in the "a report is built from the result alone" invariant of ADR 0008:
derived run **context** still comes only from the result; caller-declared **configuration** is the
one thing the caller layers on top.

## Considered options

- *Put the settings on `BacktestResult` so the builder projects them*: rejected — it pollutes the
  engine's contract with opaque display data the engine cannot produce or validate, which is exactly
  the caller-supplied run context ADR 0008 kept out. The projection would carry data it only ever
  passes through.
- *Have the strategy expose its settings and the engine surface them (as it does indicators, ADR
  0007)*: rejected — it couples the feature to strategies and cannot describe anything non-strategy
  (data range, machine, git SHA, execution-model choices), which is a large part of a run's
  configuration.

## Consequences

- `Configuration` is distinct from the derived **Run context** card (`ReportRunInfo`): context is
  *what the run covered* (derived); configuration is *how it was set up* (caller-declared). The two
  can overlap (symbols, interval, starting equity) and that duplication is accepted.
- Cells are strings only; the renderer escapes them and renders ragged rows as given. The report
  layer performs no validation of card shape — a malformed table is visible to the caller who built
  it.
- The same `ReportCard` primitive is intended to back a future general "custom cards" section; only
  the top configuration placement is wired up now.
