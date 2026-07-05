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
- **designed** — has a written brief but no code.

**Design invariants every capability inherits** (from the `INTEGRATIONS.md` stability promise, and
non-negotiable — see §5): additive-only evolution; `PawnDiaryApi` is the one public entry point;
never throw into a caller; main-thread only; plain-string/DTO across the boundary, never live
`Pawn`/`Def`/settings objects into pure code; a shipped `eventKey`/style token is save-data and is
never renamed.

**ID scheme** used below (doc-local, not a code name): `IN-*` inbound, `RD-*` read, `ST-*`
style/persona/generation, `MT-*` meta/lifecycle, `C-CTX-*` context ↔ our machinery (both feed/override
inbound and read-as-a-service outbound — see §2.4/§3.8).

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
| IN-4 | **Create from direct text + tag + title** — no LLM at all | requested | fork the external path before `QueueSolo`; new `SetInjectedText` slot mutator + existing `MarkTitleComplete` | Blast radius **Very Low** (§3.7). Open: generation-toggle collision + group-claim requirement (both policy, code ≈ 0) |
| IN-5 | **Create from direct text + tag, no title** — we generate only the title | requested | as IN-4, then queue only the title step (`QueueTitleRequest`) | Trivial once IN-4 exists |
| IN-6 | **Return a stable entry handle** from every create call | proposed | new: mint + return an id at submit time | Nothing today lets a caller correlate a submission with its result; prerequisite for MT-1/MT-2 |
| IN-7 | **Idempotency key for direct-text** (IN-4/5) | proposed | reuse the event `dedupKey`/`dedupTicks` mechanism for injected entries | Without it, a repeated call duplicates the page |

### 2.2 Read — retrieval & context (`RD-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| RD-1 | **Recent entry titles** for a pawn | **shipped v2** | `GetRecentEntryTitles` → `DiaryEntryTitleSnapshot` (cap 20) | — |
| RD-2 | **Last N entries with prose** (title + first sentence / summary) | requested (= next read API) | reads persisted diary model; `TryGetContextSnapshot` shape in MOD_COMPAT §4.3 | Build on RD-3 filters; expose summaries, not prompts/raw responses |
| RD-3 | **Filters: type, atmosphere, date, POV, archived** | **shipped v5** | `GetRecentEntryTitles(Pawn,int,DiaryEntryTitleQuery)` filters by `decorationDomain` (=type/domain), `atmosphereCue`, `date`/`tick`, `povRole`, active/archived | Source/eventKey filters stay RD-7 |
| RD-4 | **Filter: tone** | requested | `tone` is a *group directive* (`group.tone`/`ToneDirective()`), applied at prompt time, **not stored per entry** | Must persist tone on the entry, or derive it from the entry's group — decide before promising it |
| RD-5 | **Get one entry by id** | proposed | companion to IN-6 handles | — |
| RD-6 | **Cheap counts / stats** (per pawn, per type, per date range) | proposed | tallies without materializing prose | — |
| RD-7 | **Filters: source/eventKey, partner pawn, importance, has-title** | proposed | `sourceId`/`eventKey` stored on external events; partner/POV known; group `important` flag | — |

