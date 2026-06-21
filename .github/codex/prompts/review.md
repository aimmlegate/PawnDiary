# Codex pull request review

Review this pull request for bugs, behavioral regressions, security issues, broken tests, and missing validation.

Use the repository instructions in `AGENTS.md`. Focus on changed behavior and reachable impact, not general style preferences.

The workflow fetched the base branch and PR head before this prompt runs. Inspect the change with:

```bash
git diff --find-renames "origin/${PR_BASE_REF}...origin/pull/${PR_NUMBER}"
```

Return Markdown suitable for a pull request comment:

- Put findings first, ordered by severity.
- For each finding, include the file/path area, the issue, why it matters, and a concrete fix.
- If you find no actionable issues, say that clearly and mention any residual test or runtime risk.
