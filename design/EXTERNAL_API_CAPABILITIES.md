# External API — Capability Catalog

> **This is the authoritative plan for the *shape of the public API surface itself*** — every
> capability an external mod can use or has been asked to support, its status, the internal hook it
> maps to, and the decision it still carries. It sits beside the two existing docs, which keep their
> jobs:
> - `../INTEGRATIONS.md` — the **shipped contract** (stable, adapter-facing reference). Only shipped
>   capabilities appear there.
> - `MOD_COMPAT_PLAN.md` — the **target-mod survey** (which mods to patch, in what order, via XML
>   groups vs. API). It owns *who* we integrate with; this doc owns *what the API can do*.
> - `API_V4_PAWN_CONTEXT_PROVIDERS.md` — the deep-dive brief for one capability (C-CTX-1).
>
> Status: **planning draft (2026-07-04).** Consolidates the shipped v1–v3 surface, the owner's
> requested capabilities, and the proposed additions from the same discussion. No code; this is the
> sequencing/decision map that precedes the per-version design briefs.

Follow `skills/pawndiary-engineering/SKILL.md` and `AGENTS.md`. Nothing here is a contract until it
ships into `INTEGRATIONS.md`.

---

## 1. How to read this

**Status legend**
- **shipped (vN)** — live in `INTEGRATIONS.md`, `ApiVersion >= N`.
- **requested** — the owner asked for it (this session); accepted in principle, needs a brief.
- **proposed** — surfaced here as a gap/complement; not yet accepted.
- **designed** — has a written brief but no code (currently only C-CTX-1, the v4 providers brief).

**Design invariants every capability inherits** (from the `INTEGRATIONS.md` stability promise, and
non-negotiable — see §5): additive-only evolution; `PawnDiaryApi` is the one public entry point;
never throw into a caller; main-thread only; plain-string/DTO across the boundary, never live
`Pawn`/`Def`/settings objects into pure code; a shipped `eventKey`/style token is save-data and is
never renamed.

**ID scheme** used below (doc-local, not a code name): `IN-*` inbound, `RD-*` read, `ST-*`
style/persona/generation, `MT-*` meta/lifecycle, `C-CTX-*` context-into-our-prompt.

---

## 2. The catalog

### 2.1 Inbound — creating entries (`IN-*`)

The four requested creation modes form a spectrum from *caller owns the text* to *we own the text*.
All four still pass the universal gates (eligibility, per-pawn generation state where relevant,
consent, dedup, main-thread) — they differ only in **who produces the prose**.

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| IN-1 | **Submit event** (adapter pushes a moment; we build the whole prompt) | **shipped v1** | `SubmitEvent` → `ExternalEventSignal` → generation pipeline; External-domain group claims `eventKey` | — |
| IN-2 | **Create from full prompt** — caller supplies the entire system+user prompt, we only run the LLM call | requested | bypasses `PromptAssembler`; hands text straight to `LlmClient` | Bypasses persona/style/localization/safety; spends the player's token budget on arbitrary text → own consent + caps (§3.2, §3.3) |
| IN-3 | **Create from partial prompt** — caller supplies a fragment, we wrap it with our persona/event-prompt/context | requested | inject into `userPrompt`; reuse the layering `DiaryPromptBuilder`/`PromptAssembler` already do | Cleanest prompt mode; mostly a new "external prompt fragment" request field |
| IN-4 | **Create from direct text + tag + title** — no LLM at all | requested | set the POV slot (`initiator/recipient/neutralGeneratedText`) + mark status complete; skip generation | Should it honor the per-pawn "generation disabled" toggle? Injecting ≠ generating → define consent (§3.5) |
| IN-5 | **Create from direct text + tag, no title** — we generate only the title | requested | as IN-4, then queue only the title step | Trivial once IN-4 exists |
| IN-6 | **Return a stable entry handle** from every create call | proposed | new: mint + return an id at submit time | Nothing today lets a caller correlate a submission with its result; prerequisite for MT-1/MT-2 |
| IN-7 | **Idempotency key for direct-text** (IN-4/5) | proposed | reuse the event `dedupKey`/`dedupTicks` mechanism for injected entries | Without it, a repeated call duplicates the page |