### 2.3 Writing style / persona / generation control (`ST-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| ST-1 | **Get base writing style** | **shipped v3** | `GetWritingStyle` → `DiaryWritingStyleSnapshot` (`styleDefName`,`label`,`rule`) | — |
| ST-2 | **Set writing style** (free-form rule, highest priority) | requested — **design decided** | new `PawnDiaryRecord.externalStyleOverride {rule, sourceId}` + top-priority branch in the `HediffPersonaOverrides.RuleFor` seam | Free-form rule (not Def-only); overrides even the hediff voice; base untouched. See §3.4 |
| ST-3 | **Reset writing style** (clear the override) | requested — **design decided** | clear `externalStyleOverride` → seam falls through to hediff/base | Nearly free once ST-2's override exists; no bespoke undo. See §3.4 |
| ST-4 | **List available writing-style Defs** | proposed | `DiaryPersonas`/`DefDatabase<DiaryPersonaDef>` | Lets a mod show a picker instead of guessing defNames |
| ST-5 | ~~Temporary (prompt-time) style override — push/pop~~ | **folded into ST-2/§3.4** | — | Superseded: the saved-override-over-base design (§3.4) already gives ST-2/ST-3 their layering; a separate push/pop tier isn't needed |
| ST-6 | **Get/Set per-pawn generation enabled** | proposed | `DiaryGenerationEnabledFor`/`SetDiaryGenerationEnabled` exist internally | — |
| ST-7 | **Expose whole persona vs. style-only** | proposed / open | `PersonaFor` exists; v3 deliberately exposed only the style slice | Decide scope: keep style-only, or open the persona |

### 2.4 Context ↔ our machinery (`C-CTX-*`) — two directions

Our prompt-building machinery is exposed **two ways** (see the design note §3.8): **feed/override** it
(inbound — a mod supplies a piece we fold into our generation) and **read it as a pure service**
(outbound — a mod reads the context we assemble to display it or to drive *another* LLM mod like
RimTalk). Same primitives, both faces.

**Feed / override our machinery (inbound):**

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| C-CTX-1 | **Pawn-context providers** — a mod adds a `key=value` line to our pawn summary | **shipped v4** | `RegisterPawnContextProvider` → line in `BuildPawnSummary` next to DLC-identity fields | Gated by master toggle (§3.5) |
| C-CTX-6 | **Supply / override enchantments** — a mod adds live context candidates to a submitted event's prompt | proposed | `PromptEnchantments.RuleFor(extraCandidates:)` already accepts extra candidates; accept plain lines and wrap them | Relates to IN-3 (partial prompt); sanitize like `extraContext` |

**Read our machinery as a service (outbound — "pure widget" + drive external LLM):**

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| C-CTX-2 | **Get the pawn summary / game context** we'd use | **shipped v6** | `DiaryContextBuilder.BuildPawnSummarySnapshot` → `DiaryPawnSummarySnapshot` (named fields, sharing the same `PawnSummaryFacts` gather as the prompt string) | Export a **structured DTO**, not the raw `key=value` string (§3.8) |
| C-CTX-3 | **Get all prompt-enchantment candidates** for a pawn (the candidate *set*, not the one rolled) | **shipped v6** | `PromptEnchantmentCollector.Collect(pawn, …)` + live event/condition candidates + `PromptEnchantmentPlanner.PrepareCandidatesForBuild` → `DiaryPromptEnchantmentCandidateSnapshot.From` per candidate | Export the post-suppression, post-multiplier pool before the final winner roll; preserve global RNG state (§3.8) |
| C-CTX-4 | **Get the full assembled prompt** (system+user) for a pawn/event — *preview, no submit, no persist* | proposed | `DiaryPromptBuilder.BuildPromptPlan` | A preview is *a* prompt, not *the* prompt (bakes in the enchantment/humor roll) — document as representative (§3.8) |
| C-CTX-5 | **Context bundle** — summary + surroundings + continuity + enchantments + writing style + recent entries, in one snapshot | proposed | composes C-CTX-2/3 + ST-1 + RD-2 | The core-side export the RimTalk bridge feeds to `ContextHookRegistry.RegisterPawnVariable`; **core never references RimTalk** (bridge lives in `integrations/`) |

### 2.5 Meta / safety / lifecycle (`MT-*`)

