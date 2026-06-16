Triage label vocabulary

This repository uses the following canonical label strings for agent triage workflows. These map directly to GitHub label names.

- needs-triage — maintainer needs to evaluate the issue
- needs-info — waiting on reporter for more data
- ready-for-agent — fully specified; an automated agent can pick this up and implement without human context
- ready-for-human — requires a human to implement or review
- wontfix — will not be actioned

If your repository already uses different label names, update this file to map the canonical roles to your actual labels. Agent skills will use these strings when applying labels.
