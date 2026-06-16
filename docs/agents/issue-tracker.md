Issue tracker: GitHub

This repository is configured to use GitHub Issues as the canonical issue tracker for agent-driven workflows. The detected remote is `jbhdk/Backtester.net` and agent skills will target that repository by default.

How agent skills will operate

- Read issues: agent skills will use the GitHub API or the `gh` CLI to list and read issues from `jbhdk/Backtester.net`.
- Create issues: the `to-issues` and similar skills will call `gh issue create --repo jbhdk/Backtester.net` (or use the API) to open new issues when requested.
- Labels and comments: skills will manage labels, add comments, and close issues via the GitHub API / `gh` CLI against `jbhdk/Backtester.net`.

Prerequisites for automation

- The environment running agent skills should have `gh` authenticated (e.g., `gh auth login`) or provide a GitHub token via environment variable.
- If the repository uses a GitHub Enterprise instance, set the appropriate API base and auth.

If you prefer a different tracker (GitLab, local markdown, Jira, etc.), re-run the setup skill to change this file.