| ID | Capability | Status | Internal hook it maps to | Key decision |
|---|---|---|---|---|
| MT-0 | **Readiness probe** | **shipped v1** | `IsReady` | — |
| MT-1 | **Async completion signal** (submitted entry finished / failed) | proposed | fire from `ApplyLlmResult` (the existing main-thread completion seam) | The biggest *conceptual* addition, but **blast radius is Low** — no new threading; see §3.1 |
| MT-2 | **Query generation status** of a submitted entry (pending/complete/failed) | proposed | `LlmClient.IsInFlight(eventId, povRole)` already exists | Near-free once IN-6 exposes the handle |
| MT-3 | **Regenerate an existing entry** | proposed | `RegenerateEntry` exists internally | Exposure only |
| MT-4 | **Retract / delete an entry a mod created** | proposed | remove the event/archive row by handle + source check | Undo path for the source mod; guard so a mod only deletes its own |
| MT-5 | **Public `IsDiaryEligible(pawn)`** | proposed | `IsDiaryEligible` exists internally | Saves callers a silent-drop submit |
| MT-6 | **Master consent toggle** | **design decided (A1)** | one `bool allowExternalIntegrations` on `PawnDiarySettings`, honored where each capability runs | Single switch, default on; installing a mod is consent (§3.5). Per-source (A3) deferred, additive |
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

### 3.3 Gating — DECIDED: optional claiming group (B3)
**Decision (2026-07-04): the claiming group is optional for the new inbound modes (IN-2/3/4/5).**
- **Group present** → use it exactly as IN-1 does: its per-group toggle, label, and (for IN-3) its
  prompt policy apply. An adapter that wants XML control ships one.
- **No group** → the call is gated by the master consent switch (§3.5) + subject eligibility + (for
  injection) the generation-off rule; the caller's own tag/title/text stand in for the group's label,
  and no prompt policy is needed (IN-2 owns its prompt, IN-4/5 own their text).

IN-1 keeps its **required-group** rule unchanged (a stray `eventKey` with no group stays harmless).
Trade-off accepted: the no-group path loses the "unclaimed key is silently dropped" safety valve, so
a self-contained IN-2/IN-4 call records even on a typo'd tag — acceptable because such a call is
explicit page-writing, not a fire-and-forget event, and the master switch still gates it.

### 3.4 Style set/reset — DECIDED: saved override on top, reusing the resolve seam
**Decision (2026-07-04):** external style-setting is a **saved override layered over the base**, not a
base mutation — and it reuses the existing persona-override resolution seam rather than inventing a
new one. Two sub-choices are now settled:

1. **Priority: the external override is HIGHEST.** If a mod takes the style into its own hands, it
   **owns** the pawn's voice for as long as the override is set — including shadowing the situational
   hediff override (agony/high/etc.). Pawn Diary does not second-guess it; keeping style coherent is
   the mod's responsibility. Resolution order:

   ```
   external override (saved, ST-2/ST-3, free-form rule)   ← top / wins whenever set
   transient hediff override (computed, unsaved)          ← applies only when no external override
   base persona (saved, player's choice)                  ← bottom fallback
   ```

2. **Form: free-form rule.** ST-2 accepts a **free-form rule string**, not only an existing persona
   Def. The override therefore stores a *rule*, and the seam returns it directly (bypassing the
   `personaDefName → rule` lookup used for base/hediff).

**How it reuses the seam (traced 2026-07-04).** The one resolution point is
`HediffPersonaOverrides.RuleFor(pawn, diary?.personaDefName)` (called from `PersonaRuleFor` at prompt
build); it already does *override-else-base*. The change is to check the saved external override
**first**:

```
RuleFor(pawn, baseDefName):
    if pawn has a saved external override rule  -> return Sanitize(override.rule)   // NEW, top priority
    else                                        -> existing hediff-else-base logic, unchanged
```

- **Storage is new** (the hediff override is stateless/computed and has nothing to persist): add
  `PawnDiaryRecord.externalStyleOverride { rule, sourceId }`, scribed. `diary.personaDefName` (the
  base) is never touched, so **ST-1 `GetWritingStyle` stays base-only and correct**, and **ST-3 reset
  = clear the override**, after which the seam falls straight through to hediff/base with no bespoke
  undo logic.
