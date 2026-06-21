# Codex pull request review

Review this pull request for bugs, behavioral regressions, security issues, broken tests, and missing validation.

Use the repository instructions in `AGENTS.md`. Focus on changed behavior and reachable impact, not general style preferences.

The workflow checked out the pull request merge commit and fetched both parents. Inspect the pull request change with:

```bash
git diff --find-renames HEAD^1 HEAD^2
```

Return Markdown suitable for a pull request comment:

- Put findings first, ordered by severity.
- For each finding, include the file/path area, the issue, why it matters, and a concrete fix.
- If you find no actionable issues, say that clearly and mention any residual test or runtime risk.
