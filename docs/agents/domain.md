Domain docs consumption

Layout: single-context

Agent skills expect a single `CONTEXT.md` at the repository root (if present) and an optional `docs/adr/` directory for architectural decision records.

If this repository is a monorepo with multiple contexts, create a `CONTEXT-MAP.md` at the root pointing to per-context `CONTEXT.md` files and update this document accordingly.

Notes for agents

- Look for `CONTEXT.md` at the repo root.
- Look for ADRs under `docs/adr/`.
- If neither exists, agents will proceed but may lack domain-specific terminology; consider adding `CONTEXT.md` to improve automated suggestions and code transformations.