### 2.2 Read — retrieval & context (`RD-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| RD-1 | **Recent entry titles** for a pawn | **shipped v2** | `GetRecentEntryTitles` → `DiaryEntryTitleSnapshot` (cap 20) | — |
| RD-2 | **Last N entries with prose** (title + first sentence / summary) | requested (= roadmap v5) | reads persisted diary model; `TryGetContextSnapshot` shape in MOD_COMPAT §4.3 | Merge the old v5 "prose snapshot" and this into one filtered read |
| RD-3 | **Filters: type, atmosphere, date, POV, archived** | requested | already stored per entry: `decorationDomain` (=type/domain), `atmosphereCue`, `date`/`tick`, `povRole`, `archived` | Free from existing fields |
| RD-4 | **Filter: tone** | requested | `tone` is a *group directive* (`group.tone`/`ToneDirective()`), applied at prompt time, **not stored per entry** | Must persist tone on the entry, or derive it from the entry's group — decide before promising it |
| RD-5 | **Get one entry by id** | proposed | companion to IN-6 handles | — |
| RD-6 | **Cheap counts / stats** (per pawn, per type, per date range) | proposed | tallies without materializing prose | — |
| RD-7 | **Filters: source/eventKey, partner pawn, importance, has-title** | proposed | `sourceId`/`eventKey` stored on external events; partner/POV known; group `important` flag | — |

### 2.3 Writing style / persona / generation control (`ST-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| ST-1 | **Get base writing style** | **shipped v3** | `GetWritingStyle` → `DiaryWritingStyleSnapshot` (`styleDefName`,`label`,`rule`) | — |
| ST-2 | **Set writing style** | requested | `SetPersona`/`diary.personaDefName` exists internally | **Set to an existing style Def, or a free-form custom `rule`?** Free-form = a new per-pawn saved custom-style field |
| ST-3 | **Reset writing style to pre-override** | requested | — (no saved override chain exists today; the only override is the transient, unsaved hediff one) | **Only coherent if ST-2 is a push/pop override, not a base change** — see §3.4. This is the pivotal style decision |
| ST-4 | **List available writing-style Defs** | proposed | `DiaryPersonas`/`DefDatabase<DiaryPersonaDef>` | Lets a mod show a picker instead of guessing defNames |
| ST-5 | **Temporary (prompt-time) style override — push/pop** | proposed | mirror the existing hediff prompt-time override mechanism (currently internal, unsaved) | The mechanism that makes ST-3 "reset" meaningful; kept distinct from ST-2 |
| ST-6 | **Get/Set per-pawn generation enabled** | proposed | `DiaryGenerationEnabledFor`/`SetDiaryGenerationEnabled` exist internally | — |
| ST-7 | **Expose whole persona vs. style-only** | proposed / open | `PersonaFor` exists; v3 deliberately exposed only the style slice | Decide scope: keep style-only, or open the persona |

### 2.4 Context into our prompts (`C-CTX-*`)

Distinct from IN-* (which create entries): these let an external mod feed *our own* generation.

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| C-CTX-1 | **Pawn-context providers** — a mod adds a `key=value` line to our pawn summary | **designed** (v4 brief) | `RegisterPawnContextProvider` → line in `BuildPawnSummary` next to DLC-identity fields | Player-toggle model still open — see `API_V4_PAWN_CONTEXT_PROVIDERS.md` §7 |
| C-CTX-2 | **Get the prompt context we'd use** (assembled pawn summary / game context) | proposed | expose the `BuildPawnSummary` output as a snapshot | Bridges IN-2/IN-3: lets a full-prompt caller still reflect the pawn |

### 2.5 Meta / safety / lifecycle (`MT-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| MT-0 | **Readiness probe** | **shipped v1** | `IsReady` | — |
| MT-1 | **Async completion signal** (submitted entry finished / failed) | proposed | fire from `ApplyLlmResult` (the existing main-thread completion seam) | The biggest *conceptual* addition, but **blast radius is Low** — no new threading; see §3.1 |
| MT-2 | **Query generation status** of a submitted entry (pending/complete/failed) | proposed | `LlmClient.IsInFlight(eventId, povRole)` already exists | Near-free once IN-6 exposes the handle |
| MT-3 | **Regenerate an existing entry** | proposed | `RegenerateEntry` exists internally | Exposure only |
| MT-4 | **Retract / delete an entry a mod created** | proposed | remove the event/archive row by handle + source check | Undo path for the source mod; guard so a mod only deletes its own |
| MT-5 | **Public `IsDiaryEligible(pawn)`** | proposed | `IsDiaryEligible` exists internally | Saves callers a silent-drop submit |
| MT-6 | **Per-capability consent toggles** | proposed / open | new settings; see §3.5 and the v4 §7 rethink | Injection (IN-2/3/4/5) and style-writing (ST-2/3) are more invasive than IN-1 → likely separate switches |
| MT-7 | **Cost / rate guardrails** for prompt-bearing calls (IN-2/IN-3) | proposed | caps around the LLM call | They spend real tokens on the player's key |
| MT-8 | **UI attribution** — mark externally authored/injected entries | proposed | `sourceId` already stored on external events; surface it in the tab | Lets players see what a mod wrote |