- **Sanitation:** a free-form rule is untrusted prompt text → clean it like `extraContext`
  (`PromptTextSanitizer.OneLine` + length cap) before it enters the prompt. Localization is the
  caller's (we cannot translate a mod-supplied rule — same rule as `summaryText`).
- **Small interaction to handle:** while an external override is active, the hediff override is
  suppressed, so the enchantment-dedup that keys off `SuppressedPromptHediffDefNamesFor` (which
  exists to avoid repeating a condition already voiced by the *hediff* override) no longer applies —
  don't suppress those enchantments when the external override is on top.

Net cost: one saved field + one top-priority branch in `RuleFor`; the prompt pipeline below is
untouched. ST-3 becomes nearly free. Base-mutation is rejected — it saves nothing here and loses the
reset.

### 3.5 Consent granularity — DECIDED: single master toggle (A1)
**Decision (2026-07-04): one master "allow external integrations" switch, default on.** Rationale:
installing an integration mod *is* the consent — if a player adds a mod that drives the diary, that is
their choice to own. The trust ladder (submit / context / inject / run-prompt / write-style) is
**intentionally flattened**: no per-capability or per-source switches. This also resolves the v4
provider toggle (`API_V4_PAWN_CONTEXT_PROVIDERS.md` §7) — the master switch is that toggle.

- **Scope:** one `bool allowExternalIntegrations` on `PawnDiarySettings`, honored at the point each
  capability runs (submission, injection, prompt, style-write, provider invocation). Off ⇒ every
  external capability is inert; registration/calls simply no-op.
- **Per-source granularity (A3) stays a possible additive follow-up** if a noisy-mod problem ever
  appears — it does not change the contract, so deferring it costs nothing.
- **Generation-off sub-question (from the §3.7 IN-4 trace):** since consent is a single master switch,
  the per-pawn `DiaryGenerationEnabledFor` toggle keeps its own meaning — it is a *player* choice about
  that pawn, not a mod-trust question. Default: **respect it for injection too** (generation-off ⇒ no
  injected pages either), the conservative reading of a player silencing a pawn's diary. (Minor; the
  one micro-decision left in this area — flip to permissive only if a use-case demands it.)

### 3.6 Versioning & sequencing
This is ~15 new members; they land across several `ApiVersion` bumps, not one. Proposed order in §4.

### 3.7 Direct-text injection (IN-4/IN-5) — blast-radius check (traced 2026-07-04) — verdict: **Very Low**
Direct-text reuses the entire external emit path (`SubmitEvent → ExternalEventSignal →
dispatch(Decide, dedup) → Emit → AddSoloEvent`) and **forks only at the final step**: instead of
`QueueSolo` (→ `QueuePrompt` → LLM), it writes the POV slot directly.

- **Free — display:** a slot with `generatedText` + `status=Complete` is exactly the "done" shape the
  tab (`visible = hasGeneratedText`) and the reads (`ViewHasCompletedDiaryPage`) already look for.
  Injected pages surface everywhere native ones do; **no UI/read changes**.
- **Free — styling:** `AddSoloEvent` already stamps `gameContext = "external=…; source=…"`, so the
  External decoration/color-cue derives automatically.
- **Free — save/load (safer than generated):** `NormalizeLoadedMainStatus` returns `Complete`
  whenever `generatedText` is present, so an injected entry reloads Complete with **no orphan-requeue
  risk** — durable the instant it's written.
- **Free — upstream gates:** eligibility (`IsDiaryEligible`), dedup, and the group-enabled toggle run
  in `Decide`/`BuildContext` *before* the fork, so they gate injection with no extra code.
