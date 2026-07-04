# Design & planning notes

Working design documents and historical implementation plans for Pawn Diary, kept out of the
repository root so the root holds only the living top-level docs (`README.md`, `DOCUMENTATION.md`,
`EVENT_PROMPT_MAP.md`, `INTEGRATIONS.md`, `CHANGELOG.md`, and the agent files
`AGENTS.md`/`CLAUDE.md`/`CODEX.md`).

These are notes, not contracts. The authoritative descriptions of current behavior live in
`../DOCUMENTATION.md`, and the shipped public integration contract lives in `../INTEGRATIONS.md`.

| Document | What it is | Status |
|---|---|---|
| `MOD_COMPAT_PLAN.md` | **The single coherent external-mod integration design doc** — ideas, API version ledger/roadmap, and the target-mod survey/patch plan. Reconciles all integration ideas in one place. | living |
| `API_V4_PAWN_CONTEXT_PROVIDERS.md` | Design-doc-before-code brief for API v4 (`RegisterPawnContextProvider`): surface, sanitation/failure-isolation, purity boundary, player-toggle decision, and the RimPsyche consumer snippet. Elaborates MOD_COMPAT §4.2. | design draft |
| `EVENT_COVERAGE_PLAN.md` | Gap analysis of which RimWorld moments the diary covers, with XML-only suggestions to extend atmosphere. | proposal / partly implemented |
| `BODY_PART_EVENTS_PLAN.md` | Hand-off brief for the body-part-change diary events. | implemented 2026-07-03 |