---

## 3. Cross-cutting decisions (the forks that shape everything above)

### 3.1 Async results — the central new need
`SubmitEvent` is fire-and-forget (`bool`). IN-2/IN-3 (and any caller who wants the generated text)
need the result *later*, off a background LLM call. **Decision: add a completion signal (MT-1) — a
callback registered per submission, or a global "entry completed" event, keyed by the IN-6 handle —
vs. keeping everything fire-and-forget and forcing callers to poll the read API (RD-5) for their
handle.** Recommendation: a completion event keyed by handle; it also serves chat mods that want to
react to *any* new entry. This is the prerequisite that unblocks the full/partial-prompt modes.

#### Blast-radius check (traced 2026-07-04) — verdict: **Low, contained**
The async→main-thread handoff **already exists**, so MT-1 does not introduce concurrency:
`LlmClient` runs the HTTP call on background workers and pushes results to a completed queue;
`DiaryGameComponent.DrainCompletedLlmWork()` drains it **on the main thread every tick**
(`while LlmClient.TryDequeueCompleted(out result) → ApplyLlmResult(result)`). A completion signal
fires **synchronously inside `ApplyLlmResult`** — no new thread, no marshaling, no `LlmClient` change.

- **Firing site:** one seam — `ApplyLlmResult` (main entry) + optionally `ApplyTitleResult` (title).
- **Correlation exists:** `eventId` + `povRole` already key everything; `LlmClient.IsInFlight(eventId,
  povRole)` already answers MT-2, making status-query near-free.
- **Handle (IN-6) is knowable at return:** the `DiaryEvent` is created **synchronously** inside
  `signal.Emit(...)` during the `SubmitEvent` call (`eventId = Guid.NewGuid()`), so the id can be
  stashed on the `ExternalEventSignal` and read back — keep `SubmitEvent`→`bool`, add
  `SubmitEventTracked`→handle (additive). No change to the shared `Submit`/`Emit`/dispatch signatures
  is required beyond the one stashed field.
- **New code is small:** a plain `DiaryEntryCompleted` DTO + a main-thread, per-callback try-wrapped
  dispatcher in the Integration layer.
- **Does NOT touch:** `LlmClient` internals, HTTP workers, failover, the request/result DTOs (already
  carry `eventId`/`povRole`/`success`), native signal sources, or the save format (event model).

**Save/load caveat (verified):** in-flight LLM work is **not** persisted as a queue; stranded-pending
entries are re-queued by orphan-recovery after load, and their completions fire *post-load*. This
picks the delivery shape: a **global completion event survives save/load** (no persisted delegates)
and tolerates post-load firing; **per-handle callbacks orphan** across a save (delegates aren't
serializable) and need lifetime bookkeeping. → **Recommend the global event**, and document
completion as **best-effort**: it may fire after a load and may never fire if the event/pawn is gone.