- **New — one small mutator:** there is no "set complete" primitive today (completion only happens in
  the private, LLM-shaped `ApplyLlmResultToSlot`). IN-4 needs `DiaryEvent.SetInjectedText(povRole,
  text)` = set `generatedText` + `status=Complete` + `DiaryStateVersion.Bump()` (mirrors that
  method's success branch). Title: `MarkTitleComplete` already exists (IN-4), or queue the tiny
  title-only call (IN-5).

**Two decisions this trace surfaces (code cost ≈ 0; both already open elsewhere):**
1. **Generation-toggle collision.** `DiaryGenerationEnabledFor` gates *downstream* in `QueuePrompt`,
   which direct-text skips — so the per-pawn "generation off" toggle does **not** block injection
   unless re-checked explicitly. Choose: respect it (conservative), inject regardless (it's not
   AI-generation and spends no tokens), or gate it behind a separate injected-entry consent. Feeds
   §3.5.
2. **Group-claim requirement (§3.3).** IN-4 via this path still requires an External group to claim
   the `eventKey`; for direct text the group is used only for the enabled-toggle and label fallback
   (prompt policy is dead weight). Keep it (free toggle + consistency) or add a lighter no-group path.

Net: IN-4 is a ~1-method save-model addition plus two policy calls, with **no new seams** in UI,
reads, archive, or save/load. Lower plumbing cost than MT-1; its real content is the two policy
decisions above.

### 3.8 Machinery as a service — override vs. pure-widget (two directions)
Our context/prompt machinery (`DiaryContextBuilder.Build*Summary`, `PromptEnchantmentCollector.Collect`,
`DiaryPromptBuilder.BuildPromptPlan`) is reusable in **two directions**, and the API should expose both
deliberately:

- **Feed / override (inbound):** a mod supplies a piece we fold into *our* generation — a summary line
  (C-CTX-1), enchantment candidates (C-CTX-6), a prompt fragment (IN-3), a whole prompt (IN-2), a
  writing style (ST-2). "Take the wheel" on our machinery.
- **Read as a pure service (outbound):** a mod reads the context we assemble, side-effect free, to
  **display it** (a UI widget in another mod) or to **drive a different LLM mod** — feeding RimTalk our
  understanding of the pawn so its conversations reflect the diary (C-CTX-2/3/4/5).

Design rules for the outbound (read) side:
1. **Structured DTOs, not raw strings.** The pawn summary is an internal `key=value` blob; exporting it
   verbatim freezes its format under the additive-only promise. Export **named-field DTOs** so the
   assembly can keep evolving. (Same reason ST-1/RD-* already return DTOs.)
2. **Deterministic inputs over rolled outputs.** Export the enchantment **candidate set** (`Collect`),
   never `PromptEnchantments.RuleFor` — the latter uses `Rand`, so it is unstable per call. The full
   assembled prompt (C-CTX-4) likewise bakes in the roll: it is *a* prompt, not *the* prompt — fine as
   a preview, not as something a consumer should diff.
3. **Side-effect free + main-thread.** These are reads: they must not create a diary record, queue
   generation, or spend tokens (contrast the *generate*-and-return idea, which is IN-2 + MT-1). They
   read live pawn/Def state → main-thread, like every other reader.
4. **RimTalk (and any chat mod) is driven from a bridge, not core.** Core exposes C-CTX-5 as a plain
   bundle; the `integrations/` RimTalk bridge registers it as a Scriban `pawndiary` variable via
   `ContextHookRegistry.RegisterPawnVariable`. Core never references RimTalk types — the AGENTS.md
   barrier is unchanged. This is the concrete core-side counterpart to the MOD_COMPAT §4.3 bridge idea.
5. **Consent:** reads are governed by the master toggle (§3.5) like everything else; they change
   nothing and spend nothing, so no additional gate.

---

## 4. Delivery — one capability at a time, in the base mod

**Decision (2026-07-04): no bundled "version packages". Each capability is implemented and shipped
individually as a feature of the base Pawn Diary mod**, with its own `ApiVersion` bump when it adds a
public member (the ledger is monotonic-per-member, so this is the natural grain). The API surface
lives in core (`PawnDiary.Integration.PawnDiaryApi`), not in adapter/bridge mods — those remain only
as *examples/consumers* under `integrations/`. Ordering below is **dependency order**, not a release
train; pick the next single capability to build, ship it, bump `ApiVersion`, move on.

| Order | Capability | ApiVersion on ship | Depends on | Notes |
|---|---|---|---|---|
| 1 | **C-CTX-1** pawn-context providers | **shipped v4** | §3.5 decided (master toggle) | Implemented via `RegisterPawnContextProvider`; provider lines join the pawn-summary identity block |
| 2 | **RD-3** read filters (type/atmosphere/date/POV/archive) | **shipped v5** | — | Additive `DiaryEntryTitleQuery` overload on recent title snapshots |
| 3 | **RD-2 / RD-5 / RD-6** prose read, by-id, counts | vN | RD-3 | RD-4 tone only if persisted (§6.6) |
| 3b | **C-CTX-2 / C-CTX-3** export pawn summary + collected enchantments (structured DTOs) | **shipped v6** | — | Pure reads (§3.8); cheap, no new machinery |
| 3c | **C-CTX-5** context bundle (+ `integrations/` RimTalk bridge consumer) | vN | C-CTX-2/3, ST-1, RD-2 | Bridge feeds RimTalk; core stays RimTalk-free (§3.8) |
| 4 | **IN-6** entry handle | vN | — | Prereq for status/completion |
| 5 | **IN-4 / IN-5** direct-text inject | vN | IN-6 | Very Low blast radius (§3.7); §3.3 B3, §3.5 gates |
| 6 | **MT-2 / MT-5** status query, eligibility probe | vN | IN-6 | Near-free (§3.1) |
| 7 | **MT-1** async completion signal | vN | — (seam exists) | Low blast radius (§3.1); global event, best-effort |
| 8 | **IN-2 / IN-3 / C-CTX-4 / C-CTX-6** prompt modes, prompt-preview, enchantment override | vN | MT-1, IN-6, §3.2 | C-CTX-4 preview is `BuildPromptPlan`; C-CTX-6 relates to IN-3 |
| 9 | **ST-2 / ST-3 / ST-4 / ST-6** style write, reset, list, generation toggle | vN | §3.4 decided | Override-over-base seam reuse |
| later | **MT-3/4/8, ST-7, RD-7, IN-7** lifecycle polish | vN | — | As needed |

`ApiVersion` numbers past v5 are assigned **in actual ship order**, not reserved up front — so this
list is a build queue, not a version map. Each row is independently shippable and additive.

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

1. ~~**Consent granularity (§3.5)**~~ **DECIDED (§3.5):** single master toggle (A1), default on —
   installing a mod is consent. Also settles the v4 toggle.
2. **Async delivery (§3.1)** — completion event vs. poll-only. Blast radius traced (Low, §3.1);
   recommendation is a global completion event with best-effort semantics. *Blocks v7.*
3. ~~**Style model (§3.4)** — override stack vs. base mutation.~~ **DECIDED (§3.4):** saved override
   over base, reusing the `RuleFor` seam; external override highest priority; free-form rule; ST-3 =
   clear the override. Unblocks v8.
4. **Full-prompt contract (§3.2)** — wrapped vs. caller-owned. *Shapes v7.*
5. ~~**Injection gating (§3.3)**~~ **DECIDED (§3.3):** optional claiming group (B3) — group if
   present, else master toggle + eligibility. IN-1 keeps its required-group rule.
6. **Tone filter (RD-4)** — persist per-entry tone, or drop the filter.
7. ~~**Custom vs. Def-only styles (ST-2)**~~ **DECIDED (§3.4):** free-form rule (sanitized like
   `extraContext`; caller owns localization).
8. **Small knobs** — read caps/defaults (RD-2), handle format (IN-6), delete authorization (MT-4).
