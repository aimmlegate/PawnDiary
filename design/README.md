# Design & planning notes

Working design documents and historical implementation plans for Pawn Diary, kept out of the
repository root so the root holds only the living top-level docs (`README.md`, `DOCUMENTATION.md`,
`EVENT_PROMPT_MAP.md`, `INTEGRATIONS.md`, `CHANGELOG.md`, and the agent files
`AGENTS.md`/`CLAUDE.md`/`CODEX.md`).

These are notes, not contracts. The authoritative descriptions of current behavior live in
`../DOCUMENTATION.md`, and the shipped public integration contract lives in `../INTEGRATIONS.md`.

| Document | What it is | Status |
|---|---|---|
| `EXTERNAL_API_CAPABILITIES.md` | **Authoritative capability catalog for the public API surface** — every shipped/requested/proposed capability (inbound, read, style, meta), the internal hook it maps to, the cross-cutting decisions, and the v4→v8 version sequencing. The authority on API *shape*. | planning draft |
| `MOD_COMPAT_PLAN.md` | **External-mod integration design doc** — integration mechanisms, the target-mod survey, and the patch/PR plan (which mods, in what order, via XML groups vs. API). Owns *who* we integrate with; the capability catalog owns *what the API can do*. | living |
| `API_V4_PAWN_CONTEXT_PROVIDERS.md` | Design-doc-before-code brief for the one capability C-CTX-1 (API v4 `RegisterPawnContextProvider`): surface, sanitation/failure-isolation, purity boundary, player-toggle decision, and the RimPsyche consumer snippet. | design draft |
| `EVENT_COVERAGE_PLAN.md` | Gap analysis of which RimWorld moments the diary covers, with XML-only suggestions to extend atmosphere. | proposal / partly implemented |
| `BODY_PART_EVENTS_PLAN.md` | Hand-off brief for the body-part-change diary events. | implemented 2026-07-03 |