### 3.2 Bypass vs. consistency (IN-2 full prompt)
A full external prompt bypasses persona, writing style, localization, and our prompt-safety framing —
so the entry won't "sound like the pawn" and we can't localize it. **Decision: does IN-2 still get
persona/style wrapped (then it's really IN-3), or is the contract "caller owns the prompt, we don't
touch it"?** If the latter, C-CTX-2 (hand the caller our context) is how they opt back into
pawn-consistency voluntarily.

### 3.3 Gating — does a prompt/text mode need a claiming group?
IN-1 requires an **External-domain group to claim the `eventKey`** (policy lives in XML; a stray
submission is harmless). **Decision: do IN-2/3/4/5 also require a claiming group, or a lighter gate
(feature toggle + eligibility + per-pawn generation state only)?** Consistency argues for a group;
friction argues against, since a direct-text injection carries its own tag/title and needs no prompt
policy.

### 3.4 Style set = persistent base change vs. override stack
ST-3 "reset to before override" only has meaning if ST-2 **pushes an override that remembers the
prior base**, not if it overwrites the base. **Decision: model external style-setting as a push/pop
override (ST-5) with provenance (who set it), so reset pops back to the saved base** — vs. a flat
base mutation with no undo. The push/pop model also keeps external style changes from silently
becoming the pawn's permanent saved identity.

### 3.5 Consent granularity
Ties directly back into the v4 §7 toggle rethink. **Decision: one master "allow external
integrations" switch, or per-capability consent** — submit (IN-1) / inject-text (IN-4/5) /
run-prompt (IN-2/3) / write-style (ST-2/3) / context-providers (C-CTX-1) are escalating levels of
trust and may each deserve their own switch. Resolve this once, here, and both the v4 provider toggle
and the injection-mode toggles fall out of it.

### 3.6 Versioning & sequencing
This is ~15 new members; they land across several `ApiVersion` bumps, not one. Proposed order in §4.

---

## 4. Proposed version sequencing

Additive, each independently shippable; ordering by dependency and value. **Not committed — this is
the strawman to react to.**

| Version | Theme | Capabilities | Depends on |
|---|---|---|---|
| **v4** | Pawn-context providers | C-CTX-1 | toggle decision (§3.5) |
| **v5** | Read: prose + filters + by-id | RD-2, RD-3, RD-5, RD-6 (RD-4 tone only if persisted) | — |
| **v6** | Inbound direct-text | IN-4, IN-5, IN-6, IN-7, MT-2, MT-5 | IN-6 handles |
| **v7** | Inbound prompt modes + async | IN-2, IN-3, MT-1, MT-7, C-CTX-2 | MT-1 async (§3.1), v6 handles |
| **v8** | Style write + generation control | ST-2, ST-3, ST-4, ST-5, ST-6 | override-stack decision (§3.4) |
| **later** | Lifecycle polish | MT-3, MT-4, MT-8, ST-7, RD-7 | — |

Rationale: reads (v5) and direct-text (v6) are low-risk and need no new async or LLM machinery;
prompt modes (v7) are gated on the async signal, the single hardest piece; style-writing (v8) waits
on the override-stack decision. C-CTX-1 (v4) is already in flight and independent.

## 5. Design invariants (carry-over — do not relitigate per capability)

- **Additive only.** New members; never rename/remove/repurpose. `ApiVersion` bumps on add;
  consumers feature-detect `>= N`.
- **One entry point.** Everything hangs off `PawnDiary.Integration.PawnDiaryApi`; DTOs are plain
  (strings/value types), no live RimWorld objects cross the boundary.
- **Never throw; main-thread only.** Every entry point wraps, logs once (attributed to `sourceId`),
  and returns a safe empty/false; off-thread calls are rejected, not raced.
- **Save-data tokens are forever.** `eventKey`, style `styleDefName`, and any new handle persisted on
  an entry are defName-class save data — never renamed.
- **Purity boundary holds.** Live reads stay in the impure snapshot phase; only cleaned
  strings/DTOs enter pure code (AGENTS.md barrier).
- **Player consent + localization.** Player-facing text and prompt prose stay Keyed/DefInjected;
  invasive capabilities are gated by explicit consent (§3.5).

## 6. Open questions (consolidated — close these before the matching version's code PR)

1. **Consent granularity (§3.5)** — master vs. per-capability. *Blocks v4 toggle and all injection modes.*
2. **Async delivery (§3.1)** — completion event vs. poll-only. Blast radius traced (Low, §3.1);
   recommendation is a global completion event with best-effort semantics. *Blocks v7.*
3. **Style model (§3.4)** — override stack vs. base mutation. *Blocks v8 (and makes ST-3 possible).*
4. **Full-prompt contract (§3.2)** — wrapped vs. caller-owned. *Shapes v7.*
5. **Injection gating (§3.3)** — claiming group vs. lighter gate. *Shapes v6/v7.*
6. **Tone filter (RD-4)** — persist per-entry tone, or drop the filter.
7. **Custom vs. Def-only styles (ST-2)** — does set accept a free-form `rule`.
8. **Small knobs** — read caps/defaults (RD-2), handle format (IN-6), delete authorization (MT-4).
