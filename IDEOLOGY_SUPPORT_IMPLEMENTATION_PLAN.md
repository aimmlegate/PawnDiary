# Pawn Diary — Ideoligion Support Implementation Plan

Status: Phases 0–1 and Narrative N3-I are code-complete; Phase 2 infrastructure, exact interaction consumers, and
the exact `IdeoChange` crisis slice are partially implemented; Phases 3–6 remain pending. Guarded
mutation capture/coalescing, exact downstream Ability ownership, existing PlayLog conversion/
reassurance enrichment, and the existing solo crisis page's truthful mutation/current-state context
are live; ritual, broader evidence, remaining exact prompts, and reflection work remains pending.
N3-I closes the master schedule's ordering gap without broadening partial Phase 2; Counsel, rituals,
throne speech, broader enrichment, passive tracking, and reflection scheduling remain deferred.

Scheduling authority: implement Ideology phases only in the waves assigned by
`DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`; this file remains the technical authority for Ideology.

This plan turns Ideology from a one-line pawn-summary fact into event-aware diary material. Its core
feature is a required `EventRelativeStanceResolver`: every enriched entry asks which of this pawn's
live beliefs actually bears on this event before any doctrine reaches the prompt. The plan also covers
the new belief-reflection diary type, richer conversion/crisis/ritual/speech entries, guarded use of
RimWorld's own precept and meme text, certainty-sensitive voice, ideological structures, and named
gods. It is intentionally implementation-level: a coding agent should be able to take one phase at a
time without redesigning the feature.

Cross-DLC delivery follows `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`. Ideology owns
belief capture and stance resolution, but the shared layer owns general lens budgeting, arc
references, and reflection arbitration. Ideology must not become a prerequisite for continuity
between the other DLCs.

The implementation must follow `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md`: keep live
RimWorld reads behind an impure adapter, keep policy pure and tested, put tuning and prompt policy in
XML, update documentation and the changelog with every behavior change, build after every slice, and
remain fully usable without any paid DLC.

## 1. Product outcome

After implementation:

1. A pawn can write a dedicated **belief reflection** about a recent relevant event, a meaningful
   certainty change, or occasionally a doctrine that has been on their mind.
2. Existing crisis-of-belief, conversion, reassurance/counsel, conversion-ritual, throne-speech,
   thought, and social entries can receive concise ideological context only after the
   `EventRelativeStanceResolver` finds a relevant live stance. No match leaves the ordinary entry
   unchanged.
3. The prompt uses the pawn's ideoligion name and RimWorld's localized, possibly modded or
   player-authored text. It never invents a precept that was not selected from the live ideoligion.
4. Strong/relevant precepts and memes are preferred without parsing English words such as
   “Abhorrent,” “Essential,” or “Revered.” Relevance is primary; Def impact is a secondary weight.
5. Certainty changes the *way the pawn reasons*, not whether the underlying event was good or bad:
   high certainty is firm and zealous, middle certainty is assured or uneasy, and very low certainty
   is conflicted or actively doubtful.
6. Ideological structure contributes worldview flavor through its in-game label/description. There
   is no hardcoded English style map such as “AbstractTheist means poetic.”
7. A named god may appear when it is relevant. A deity related to the selected meme is preferred,
   then the ideoligion's key deity; unrelated gods are normally omitted.
8. One gameplay action produces one canonical diary event. A conversion ability that already emits
   a richer PlayLog interaction or completed ritual must not also create a generic ability page.
9. A base-game-only colony sees no errors, settings clutter, empty belief fields, or changed event
   behavior.
10. Future Royalty, Biotech, Anomaly, Odyssey, and modded events can ask the same resolver “what does
    this pawn's faith say about this?” through plain visible facts, guarded labels, and optional
    narrative/topic tags—without adding the new precept's ID to Pawn Diary or creating a second event
    pipeline.

## 2. Scope and non-goals

### Required first release

- Guarded Ideology snapshot collection in `DlcContext`.
- A complete pure `EventRelativeStanceResolver`, with fail-closed knowledge gating, deterministic
  relevance tiers, a normally-one/maximum-two stance result, and bounded formatting.
- Automatic correlation discovery from each live precept's `sourcePrecept`, issue, memes,
  `PreceptComp` thought links, and `HistoryEventDef` links, followed by conservative guarded-text
  similarity only when structural metadata has no answer.
- XML-owned thresholds, field weights, semantic aliases, exclusions, and optional compatibility
  overrides. The normal resolver must not require a hand-authored vanilla/DLC/mod precept-ID catalog.
- Per-POV, event-time persistence of the selected belief context.
- Certainty/ideoligion mutation capture for conversion, reassurance, ritual outcomes, and belief
  crises.
- Enrichment of the existing event routes listed in §9.
- Duplicate suppression for downstream-covered conversion abilities.
- Persisted passive certainty tracking.
- The new `BeliefReflection` capture type, prompt template, XML group, scheduler, and UI-visible event.
- Full pure tests, focused RimTest coverage, save compatibility, localization, documentation, and
  no-DLC verification.

### Deferred unless a later phase explicitly takes them on

- Generating new belief prose for every ordinary thought or social interaction. Those routes only
  receive belief context when relevance is strong enough.
- A hand-authored prose style for every vanilla or modded structure meme. The first release uses the
  structure's guarded in-game text plus generic XML prompt guidance.
- Creating diary pages directly from every `HistoryEvent`. A guarded, non-emitting
  `HistoryEventsManager.RecordEvent` observer is in scope only as short-lived correlation evidence for
  a page authorized by another source; it must never become a broad page generator.
- Maintaining per-mod or per-DLC lists of precept, issue, or meme IDs. Runtime defNames remain valid
  equality/save keys, but shipped hand-authored IDs are exceptional correction overrides only.
- Implementing the wider Royalty/Biotech/Anomaly/Odyssey story-arc roadmap in this feature. This plan
  defines the evidence seam those later slices consume; it does not add growth moments, persona
  weapon bonds, gravship landings, containment breaches, or other new DLC hooks itself.
- Creating generic `IdentityTransitionEventData`, `BondLifecycleEventData`, `JourneyChapterEventData`,
  or `AmbientPressureEventData` capture types. These are cross-cutting meanings attached to existing,
  source-specific events, not substitutes for the event catalog.
- Retrofitting belief context into already-saved historical entries.
- Making Ideology, Royalty, or any other DLC a mod dependency.
- Letting LLM output change certainty, conversion results, precepts, or any game state.

## 3. Definition of done

The feature is done only when all of these are true:

- A dedicated belief-reflection page can be produced from recent evidence, passive certainty change,
  and quiet/random reflection, with cooldown and frequency bounds.
- Every generated belief claim is traceable to a saved `BeliefContext` snapshot taken on the main
  thread when the event was created.
- Conversion success/failure, reassurance, crisis of belief, and conversion-ritual outcomes include
  the correct before/after certainty and ideoligion facts when available.
- Conversion ability → PlayLog and conversion ability → ritual paths produce no duplicate generic
  ability page.
- The same saved event renders the same belief facts after save/load, even if the pawn later converts
  or the ideoligion is reformed.
- Full, balanced, and compact prompt modes behave intentionally; belief context cannot silently crowd
  required event facts out of compact prompts.
- Custom/modded labels, descriptions, ideoligion names, and deity names are sanitized and bounded.
- With Ideology inactive, all collectors and patches cleanly no-op and existing base-game tests pass.
- Old saves baseline current belief state without generating catch-up pages.
- XML, pure tests, focused RimTests, the Debug core build, and the repository verification hook pass.
- A source can submit stable narrative facets and belief-topic tags, and the resolver ignores unknown
  tags, hidden facts, and DLC-defined topics absent from the pawn's live ideoligion.
- A gained thought carrying `Thought.sourcePrecept` resolves that exact live precept without any XML
  ID entry; an arbitrary modded precept using ordinary thought/history precept components does the
  same through automatically projected correlations.
- With all optional exact-ID correction overrides removed, structural correlation and guarded-text
  matching still pass the representative body, meal, raid, ritual, and modded-precept scenarios.
- Lexical matching requires both a configurable confidence threshold and a runner-up margin. Weak or
  ambiguous similarity returns no belief context rather than guessing.
- For every enriched event, the resolver returns zero to two live stances tied to that event; a strong
  but unrelated doctrine never wins merely because of impact, required status, or localized wording.
- The same organ implant, meal, raid, ritual, or conversion evidence can resolve differently for two
  ideoligions, and resolves empty when neither faith has a matching live belief.
- The current body-modification approve/despise behavior is preserved through the shared resolver,
  including despise-over-approve precedence, with no second independently tuned matching path.
- `DOCUMENTATION.md`, `EVENT_PROMPT_MAP.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`, English Keyed and
  DefInjected localization, and the committed DLL all match the shipped behavior.

## 4. Verified current seams and game APIs

These are the implementation anchors already present in the repository and RimWorld 1.6. The coding
agent should re-check them with CodeGraph and the locally installed managed assembly before editing,
but should not redesign around a parallel pipeline.

### 4.1 Pawn Diary seams

- New event sources follow `DiaryEventType` → `XxxEventData` → `XxxEventSpec` →
  `DiaryEventCatalog` → `XxxSignal` → shared dispatch.
- Reflection events already have working examples in `DayReflection`, `QuadrumReflection`, and
  `ArcReflection`.
- `DiaryGameComponent` uses elapsed `next...ScanTick` schedules, not tick modulo checks.
- `FlushDaySummaryForPawn` currently gives arc and quadrum reflections priority over the ordinary
  day reflection.
- `DiaryEvent.PovSlot` is the event-time, saveable home for POV-specific prompt facts.
- `DiaryPipelineAdapters.ToPayload`, `DiaryPovPayload`, `DiaryPromptPlanner.ProjectValues`,
  `PromptValues`, and `PromptAssembler.ResolveSource` are the full path for a new prompt field source.
- Prompt template selection, event-prompt precedence, and prompt-context trimming already live in
  pure pipeline code and XML Defs.
- Conversion interactions already flow through `PlayLog.Add` → `InteractionSignal`; ritual outcomes
  already fan out by participant role; generic ability activations already flow through
  `AbilitySignal`.

### 4.2 RimWorld Ideology APIs to use behind `DlcContext`

| Need | Guarded live source | Notes |
|---|---|---|
| Ideoligion identity | `pawn.ideo.Ideo`, `Ideo.name` | Guard both DLC activation and null trackers. Serialize a stable id separately from display name. |
| Certainty | `pawn.ideo.Certainty` | Clamp defensive snapshots to `[0, 1]`; do not infer certainty from prose. |
| Precepts | `Ideo.PreceptsListForReading` | Select only after snapshotting to plain facts. |
| Precept display | `Precept.TipLabel`, `Precept.Description` | Localized and override-aware. Do not use the large/mechanical `Precept.GetTip()` block. |
| Precept strength hints | `Precept.def.impact`, issue and meme associations | Do not parse localized stance labels for words such as “horrible.” |
| Exact gained-thought belief | `Thought.sourcePrecept` | Strongest possible correlation; the current thought hook already holds the live `Thought_Memory`. Snapshot identity only. |
| Thought correlations | `Precept.def.comps`, `PreceptComp_Thought.thought` | Project Def/stage labels, descriptions, and mood/opinion offset ranges. Standard modded components inherit support. |
| History correlations | public `HistoryEventDef eventDef` fields on precept components | Project through guarded cached field metadata; never invoke arbitrary mod properties. |
| Recent history evidence | `HistoryEventsManager.RecordEvent(HistoryEvent, bool)` | Observe into a bounded non-emitting sidecar only; existing capture sources remain page authority. |
| Memes | `Ideo.memes`, `MemeDef.LabelCap`, `MemeDef.description`, `MemeDef.impact` | Preserve modded/player-localized values. |
| Structure | `Ideo.StructureMeme` | Keep separate from ordinary meme selection. |
| Deities | `IdeoFoundation_Deity.DeitiesListForReading` | Snapshot name, type/gender tokens, related meme, and key-deity status only. |
| Preferred deity | `Ideo.KeyDeityName`, deity `relatedMeme` | Related selected meme first, key deity second. |
| Conversion | `Pawn_IdeoTracker.IdeoConversionAttempt` | Can reduce certainty and call `SetIdeo` when it reaches zero. |
| Reassurance | `Pawn_IdeoTracker.OffsetCertainty` / reassurance path | Snapshot actual before/after values; never predict the result. |
| Ideoligion change | `Pawn_IdeoTracker.SetIdeo` | Coalesce when nested inside a conversion attempt. |
| Passive drift | periodic comparison of `Pawn_IdeoTracker.Certainty` | The normal tracker interval can change certainty without calling `OffsetCertainty`; a scanner is still required. |

All methods above exist in the shared game assembly even without the DLC. That does **not** make an
unguarded runtime read safe. Every live read must pass through the double guard in §6.

## 5. Target architecture

Keep the feature on the established architecture boundary:

```text
Harmony hook / existing signal / scheduled scanner
                 |
                 v
      DlcContext (main-thread, guarded)
                 |
                 v
 plain BeliefSnapshot + projected correlations + guarded source facts
                 |
                 v
 EventRelativeStanceResolver (exact graph -> lexical fallback)
                 |
                 v
       pure formatter -> bounded string
                 |
                 v
     DiaryEvent.PovSlot.beliefContext
                 |
                 v
 pipeline BeliefContext source -> XML prompt template
```

No pure class may reference `Pawn`, `Ideo`, `Precept`, `MemeDef`, `DefDatabase`, `Verse.Rand`, Scribe,
settings singletons, Unity, or an LLM transport. No prompt formatter should re-read a live pawn.

### 5.1 New plain contracts

Add plain classes under `Source/Pipeline/Belief/` (or the repository's nearest existing pure-policy
folder) and file-link them into a standalone pure test project.

`BeliefSnapshot`

- `pawnId`, `capturedTick`.
- `ideologyId`, `ideologyName`, optional `roleName`.
- `certainty` and `hasCertainty`.
- one optional `BeliefMemeFact structure`.
- lists of ordinary `BeliefMemeFact`, `BeliefPreceptFact`, and `BeliefDeityFact`.
- no live Def or pawn references.

`BeliefPreceptFact`

- runtime `instanceId`, `defName`, `issueDefName`, `issueLabel`. The instance id distinguishes
  duplicate-capable precepts; neither identity is a configured catalog entry.
- localized `displayLabel` from `TipLabel` and bounded localized `description`.
- stable `impactRank`; do not store only an enum display string.
- `visible`, `proselytizes`, `requiredByCurrentMeme`.
- associated/required meme identities plus their guarded labels.
- a bounded list of automatically projected `BeliefCorrelationFact` values.

`BeliefCorrelationFact`

- stable kind: `thought`, `history_event`, or `issue`; runtime defName equality key and bounded guarded
  label/description text.
- source component kind/field token for diagnostics, never prompt prose.
- for thoughts, minimum/maximum mood and opinion offsets plus a stable `positive`, `negative`, `mixed`,
  `neutral`, or `unknown` valence token. This is game data, not a guess from words like “abhorrent.”
- no live `PreceptComp`, `ThoughtDef`, `HistoryEventDef`, or reflection object.

`BeliefMemeFact`

- `defName`, localized `label`, bounded localized `description`, `impactRank`.
- `isStructure` so structure cannot accidentally enter the ordinary meme lottery twice.

`BeliefDeityFact`

- guarded `name`, stable type/gender tokens, related meme defName, and `isKeyDeity`.
- no lore invented from the type token.

`BeliefMutationSnapshot`

- pawn id and capture tick.
- before/after ideology ids and guarded display names.
- before/after certainty plus availability flags.
- attempted ideology id/name when supplied by the conversion API.
- booleans for `ideologyChanged`, `certaintyChanged`, and conversion result when known.
- a small stable cause-token set; the consuming event remains the authority on narrative outcome.

`BeliefEventEvidence`

- event id/tick, domain, defName, classifier/group key, and POV role.
- optional runtime `sourcePreceptId` and `sourcePreceptDefName` copied from `Thought.sourcePrecept`;
  these are observed identity/equality keys, not a configured allowlist.
- stable evidence tokens: thought defName, history-event defName, issue/meme identities when the game
  directly supplies them, ritual/ability kind, conversion/reassurance/crisis/speech flags, and
  relevant counterpart mutation.
- bounded guarded match fields: event/Def label, factual subject/object labels, and source context
  phrases. These are data for pure lexical comparison, never instructions and never the assembled LLM
  prompt.
- bounded lists of stable `narrativeFacets` and `beliefTopics`, plus a stable salience token.
- a tri-state per-POV `pawnCanKnow` gate; only explicit true reaches selection, while false/unknown
  evidence is omitted.
- no free-form prompt instruction and no live object.

`BeliefResolutionRequest`

- a `BeliefSnapshot`, `BeliefEventEvidence`, and immutable plain XML-policy snapshot.
- a stable mode token: `event_enrichment` or `quiet_reflection`.
- deterministic seed and bounded recent-selection defNames used only for repetition policy.
- `event_enrichment` fails closed when useful visible evidence is absent; only `quiet_reflection` may
  consider general high-impact doctrine.

`ResolvedBeliefStance`

- the selected live `BeliefPreceptFact` plus any supporting live meme fact.
- matched topic/correlation identity, optional XML override key, and a stable relevance-source token
  such as `source_precept`, `thought_correlation`, `history_correlation`, `issue_identity`,
  `lexical_phrase`, `lexical_tokens`, `meme_association`, or `quiet_fallback`.
- relevance tier, confidence score, and runner-up gap for deterministic diagnostics/tests; none is
  prompt prose.
- correlation valence only when supplied by the actually matched thought/game relation. Generic prompt
  code must not infer approval/disapproval from localized precept text.

`BeliefStanceResolution`

- an ordered list of zero to two `ResolvedBeliefStance` values; ordinary event enrichment normally
  selects one and admits a second only when it is independently relevant and non-redundant.
- selected structure, zero or more supporting ordinary memes, and optional deity.
- certainty band and trend tokens.
- before/after ideoligion identity for mutation events.
- expanded topic tokens and selection-reason tokens for dev diagnostics/tests; reasons do not go to
  the LLM.
- a directly matched live meme may be the sole doctrinal match. Structure or deity flavor alone never
  makes an ordinary event relevant; mutation/certainty facts may still make conversion/crisis context
  useful without a matched precept.

### 5.2 Impure orchestration

Add a small `BeliefContextBuilder` in the generation/adapter layer:

1. Fast-gate on Ideology active, eligible pawn, non-null tracker/Ideo, and relevant evidence.
2. Ask `DlcContext.CaptureBeliefSnapshot(pawn)` for a plain snapshot.
3. Read a plain XML policy snapshot on the main thread.
4. Build a plain `BeliefResolutionRequest` and call `EventRelativeStanceResolver.Resolve`.
5. Call the pure bounded formatter.
6. Return an empty string when there is no selected, useful context.

`AddSoloEvent` and `AddPairwiseEvent` should accept an optional plain `BeliefEventEvidence`; their
event factory snapshots context once for each eligible POV. Callers that do not supply evidence keep
their existing behavior. A lightweight pure evidence classifier may derive obvious hints from
`defName` and `gameContext`, but it must not scan every pawn's full ideoligion for every minor event.

### 5.3 Cross-DLC narrative facet seam

Consume the canonical Narrative Continuity Layer's four primitives as **stable evidence facets**, not
new event types or base classes. Do not redeclare their token constants in the Belief subsystem:

| Facet token | Meaning | Normal event shape |
|---|---|---|
| `identity_transition` | A pawn's body, role, faith, age, or nature permanently changed | Source-specific page plus optional later reflection. |
| `bond_lifecycle` | A meaningful relationship with a pawn, weapon, mech, animal, or other named subject began, changed, or ended | Pair/subject-aware page with continuity. |
| `journey_chapter` | The colony crossed a chapter boundary such as departure, landing, arrival, or endgame choice | Rare colony/crew chapter page. |
| `ambient_pressure` | A persistent condition changes daily life without being a single remembered act | Prompt shading/observed condition; page only on start, escalation, or end when separately justified. |

The facet says *why the event matters*. A separate bounded `beliefTopics` list says *which moral
questions it raises*. Examples include `body_modification`, `organ_use`, `weapons`, `bonding`,
`autonomous_weapons`, `child_labor`, `growth_vat`, `psychic_rituals`, `space_habitat`, `fishing`,
`slavery`, `charity`, `violence`, and `darkness`. These are stable XML-facing schema tokens, not
localized text and not promises that the pawn has a corresponding precept.

`EventRelativeStanceResolver` is Ideology's provider for the shared layer, not a later extension point
or an alias for random doctrine selection. It first uses runtime structure already
loaded by RimWorld: direct `Thought.sourcePrecept`, exact thought/history correlations projected from
the pawn's live precepts, issue identity, and meme relationships. Only when those have no answer does
it compare bounded guarded event text with issue, correlated-thought/history, precept, and meme text.
XML topics and aliases can strengthen or correct evidence, but do not enumerate the candidate
doctrine. This lets new modded and DLC precepts participate when they use normal RimWorld metadata,
without a Pawn Diary update for each new Def.

Do not let these facets decide capture on their own:

- The existing source-specific `XxxEventData.Decide` remains responsible for whether a page exists,
  its shape, deduplication, batching, and fan-out.
- The belief resolver decorates a page already authorized by that source. For ordinary event
  enrichment, its high-confidence formatted result becomes one `interpretation` candidate and
  consumes the shared selector's single interpretation slot. It may also make that saved page
  eligible for a later bounded belief reflection.
- `ambient_pressure` normally feeds the existing observed-condition/prompt-context machinery, not a
  new immediate page.
- A future DLC source may emit more than one facet, such as a child's growth moment being both
  `identity_transition` and `bond_lifecycle` for a parent POV.
- Facts hidden from the pawn are never converted into topics or prompt data. In particular, unknown
  Anomaly infections/downside identities stay unknown until the game visibly reveals them.
- Source adapters set `pawnCanKnow=true` only after constructing the POV from visible/recorded facts.
  The nullable/unknown default intentionally fails closed for new integrations.

Standalone belief-reflection pages still use the full `BeliefStanceResolution` as their primary
source content and do not compete with themselves through the cross-DLC selector. Their scheduling
must enter the shared `ReflectionCoordinator` once Narrative Continuity N4 lands. Until N4, Phase 4
may not add another independent permanent priority chain; it must either land with N4 or remain
deferred.

The existing `BodyModContext.IdeologyStance` is a narrow prototype of this resolver. After parity
tests exist, remove its approve/despise ID lists. Supply the artificial-part event's nearby
history/thought evidence and guarded part/hediff labels to the shared resolver. Exact precept-component
links handle the variants with install-history thoughts; guarded-text matching against issue and
correlated situational-thought text covers a positive variant whose install memory was intentionally
removed from current vanilla data. Map only a high-confidence matched thought's positive/negative
game offset back to the existing `BodyPartEventPolicy` stance token. If evidence is ambiguous, return
`none`; if both reliable polarities appear, existing despise-over-approve precedence remains.

## 6. Guarded collection and text safety

### 6.1 One DLC boundary

Make the existing `DlcContext` static class partial and put the new reads in
`Source/Generation/DlcContext.Ideology.cs`. This remains the one logical home required by
`AGENTS.md`.

Every public/internal collector begins with the equivalent of:

```text
if !ModsConfig.IdeologyActive -> empty snapshot
if pawn == null or pawn.ideo == null or pawn.ideo.Ideo == null -> empty snapshot
```

Also handle Ideology classic mode and partially initialized/load-time pawns defensively. Callers must
be allowed to append the returned empty context unconditionally.

### 6.2 In-game text policy

- Collect translated/game-generated text only on the RimWorld main thread.
- Pass every ideoligion, precept, meme, structure, deity, role, and issue string through the existing
  localized prompt sanitizer before it reaches a payload.
- Run lexical fingerprinting only over that sanitized, bounded main-thread snapshot—not raw Def text,
  `GetTip()`, final prompt instructions, or background-thread translations.
- Collapse line breaks/control characters and cap descriptions at a whole-word boundary.
- Treat player-authored and mod-authored names/descriptions as untrusted data. Place them only in
  structured user-prompt fields, never in the system prompt or XML instruction text.
- Keep free-form names/descriptions out of semicolon-parsed `gameContext`. That string should contain
  stable tokens, booleans, and invariant numbers only. Human-readable before/after names belong in
  the dedicated belief-context block.
- Skip blank, placeholder, hidden, broken, or mechanically structural precepts unless exact live
  correlation or high-confidence guarded-text evidence makes them useful.
- Never call `.Translate()` on an LLM/background thread.

### 6.3 Structure and named gods

Structure is represented as:

- its localized label in all belief reflections;
- a short, sanitized excerpt of its in-game description in full/balanced reflection prompts;
- normally label-only or omitted on ordinary events.

The XML prompt should say that structure may color metaphors, assumptions, and cadence, while all
specific claims must come from supplied facts. Do not infer a fixed style from a defName.

Deity selection order:

1. A deity whose `relatedMeme` matches the highest-ranked selected meme.
2. The ideoligion's key deity.
3. A deterministic weighted deity from the live list only when the event policy permits incidental
   invocation.
4. None.

Ordinary events should often choose none. A missing deity foundation, custom structure without gods,
or blank deity name is normal and produces no field.

## 7. Automatic structural and lexical stance resolution

### 7.1 Runtime correlation projection—no candidate catalog

The impure collector projects relationships already present on each **live** precept. Runtime
defNames are equality/save keys discovered from loaded objects; they are not hand-authored policy.

Use these links, in order:

1. `Thought.sourcePrecept` on the actual gained thought. Pawn Diary's current `ThoughtSignal` already
   holds the `Thought_Memory`, so this gives an exact precept instance with no search or string match.
2. `PreceptDef.issue`, `associatedMemes`, `requiredMemes`, and the ideoligion's
   `GetMemeThatRequiresPrecept` result.
3. Every `PreceptComp_Thought.thought` attached to the live precept, including its guarded Def/stage
   labels, descriptions, and mood/opinion ranges.
4. `HistoryEventDef` links on ordinary precept components. Vanilla uses public `eventDef` fields on
   self-took, knows-memory, unwilling-to-do, development-point, and related components.
5. A cached, defensive projector for modded `PreceptComp` subclasses: inspect public instance fields
   named `thought`/`eventDef` (and bounded collection equivalents) only when their actual value type is
   `ThoughtDef`/`HistoryEventDef`. Cache field metadata by component type; never invoke arbitrary mod
   properties or walk arbitrary object graphs.

The installed 1.6.4871 Def trees demonstrate why this is the primary integration path:

| Pack | Precept nodes | `<eventDef>` references | `<thought>` references |
|---|---:|---:|---:|
| Ideology | 192 | 322 | 395 |
| Biotech | 9 | 5 | 18 |
| Anomaly | 8 | 9 | 11 |
| Odyssey | 6 | 7 | 9 |
| Royalty | 2 | 0 | 2 |

These are raw XML reference counts, not a claim that every reference belongs to a unique precept, but
they confirm that official DLC doctrine already carries extensive machine-readable correlations. Mods
using the same standard components inherit support automatically.

Add a guarded postfix observer on `HistoryEventsManager.RecordEvent` that stores only a bounded,
short-lived plain fact keyed by tick and visible doer/subject/witness pawn IDs. It never creates a
diary event. An existing source may consume a nearby correlation fact when creating its own authorized
page. Early-return before inspecting arguments unless Ideology and belief enrichment are enabled.

### 7.2 Required pure algorithm

`EventRelativeStanceResolver.Resolve(request)` performs these stages in order:

1. **Fail closed.** Return empty for a missing snapshot, inactive/empty ideoligion, or any event whose
   per-POV `pawnCanKnow` is not explicitly true. `event_enrichment` also returns empty without useful
   visible evidence.
2. **Resolve exact source identity.** If evidence contains a runtime source-precept instance id, match
   it first and use defName only as a defensive fallback. Select only a precept present in this pawn's
   snapshot.
3. **Resolve exact game correlations.** Match event `ThoughtDef`/`HistoryEventDef` identities against
   the projected correlation facts. Direct thought and history links outrank every text score.
4. **Resolve direct issue/meme identity.** Use identity only when the source/game actually supplied it;
   never manufacture one from a topic.
5. **Run conservative lexical fallback.** If no structural tier answered—or a separate second issue may
   be justified—compare bounded event fields with correlation, issue, precept, and meme fields under
   §7.3. Optional XML aliases participate here; optional force/exclude overrides are explicit
   diagnostics, not the normal path.
6. **Choose the relevance tier.** `source_precept` outranks exact thought/history correlation; exact
   correlation outranks direct issue identity; identity outranks correlation-text similarity; that
   outranks issue text; issue text outranks general precept/meme text and association. General impact
   is never primary evidence.
7. **Score only eligible candidates.** Apply XML bonuses for impact rank, required-by-current-meme,
   proselytizing relevance, actual mutation semantics, POV role, and salience, then repetition
   penalties. These cannot admit an otherwise unrelated doctrine or cross a categorical tier.
8. **Demand confidence and separation.** A lexical candidate must clear the minimum score and beat the
   runner-up by the configured margin. Otherwise return no lexical result.
9. **Collapse and select.** Collapse duplicate precepts and same-issue variants. Select one stance by
   default; admit a non-redundant second only when a separate visible fact independently supports it
   and it clears the second-slot threshold. Never return more than two.
10. **Attach bounded worldview context.** Select only supporting live memes, then optional structure
    and deity context. The resolver returns facts/diagnostics; the formatter alone produces prompt
    text.

`quiet_reflection` uses the same live snapshot but may enter a separate `quiet_fallback` tier containing
high-impact or meme-required doctrine not recently selected. That tier is forbidden for ordinary event
enrichment.

### 7.3 Guarded-text similarity fallback

Use string matching, but not raw `Contains` and not as the first source of truth. Implement a pure
`BeliefLexicalMatcher` over sanitized plain fields.

Build the event-side fingerprint from:

- thought/history identity labels and descriptions, when present;
- exact event label plus factual subject, object, ingredient, body-part, hediff, weapon, ritual, and
  condition labels supplied by the source;
- CamelCase/underscore-separated event defName tokens;
- stable topic/semantic-alias tokens;
- classifier/domain only at very low weight.

Build each live-belief fingerprint from, highest weight first:

- correlated thought/history Def names, labels, and descriptions;
- issue defName and guarded label;
- precept `TipLabel`, guarded description, and split defName;
- associated/required live meme labels, descriptions, and split defNames.

Normalization and confidence rules:

- strip rich-text tags/control characters, normalize Unicode/case/whitespace, split punctuation and
  CamelCase/underscore boundaries, retain whole-word and short phrase sets, and cap every field;
- weight exact phrases and exact normalized tokens above fuzzy matches;
- suppress tokens appearing across many of the pawn's live beliefs with per-request document
  frequency rather than an English-only hardcoded stopword list;
- a single generic/common token never qualifies; normally require an exact phrase, two distinctive
  tokens, or one sufficiently long token unique to one live issue/correlation;
- permit bounded prefix/character n-gram similarity only for sufficiently long tokens and require a
  higher score/margin. For scripts without whitespace, an XML-enabled character n-gram path may be
  used with the same conservative margin;
- exclude pawn names, ideoligion name, generic schema words, and the final assembled prompt from the
  match corpus;
- if two candidates are close or the only match is generic, return empty.

Text similarity determines **relevance only**. Never parse “Abhorrent,” “Horrible,” “Respected,”
“Required,” “Essential,” “Revered,” or translated equivalents into polarity. Use matched thought mood/
opinion offsets when a mechanical adapter needs valence; otherwise provide the guarded precept text to
the LLM as doctrine. `impactRank` and `requiredByCurrentMeme` remain secondary strength weights.

### 7.4 XML policy and optional corrections

Add a singleton `DiaryBeliefPolicyDef` whose immutable pure snapshot owns:

- feature enabled flag, group availability, certainty bands/phrases, scan/cooldown/chance/cap policy,
  mutation-cache bounds, and structure/deity inclusion policy;
- hard cap two, default-one policy, second-slot threshold, total text/line/description budgets;
- categorical tier scores, within-tier weights, repetition penalty, minimum lexical confidence,
  runner-up margin, phrase/token/unique-token thresholds, fuzzy-match caps, and field weights;
- `eventEvidenceRules` that match existing source/domain/def/group/facet facts and add topics or
  semantic aliases, but do not name candidate precepts;
- localized semantic alias groups where useful (for example equivalent concepts such as prosthetic,
  augmentation, and body modification), using DefInjected text where player-language words are
  involved;
- optional `correlationOverrides` with force/exclude behavior for a proven malformed or metadata-poor
  Def. Every exact precept/issue/meme ID override requires a comment, DLC/mod gate when applicable,
  focused regression test, and a reason why structural/text matching cannot work.

The shipped core policy must work with an **empty override list**. It may contain event vocabulary and
concept aliases, but no required vanilla/DLC precept, issue, or meme catalog. Runtime identities
observed from loaded objects remain safe equality keys and may appear in diagnostics/saves.

Suggested categorical scores for initial tuning are source-precept `1200`, exact thought/history
correlation `1000`, direct issue identity `900`, correlation-text `750`, issue-text `650`, precept/meme
text `450`, association `300`, high/medium/low impact bonuses `220/120/40`, role fit `100`, and recent
repetition `-450`. Tests assert precedence, minimum confidence, and runner-up separation—not a
statistical distribution tied to these provisional values.

### 7.5 Required initial evidence coverage

The first release must make existing captured routes supply factual match fields for these families.
This is an **event evidence catalog**, not a belief-ID catalog; the resolver discovers candidate
doctrine from the pawn's loaded live data.

| Family | Event evidence | Constraint |
|---|---|---|
| Belief change and authority | actual conversion/reassurance/crisis mutation, roles, speech/ritual labels | A title alone does not imply doctrine. |
| Body and medicine | exact operation, body-part, implant/organ/hediff, and nearby thought/history facts | Preserve body-mod behavior without configured precept IDs. |
| Food | known ingredient/meal labels and directly caused thought | Never infer ingredients from a generic meal. |
| Conflict and captivity | exact combat, raid, weapon, prisoner, execution, slavery, or outcome facts | A raid alone does not imply execution or slavery. |
| Aid and outsiders | visible aid/refusal/refugee/beggar/rescue facts | Outsiders in a raid do not automatically imply charity. |
| Work and environment | exact darkness/light/tree/mining thought, condition, work, or tale facts | Ambient state alone does not force a page. |
| Ritual and role | exact ritual family, label, participant role, and visible outcome | Generic ritual quality is not a moral outcome. |

A newly loaded DLC or mod issue can therefore resolve by direct thought/history metadata or guarded
text even if Pawn Diary has never seen its IDs. Phase 5 may add semantic aliases or exceptional
corrections discovered in playtests, but cannot replace this automatic path with manual lists.

### 7.6 Determinism and diversity

- Derive a local seed from event id (or tick + defName), pawn id, POV role, and selection category.
- Never consume `Verse.Rand`; selection must not perturb gameplay randomness.
- Sort normalized fields and candidates before scoring/tie-breaking; dictionaries, Def iteration, and
  reflection field order must not change output.
- Perform deterministic weighted selection without replacement only among candidates admitted by the
  strongest eligible relevance tier.
- Persist a short list of recently selected precept/meme runtime identities on `PawnBeliefState` and
  penalize them; direct relevance may override the penalty.
- Persist the formatted context at event creation rather than re-resolving on render.

## 8. Certainty model and narrative contract

Certainty is a fact and voice constraint, not a mood score. Put band boundaries and model-facing
phrases in XML/DefInjected text. Suggested initial bands for playtesting:

| Certainty | Stable token | Intended reasoning voice |
|---|---|---|
| `>= 0.85` | `fervent` | Unshakable, zealous, morally certain; may sound fanatical when the event is doctrinally charged. |
| `>= 0.60` | `confident` | Assured and committed without automatic extremism. |
| `>= 0.35` | `uneasy` | Still identifies with the faith but notices tensions and exceptions. |
| `>= 0.15` | `conflicted` | Openly struggles between doctrine, experience, and alternatives. |
| `< 0.15` | `doubtful` | Actively questions the faith and may contemplate abandoning it. |

Rules for the prompt and formatter:

- Include both the invariant percentage and the stable band phrase.
- Include trend only when a saved before/after comparison exists: rising, falling, or stable, plus a
  qualitative magnitude derived from XML thresholds.
- High certainty does not make every event positive. A fervent pawn confronting a violated precept
  should condemn it more firmly.
- Low certainty does not force sadness. It produces hesitation, contradiction, rationalization, or
  curiosity appropriate to the event.
- After actual conversion, the current ideoligion and reset certainty are facts; the prompt may show
  the old and new names but must not call a failed attempt a conversion.
- A crisis entry may discuss the candidate/attempted ideoligion only if the game supplied it.

## 9. Event behavior matrix

| Route | Canonical event | Belief facts | Required behavior |
|---|---|---|---|
| Quiet/recent reflection | New `PawnBeliefReflection` solo event | Current ideology, certainty/band/trend, selected doctrine, optional structure/deity, recent evidence | Direct first-person reflection; no invented external event. |
| Crisis of belief (`IdeoChange`) | Existing mental-state event | Before/after certainty, attempted/current ideology, relevant apostasy/diversity doctrine | Dedicated group/prompt before generic mental-break catchall. |
| Random conversion conversation | Existing pair PlayLog interaction | Converter and target contexts, target before/after certainty, actual success flag | Two POVs; exact success/failure wording; no generic fallback battle-of-beliefs prose when facts are known. |
| Convert ability | Existing `Convert_Success`/`Convert_Failure` pair PlayLog | Same as conversion interaction | Suppress generic Ability event covered by the later PlayLog row. |
| Reassure/counsel | Existing pair interaction | Same-faith context and actual restored certainty | Support/reassurance language, never claim conversion. Move `Reassure` out of generic heartfelt handling. |
| Conversion ritual | Existing ritual fan-out | Target mutation, organizer/target/participant role, relevant memes/precepts | Dedicated conversion ritual group before generic ritual; role-specific context; one event per existing fan-out policy. |
| Conversion ritual ability | Completed ritual event | Activation may identify pending ritual only | Suppress generic activation page when the downstream ritual is canonical. |
| Throne speech | Existing Royal ritual/speech event | Speaker role/certainty plus authority-relevant doctrine when found; limited witness context | Royal authority remains primary. Do not force religion when the resolver finds no relevance. |
| Thought with direct precept link | Existing thought route | Matching precept/meme and certainty | Enrich existing page only; no second immediate reflection. |
| Social event with exact XML relevance | Existing interaction route | Matching issue/meme and POV-specific certainty | Enrich only above threshold; ordinary chitchat stays unchanged. |
| Body/medical event with exact operation or body-part facts | Existing body-part/health route | `body_modification`, `organ_use`, scarification, or blinding stance when live | Shared resolver preserves existing capture/attitude policy; no generic medical inference. |
| Food event with known ingredient or direct thought | Existing thought/food-capable route | Cannibalism, meat, insect meat, fungus, or paste stance when live | Unknown ingredients produce no food topic; unrelated doctrine is excluded before weighting. |
| Combat, raid, prisoner, or aid event with an exact visible outcome | Existing tale/interaction/event route | Only supported violence, raiding, honorable combat, execution, slavery, prisoner, charity, or outsider issues | Broad “raid” or “outsider” labels do not imply every adjacent moral issue. |
| Darkness/light/work condition with direct source facts | Existing thought/tale/observed-condition route | Matching darkness, light, tree-cutting, or mining issue | Context shading only unless the existing source already authorizes a page. |
| Passive certainty drift | Scheduled state comparison | Accumulated before/after certainty and trend | Becomes pending reflection evidence; never an immediate page per tiny tick. |
| Ideoligion changed without a caught source | Scheduled state comparison | Old/current identity when known | One pending reflection, not a fabricated conversion event. |

### 9.1 Future DLC events as belief-resolver clients

These rows validate the seam but do not expand the required capture scope of this plan:

| Later source | Facet/topic evidence | Ideology behavior |
|---|---|---|
| Royalty persona-weapon bond or consequential kill | `bond_lifecycle`; `weapons`, `bonding`, `violence` | Select an actual weapon/bonding stance when present; otherwise use only Royalty/persona facts. |
| Biotech growth moment | `identity_transition`, parent POV may add `bond_lifecycle`; `child_labor`, `growth_vat` when source facts make them relevant | Child and parent can interpret the same milestone differently; no doctrine is forced merely because Biotech is active. |
| Biotech xenogerm/mechanitor milestone | `identity_transition` or `bond_lifecycle`; `body_modification`, `autonomous_weapons` | Prefer live Flesh Purity/Transhumanist/autonomous-weapon doctrine and preserve event-specific gene/mech facts outside the belief block. |
| Anomaly psychic ritual | `psychic_rituals` plus ritual-specific topics; `identity_transition` only when the visible result truly changes identity/body/age | The existing ritual page gains the pawn's actual exalted/disapproving/abhorrent stance without adding another page. |
| Anomaly monolith level/outcome | `journey_chapter`; `psychic_rituals`, knowledge/temptation topics supported by visible facts | Treat the rare progression boundary as a chapter; never reveal hidden entity/host/downside state. |
| Odyssey landing or first orbit | `journey_chapter`; `space_habitat` and only landscape/activity topics supported by source facts | The journey page may become ideological when the pawn actually has a relevant precept; an unrelated faith stays silent. |
| Pollution, flood, vacuum, or occult pressure | `ambient_pressure`; source-specific topic | Usually shade ordinary entries. Only the observed-condition/event-window policy may authorize bounded transition pages. |

## 10. Existing-event enrichment and mutation capture

### 10.1 Transient mutation cache

Add guarded Harmony hooks for the exact target RimWorld 1.6 signatures of:

- `Pawn_IdeoTracker.IdeoConversionAttempt`;
- `Pawn_IdeoTracker.OffsetCertainty`;
- `Pawn_IdeoTracker.SetIdeo`.

Register signature-sensitive targets through `DiaryPatchRegistrar` with null checks and one warning,
rather than assuming an overload forever. Every prefix snapshots plain before-state; every postfix
snapshots actual after-state and calls a bounded `BeliefMutationCache.RecordOrMerge`.

The cache is keyed by pawn id and tick/window. Merging keeps the earliest before-state, latest
after-state, attempted ideology, and union of cause tokens. This coalesces the nested
`IdeoConversionAttempt` → `SetIdeo` path. Consumers **peek** rather than consume, because the same
outcome may be needed by two POVs or ritual fan-out entries. Expire by XML lifetime, enforce a hard
cap, and clear it in the same session reset/finalization paths as other transient caches.

The patches must not become a second raw DLC-reading boundary. They pass `Pawn_IdeoTracker` and any
attempted `Ideo` argument to guarded `DlcContext` helpers, which resolve the owning pawn and return a
plain mutation state. Any private-field reflection needed to locate the pawn is cached and contained
inside that adapter.

Do not record setup/load noise. Require a playing game, Ideology active, a valid pawn id, a meaningful
before-state, and an actual change/attempt. The scheduled scanner remains authoritative for passive
certainty drift that bypasses these methods.

### 10.2 Interaction path

> **First consumer implemented (2026-07-21).** Exact XML rows now map `ConvertIdeoAttempt`,
> `Convert_Success`, `Convert_Failure`, and `Reassure` to the recipient mutation and validate result,
> direction, ideology-change shape, cause, effective group, exact pawn, and one-way tick age. The
> existing saved belief block carries the stable mechanical fields for both authorized POVs and labels
> cross-POV target mechanics explicitly. Exact conversions append only `belief_event=conversion` to
> `gameContext` for prompt priority; numerical facts are not duplicated. Counsel and exact event prompt
> Defs remain pending.

Before `InteractionSignal` calls `AddSoloEvent`/`AddPairwiseEvent`:

- classify conversion success, conversion failure, conversion attempt, reassurance, counsel, and
  related known defNames through XML strings;
- peek the target pawn's recent mutation;
- append a stable event-class marker such as `belief_event=conversion` to `gameContext` so prompt
  selection can prioritize the typed block;
- pass a `BeliefEventEvidence` containing the mutation, its visible subject, and each POV role to event
  creation; keep certainty/identity/result numbers in that typed evidence and saved belief block.

Add exact `DiaryEventPromptDef` entries for success, failure, attempt, and reassurance/counsel so an
exact defName beats the broad conversion group. The event's saved game text remains the factual
fallback.

### 10.3 Ability path and duplicate suppression

Add an XML-owned “covered by downstream event” classification for vanilla `Convert` and `Reassure`
ability defNames. Feed a plain boolean/reason token into `AbilityEventData.Decide`
so the pure policy also locks the drop contract. Because `AbilitySignal.Payload` currently draws the
lazy `Rand.Value` before `Decide` runs, its getter must explicitly skip that draw when the payload is
downstream-covered. This preserves both the no-page result and RimWorld's gameplay RNG state.

`ConversionRitual` must retain its generic activation page until a cancel-aware pending/fallback route
exists: vanilla exposes no cancellation event, so suppressing activation immediately could lose the
only page when the ritual never completes. Its completed ritual remains future enrichment work.

Do not globally drop every ability with “Convert” in its name. A modded conversion ability that emits
neither a PlayLog interaction nor a ritual should retain the existing generic ability route and may
receive belief evidence from its guarded label/context, mutation facts, or an optional event-evidence
alias rule; it does not require a matching precept-ID entry.

The focused Convert/Reassure integration tests must prove the ordering: activation generates zero
generic Ability events and exactly one canonical downstream interaction event. A future ritual-owner
test must require the same only after cancellation-safe pending/fallback behavior exists.

### 10.4 Conversion ritual and throne speech

- Add a dedicated conversion ritual group matching exact ritual/behavior-worker strings and place it
  before the ritual catchall.
- Extend ritual context construction with stable belief outcome markers and pass role-specific plain
  evidence to every existing fan-out signal.
- Target POV gets before/after identity and certainty. Organizer/converter POV gets proselytizing,
  moral-guide, or role-relevant doctrine. Other participants get a smaller context budget.
- Keep existing ritual quality/outcome facts authoritative. “Masterful” is not itself proof of
  conversion unless the mutation snapshot says the ideoligion changed.
- Keep throne speech in its existing Royal group. Supply an `authority_speech` hint; let XML relevance
  choose leader/authority/slavery/etc. doctrine if actually present. Structure and deity are flavor,
  not mandatory facts.

### 10.5 Crisis of belief

> **Implemented (2026-07-22) as the smallest next Phase-2 slice.** The existing exact `IdeoChange`
> solo mental-state page now resolves the Ideology-gated `beliefCrisis` group before the generic
> catchall, peeks the matching breaking-pawn mutation, freezes observed changed/unchanged mechanics,
> and appends `belief_event=crisis`. Missing or rejected mutation evidence carries only a typed request
> for guarded current identity/certainty; no old ideology is reconstructed. The exact localized
> `DiaryEventPrompt_IdeoChange` wins by normal prompt-key precedence. No new page, patch target, save field,
> polling path, or gameplay RNG draw was added. The first loaded run confirmed vanilla's outer
> lifecycle recursively starts a silent `Wander_OwnRoom`/`Wander_Sad` in `PostStart`; the existing
> mental-state hook now treats that exact current/requested/silent signature as the companion so the
> crisis contract truly remains one page. The prefix and optional evidence adapter fail open, dormant
> package rows yield to the live fallback classifier, and model-facing prose stays neutral for secular
> ideoligions.

- Add an exact mental-state group for `IdeoChange` before the generic mental-state catchall.
- Peek the mutation created during `MentalState_IdeoChange.PreStart` and attach it to the event.
- If the attempt did not change ideology, say certainty fell or convictions were challenged, not that the
  pawn converted.
- If no mutation is available, use current ideology/certainty and the mental-state facts only. Never
  infer an old ideology from the current one.

## 11. New belief-reflection event

### 11.1 Capture contract

Add:

- `DiaryEventType.BeliefReflection`;
- `BeliefReflectionEventData` with `DefNameToken = "PawnBeliefReflection"`;
- `BeliefReflectionEventSpec` registered in `DiaryEventCatalog`;
- `BeliefReflectionSignal` in the Reflection domain;
- `DiaryEvent.IsBeliefReflection()` and pipeline payload/template routing;
- XML interaction group, event prompt, and `DiaryPromptTemplate_SoloBeliefReflection`.

The plain event data should contain only the facts required for pure decision:

- eligible/user/group/signal gates from `CaptureContext`;
- trigger token: `recent_event`, `certainty_shift`, `ideology_change`, or `quiet`;
- number of usable evidence items;
- whether a valid belief context exists;
- before/after certainty and meaningful-delta flags;
- whether a reflection was already written for this source/cooldown window.

`Decide` drops when Ideology context is absent, the feature/group is disabled, evidence is unusable,
or cooldown/frequency policy rejects it. It emits one solo event otherwise.

### 11.2 Reflection evidence selection

At rest/sleep flush, choose one trigger in this priority:

1. Unreflected ideoligion change or major certainty shift.
2. A recent saved diary event for the pawn with strong saved belief relevance.
3. Accumulated meaningful passive certainty drift.
4. A deterministic quiet-reflection roll selecting a high-impact or role-relevant doctrine.

Recent-event evidence must come from bounded, already-saved diary data; do not keep live `Pawn`/Def
references or an unbounded shadow event history. Track the chosen source event id so the same event is
not repeatedly reflected on. An event can be revisited only if XML policy explicitly permits it after
a long gap.

For the first release, scan only `DiaryEventRepository.MostRecentEvents(policy.maxRecentEventsScanned)`
and require a non-empty saved POV belief context plus a stable relevance marker. Do not duplicate the
full belief block into `ArchivedDiaryEntry`: once an event has crossed normal active-event retention,
it is no longer “recent” evidence. Long-term apostasy/conversion memories belong to the later arc
extension in §20.

### 11.3 Scheduler and priority

Add elapsed scheduling fields and a scan partial beside existing progression scanners. Spread pawn
work across scans and avoid per-tick full-ideology enumeration.

Insert `TryFlushBeliefReflectionForPawn` into the existing rest/day flush order:

```text
Arc reflection -> Quadrum reflection -> Belief reflection -> ordinary Day reflection
```

Return after the first successful reflective event so one rest produces at most one reflection page.
Immediate conversion/crisis/ritual pages do not also create an immediate belief reflection; they may
become delayed “recent event” material only if policy allows and they have not already exhausted the
frequency budget.

When the feature/group is disabled, continue baselining current ideology/certainty but clear or age
out pending reflection debt. Re-enabling must not dump months of catch-up pages.

### 11.4 Context schema and prompt contract

Keep stable machine facts in `gameContext`, for example:

```text
belief_reflection=true; belief_trigger=certainty_shift; certainty_before=0.42;
certainty_after=0.19; certainty_trend=falling; belief_source_event=<stable-id>
```

Add a dedicated multi-line `BeliefContext` prompt source for human-readable facts. A representative
shape is:

```text
ideoligion: The Quiet Flame
certainty: 19% (conflicted; falling sharply)
structure: Abstract theist
structure outlook: <short guarded in-game description>
relevant precept: Apostasy: abhorrent
precept meaning: <short guarded in-game description>
relevant meme: Proselytizer
deity: Auralis (key deity; related to Proselytizer)
```

Labels are stable prompt schema and may remain English under the documented localization carve-out;
all prompt guidance and descriptive values follow Keyed/DefInjected localization rules.

The reflection template must instruct the model to:

- write a private first-person diary reflection, not dialogue or a sermon unless the event was one;
- use the exact ideoligion/deity names supplied;
- discuss only supplied precepts/memes and the supplied event evidence;
- let certainty determine conviction versus doubt;
- use structure as flavor, never as permission to invent doctrine;
- avoid listing raw fields or explaining game mechanics/percentages verbatim unless natural;
- avoid asserting a conversion, divine command, or moral stance absent from the facts.

## 12. Persistence and save compatibility

### 12.1 Per-POV event snapshot

Add `beliefContext` to `DiaryEvent.PovSlot` and Scribe it as:

- `initiatorBeliefContext`;
- `recipientBeliefContext`.

Expose the same data through the event's convenience properties, `DiaryPovPayload`,
`DiaryPipelineAdapters.PovFor`, `PromptValues`, `DiaryPromptPlanner.ProjectValues`, and
`PromptAssembler.ResolveSource("BeliefContext")`.

Old saves default these strings to empty. Normalize null to empty after load. Do not regenerate belief
context from the pawn at prompt-render time; that would rewrite history after a conversion or reform.

Update prompt-context selection:

- `BeliefContext` is required for `SoloBeliefReflection`.
- It receives high optional priority for conversion, belief crisis, and conversion ritual markers.
- It receives medium/low priority for ordinary relevant thought/social events.
- Persist one essential-first, stable-keyed block. In `ProjectValues`, call a pure
  `BeliefContextFormatter.ForDetail(savedBlock, request.contextDetailLevel)`: Full keeps the bounded
  block, Balanced drops low-ranked description lines as needed, and Compact keeps identity,
  certainty/trend, and at most the top doctrine/deity fact. This transforms only saved facts and never
  re-reads live game state or truncates an arbitrary line.
- Mirror the new source in the prompt-lab/Node harness and golden tests.

### 12.2 Per-pawn belief state

Add `PawnBeliefState` to `PawnDiaryRecord`, Scribed as a deep object with safe defaults:

- `baselineOnNextScan = true`;
- last observed ideology id/name and certainty;
- last scan tick;
- accumulated pending certainty before/after and first/last tick;
- pending ideology-change identity when known;
- last belief-reflection tick/day/quadrum and count;
- last reflected source event id(s), bounded;
- recently selected precept/meme defNames, bounded.

Normalization rules:

- clamp certainty and counts;
- remove blank/duplicate ids and cap all lists;
- reset impossible future ticks after load;
- first scan on an old save records a baseline and emits nothing;
- a missing DLC/tracker clears transient pending evidence but does not damage the rest of the diary;
- if the pawn later regains a valid tracker, baseline again before detecting change.

Mark reflection state consumed immediately after a `DiaryEvent` is successfully created, not after
LLM success. The existing generation retry path owns failed requests; the scanner must not create a
second event for the same evidence.

## 13. XML, prompt, settings, and localization work

### 13.1 Defs

Add or extend:

- `DiaryBeliefPolicyDef` XML with all thresholds, caps, tier/field weights, lexical confidence/margin,
  chances, phrases, event-evidence aliases, and optional empty-by-default corrections;
- Reflection group for `PawnBeliefReflection`, gated with
  `MayRequire="Ludeon.RimWorld.Ideology"` where the Def itself is DLC-specific;
- exact conversion/reassurance/crisis prompt policies;
- dedicated conversion ritual classifier before ritual catchall;
- downstream-covered ability strings;
- `DiaryPromptTemplate_SoloBeliefReflection`;
- a `BeliefContext` field appended to applicable first-person templates.

Append new prompt fields rather than inserting into existing XML lists when possible, because
DefInjected list-index paths are translation-sensitive. Update all English DefInjected paths to
match final indexes.

### 13.2 Settings

The new reflection group should use the existing group-toggle UI. Do not add a broad top-level setting
unless playtesting proves the group toggle insufficient. If the XML group is DLC-gated, a no-DLC
player should not see a dead setting row.

Developer diagnostics may display:

- selected certainty band;
- structural/lexical tier, normalized diagnostic token hashes or dev-safe tokens, candidate scores,
  confidence threshold, and runner-up gap;
- selected/omitted precepts, memes, structure, deity;
- trigger, cooldown decision, and source event id;
- mutation/history-evidence cache match/age and any optional correction key.

Diagnostics must never become model prompt text or write full player/mod-authored descriptions to the
log.

### 13.3 Localization

- UI labels, fallback event prose, and any Keyed prompt fragments go in
  `Languages/English/Keyed/PawnDiary.xml`.
- Def `label`, `instruction`, `rule`, and system/final prompt text use DefInjected localization.
- Stable schema keys (`belief_reflection=`, `certainty_before=`, field labels) and sentinel tokens may
  remain English as documented.
- Never hardcode English certainty prose or structure guidance in C#.
- Use placeholders for ideoligion, deity, pawn, and event names; do not concatenate English grammar.

## 14. File-level change map

Names may be adjusted to local conventions, but responsibilities must not be merged across the
impure/pure boundary.

### New production files

| Proposed file | Responsibility |
|---|---|
| `Source/Generation/DlcContext.Ideology.cs` | Guarded main-thread snapshot plus cached projection of live issue/meme/component thought/history correlations. |
| `Source/Generation/BeliefContextBuilder.cs` | Impure orchestration: gate, snapshot, policy snapshot, pure resolve/format. |
| `Source/Pipeline/Belief/BeliefContracts.cs` | Plain snapshot, evidence, mutation, request/resolution, policy DTOs, and stable narrative-facet/topic tokens. |
| `Source/Pipeline/Belief/EventRelativeStanceResolver.cs` | Pure structural-correlation tiers, confidence gates, deduplication, and deterministic 0–2 stance selection. |
| `Source/Pipeline/Belief/BeliefLexicalMatcher.cs` | Pure Unicode/token/phrase/fuzzy fingerprinting, document-frequency suppression, confidence score, and runner-up margin. |
| `Source/Pipeline/Belief/BeliefContextFormatter.cs` | Pure full/balanced/compact bounded blocks. |
| `Source/Pipeline/Belief/BeliefReflectionPolicy.cs` | Pure baseline, trigger, cooldown, priority, and consumption decisions. |
| `Source/Models/PawnBeliefState.cs` | Saved per-pawn observation/reflection state and normalization. |
| `Source/Capture/Events/BeliefReflectionEventData.cs` | Plain decision payload and context formatting for the new source. |
| `Source/Capture/Specs/BeliefReflectionEventSpec.cs` | Catalog spec. |
| `Source/Ingestion/Sources/BeliefReflectionSignal.cs` | Main-thread signal and solo emit. |
| `Source/Core/DiaryGameComponent.Belief.cs` | Elapsed scanner, state updates, evidence lookup, rest flush. |
| `Source/Patches/DiaryIdeologySignalPatches.cs` | Guarded certainty/ideology mutation hooks. |
| `Source/Generation/BeliefMutationCache.cs` | Bounded/coalescing transient mutation snapshots. |
| `Source/Generation/BeliefHistoryCorrelationCache.cs` | Runtime wrapper for the non-emitting bounded per-pawn/tick plain history-event correlation sidecar. |
| `Source/Pipeline/Belief/BeliefHistoryCorrelationBuffer.cs` | Pure bounded/stale-pruned exact-pawn HistoryEvent storage. |
| `Source/Pipeline/Belief/BeliefEventEvidenceFactory.cs` | Pure bounded constructors/copies for explicit source evidence and developer fixtures. |
| `Source/Defs/DiaryBeliefPolicyDef.cs` | XML schema, singleton resolution, defensive fallbacks, plain snapshot conversion. |
| `1.6/Defs/DiaryBeliefPolicyDef.xml` | Runtime tuning and relevance policy. |
| `tests/BeliefContextTests/` | Standalone pure test executable/project. |

### Existing production files to modify

| Area | Expected edits |
|---|---|
| `Source/Generation/DlcContext.cs` | Make partial; retain all existing guards/behavior. |
| `Source/Capture/DiaryEventType.cs` | Add `BeliefReflection`. |
| `Source/Capture/Catalog/DiaryEventCatalog.cs` | Register its spec. |
| `Source/Models/DiaryEvent.cs` | POV belief field, Scribe, normalization, reflection predicate. |
| `Source/Models/PawnDiaryRecord.cs` and progression/state partials | Own and Scribe `PawnBeliefState`. |
| `Source/Core/DiaryGameComponent.cs` | Elapsed next-scan field/reset/tick call. |
| day/reflection flush partial | Insert belief reflection in the priority chain. |
| event factory/add-event partials | Optional plain evidence and per-POV event-time snapshot. |
| `Source/Generation/BodyModContext.cs` | Remove precept-ID lists; delegate to shared structural/text resolution and map reliable correlation valence to existing stance tokens. |
| `Source/Ingestion/Sources/ThoughtSignal.cs` | Snapshot direct `Thought.sourcePrecept` identity and guarded thought match fields. |
| `Source/Ingestion/Sources/InteractionSignal.cs` | Mutation match and role-aware conversion/reassurance evidence. |
| `Source/Ingestion/Sources/AbilitySignal.cs` and `AbilityEventData.cs` | Pure downstream-covered drop before random roll. |
| existing thought/body/tale/condition/event sources | Supply only exact visible §7.5 match fields their current payload actually knows; do not create a generic belief capture path. |
| ritual/fan-out signal files | Mutation and role-aware evidence. |
| mental-state signal path | Exact belief-crisis evidence. |
| `Source/Pipeline/DiaryPipelineContracts.cs` | POV belief string and reflection flag/template constant. |
| `Source/Generation/DiaryPipelineAdapters.cs` | Map saved values, flag, event noun/domain/fallback. |
| `Source/Pipeline/DiaryPromptPlanner.cs` | Project `BeliefContext` and select reflection template. |
| `Source/Generation/PromptAssembler.cs` | Add source/value mapping. |
| `Source/Pipeline/PromptContextDetail.cs` | Required/priority behavior for the new source. |
| domain/classifier helpers | Recognize `belief_reflection=` and exact belief subgroups. |
| patch registration/session reset | Register the guarded history observer and mutation hooks; clear both bounded caches. |
| prompt preview/dev tools | Synthetic belief event and visible context diagnostics. |
| `1.6/Defs/DiaryInteractionGroupDefs.xml` | New Reflection/conversion/crisis/ritual classifiers and downstream-covered ability strings. |
| `1.6/Defs/DiaryEventPromptDefs.xml` | Exact belief-event prompt policies. |
| `1.6/Defs/DiaryPromptTemplateDefs.xml` | New reflection template and appended `BeliefContext` fields. |
| `1.6/Defs/DiaryTuningDef.xml` / signal policy XML | Remove legacy body-mod precept lists; retain only cross-feature scheduling/signal fields that belong here. |
| English Keyed/DefInjected XML | All player/model-facing text. |

### Tests and documentation to modify

- `tests/DiaryCapturePolicyTests/Program.cs` — catalog and Decide/drop behavior.
- `tests/DiaryPipelineTests/Program.cs` — payload, domain, key precedence, template, source rendering,
  context-detail modes, and old-empty behavior.
- `tests/PawnDiary.RimTest/` — focused live snapshot, body-mod parity, resolver, mutation, and
  end-to-end flow cases.
- `DOCUMENTATION.md` — file map, event flow, prompt context, persistence, settings, DLC safety, and
  localization.
- `EVENT_PROMPT_MAP.md` — new template/source and exact event-prompt precedence.
- `TEST_COVERAGE_PLAN.md` — add belief reflection and enriched conversion assertions to the matrix.
- `CHANGELOG.md` — dated entry for every implemented phase.

## 15. Phased implementation sequence

Each phase is a reviewable, buildable slice. The coding agent must use the phase loop from the
engineering skill: inspect with CodeGraph, state the smallest plan, implement, run focused tests,
update docs/changelog, build, inspect diff, and stop if unrelated user changes overlap.

### Phase 0 — Complete pure resolver and policy contract

> **Implementation status (2026-07-21): automated code exit gate complete.** The detached contracts,
> immutable XML-policy snapshot, structural/lexical resolver, bounded formatter/reflection shells, and
> assembly-free `BeliefContextTests` now exist. The suite passes 193 assertions with arbitrary synthetic
> Def identities, selectorless evidence rules fail closed, default-one/default-two selection and every
> formatter/deity/mutation diagnostic seam are pinned, the shipped correction list is empty, the policy
> and English/Russian DefInjected XML parse, and the core Debug build
> succeeds. No RimWorld/Verse/Unity/DLC reference enters the pure project. No live collector, adapter,
> hook, event route, prompt field, page, Scribe field, or scanner was added; Phase 1 and later remain
> pending and no loaded-game/manual acceptance is claimed.

1. Add `BeliefContracts`, including `BeliefResolutionRequest`, `ResolvedBeliefStance`, and
   `BeliefStanceResolution`, and `BeliefCorrelationFact`.
2. Add the `DiaryBeliefPolicyDef` schema/fallbacks and immutable pure snapshot for tier scores, lexical
   field weights/thresholds/margins, event-evidence rules, semantic aliases, and optional corrections.
3. Implement `BeliefLexicalMatcher` with bounded normalization, phrase/token/fuzzy scoring, dynamic
   common-token suppression, minimum confidence, and runner-up separation.
4. Implement `EventRelativeStanceResolver` completely: fail-closed knowledge gate, exact source/
   thought/history/issue tiers, lexical fallback, live-doctrine intersection, issue/redundancy collapse,
   normally-one/maximum-two selection, and quiet-only fallback.
5. Add formatter/reflection policy shells and `tests/BeliefContextTests`, file-linking only pure source.
6. Reuse Narrative Continuity N0's facet, salience, knowledge, category, and deterministic seed
   contracts; define only belief-specific evidence vocabulary and forward-compatible unknown-topic
   behavior.
7. Lock structural precedence, lexical ambiguity rejection, second-slot threshold,
   impact/repetition behavior, certainty boundaries, caps, empty behavior, and first-scan baseline in
   tests. The default correction-override list must be empty. No live event route changes yet.

Exit gate: arbitrary synthetic mod IDs resolve through structural/text facts, ambiguous/common-token
fixtures return empty, and organ/cannibalism/unrelated-belief scenarios pass; no RimWorld types leak
into the pure project; XML loads; docs describe the inactive policy contract.

**Exit-gate result:** complete for automated Phase-0 code/XML/tests only. Phase 1 subsequently supplied
the guarded runtime boundary without changing this resolver contract.

Phase 0 may be implemented beside Narrative Continuity N0. Narrative Continuity N1 must exist before
Phase 1 persists ordinary event enrichment, and Narrative Continuity N4 must be included in or precede
Phase 4's permanent rest-scheduler change.

### Phase 1 — Guarded snapshots and saved prompt source

1. Implement guarded `DlcContext.CaptureBeliefSnapshot`, including cached live issue/meme projection,
   `PreceptComp_Thought` links, conservative cached `eventDef`/`thought` field discovery for modded
   component subclasses, and thought offset valence.
2. Implement `BeliefContextBuilder`; call `EventRelativeStanceResolver` exactly once per eligible POV
   at event creation. Add event-time per-POV `beliefContext`, Scribe/load normalization, pipeline
   payload/value/source, context-detail behavior, and prompt-lab parity.
3. Snapshot `Thought.sourcePrecept` in `ThoughtSignal` and prove exact source identity with a live case.
4. Add the guarded, non-emitting `HistoryEventsManager.RecordEvent` observer and bounded correlation
   cache. Prove it attaches evidence to an existing event but creates no page by itself.
5. Wire a developer-only synthetic preview, one exact structural correlation, and one high-confidence
   lexical fallback to prove the live path with an empty exact-ID override list.
6. Add body-mod parity tests, remove its approve/despise ID lists, and use matched correlation valence
   without changing `BodyPartEventPolicy` precedence or capture decisions. Remove dual evaluation.
7. Do not add the standalone reflection yet.

Exit gate: a saved/reloaded synthetic event renders identical context; custom text is sanitized and
bounded; no-DLC returns empty; the history observer emits no pages; full/balanced/compact golden tests
pass; the same visible event resolves differently for different live ideoligions and remains ordinary
when there is no confident match.

**Exit-gate result:** complete for the Phase-1 code/XML/automated slice. The guarded adapter captures
active ideology, certainty/role, memes/structure, issues/precepts, exact source/thought/history
correlations, and mechanical thought valence into plain DTOs. Thought and body-mod sources resolve once
per already-eligible POV and persist normalized `beliefContext`; ordinary non-evidenced routes remain
unchanged. The non-emitting HistoryEvent observer uses a bounded exact-pawn/tick cache, developer
structural/lexical previews use the normal saved prompt seam, body-mod ID shortcuts are removed, and
Full/Balanced/Compact plus prompt-lab golden coverage passes. The adversarial follow-up additionally
failure-isolates optional enrichment, restores vanilla situational body-mod polarity through exact
active selected-precept thoughts, caches policy by active language, removes the history-hook closure,
and applies event-sensitive belief-field priority. Standalone belief tests pass 227 assertions;
focused capture/pipeline suites pass 713/2,803; the core and 362-test RimTest assemblies
build 0/0. The complete verification hook passes all 23 pure projects at 7,445 assertions. RimWorld was
not launched for the follow-up, so its new live fixtures are compiled but not recorded as executed.

### Phase 2 — Required event integrations and mutation capture

> **Partial implementation status (2026-07-22): infrastructure, exact interactions, and crisis.** Exact guarded
> `IdeoConversionAttempt`/`OffsetCertainty`/`SetIdeo` hooks project live state only through
> `DlcContext`, and a bounded pure buffer coalesces overlapping nested calls while keeping sequential
> same-tick actions distinct. The transient cache is reset per game, non-scribed, non-emitting, and
> optional-DLC inert. XML now owns exact `Convert`/`Reassure` canonical ownership; their generic
> Ability route drops before random sampling only while Ideology is active. `ConversionRitual` retains
> its generic start page until a cancel-aware fallback exists. Pure suites
> and five compiled RimTests cover policy/correlation/coalescing, hook ownership, no-DLC behavior,
> actual before/after boundaries, failure cleanup, and one Convert downstream page. Exact XML mutation
> rules now enrich existing conversion success/failure/attempt and reassurance PlayLog pages with one
> non-consuming target fact shared by both authorized POVs; pure selection fails closed on wrong pawn,
> time direction/age, mechanics, or a newer mismatched sequential row. Fifteen compiled Phase-2 RimTests
> add real success/failure/reassurance tracker + PlayLog boundaries, exercise the vanilla random-
> conversion worker with a conversion-capable spawned initiator and its normal stat/certainty factors,
> run the real Convert/Reassure effect comps through `Ability.Activate`, cover both successful and failed
> real `IdeoChange` boundaries, validate optional-evidence failure isolation and every shipped consumer-
> rule field, and prove cache-only non-emission. Classic Ideology mode
> skips only live mutation mechanics while keeping patch and XML-policy ownership checks active.
> The first loaded 376-test run completed 374/376; after correcting the conversion-worker fixture and
> satisfying the unrelated N3-O parked-gravship host guard, the rerun passed all 376/376 tests. Broader
> step 2, ritual/throne-speech parts of step 3, remaining step 4 prompt/group work, and the complete
> per-path exit gate remain pending. The adversarial hardening cases raise the compiled assembly to
> 379 tests; the prior 377/377 active-Ideology run remains the latest loaded result, and a post-Phase-2
> base-only run remains required before release sign-off.

> **Narrative N3-I result (2026-07-22): code-complete; loaded acceptance pending.** Phase 1's guarded,
> detached per-POV snapshot and single resolver result now feed one pure high-confidence gate and the
> fixed shared provider list. Exact thought/body evidence can yield one `interpretation` candidate;
> structural tiers are accepted directly and lexical tiers must retain both configured confidence and
> runner-up separation. Exact event/POV/source/facet matching, stable IDs, localized DefInjected neutral
> prose, deterministic selection, repetition, category/two-lens budgets, and N1 persistence are reused.
> No live ideology rescan, hook, page, poll, save field, or scheduler was added, and partial Phase 2 did
> not gain another consumer. Pure suites pass 360/345 belief/narrative assertions and 381 RimTests
> compile. The host's headless startup attempt did not load a game, so 377/377 remains the latest valid
> active result and current active/base-only execution remains pending.

1. Implement guarded/coalescing mutation patches and cache. **Implemented in the infrastructure slice.**
2. Add conservative evidence adapters for the §7.5 families to existing captured thought, social,
   body/medical, food, combat/prisoner, observed-condition, and ritual routes. Emit a topic only when
   that source has the exact visible fact needed; unavailable evidence leaves the route unchanged.
3. Enrich conversion conversation, conversion success/failure, reassurance/counsel, `IdeoChange`,
   conversion ritual, and throne speech through the same resolver. **Exact conversion interaction,
   Reassure mechanics, and the existing `IdeoChange` solo page are implemented; Counsel, ritual, and
   throne speech remain pending.**
4. Add exact XML groups/prompts and role-specific evidence. **The exact IdeoChange group/prompt and
   breaking-pawn evidence are implemented; remaining routes stay pending.**
5. Drop downstream-covered generic abilities before their random roll. **Implemented for the two exact
   interaction-backed vanilla routes (`Convert` and `Reassure`); `ConversionRitual` intentionally keeps
   its start owner until cancellation-safe downstream ownership exists.**

Exit gate: focused pure and RimTests prove correct before/after facts and exactly one canonical event
for each conversion ability/interaction/ritual path; representative body, meal, raid, thought, and
ritual fixtures select only relevant live doctrine; unrelated event and ability behavior is unchanged.

### Phase 3 — Persistent passive belief tracking

1. Add `PawnBeliefState` and save normalization.
2. Add elapsed, spread-out certainty/identity scanner.
3. Baseline old/new pawns, accumulate meaningful drift, merge changes, age stale evidence, and avoid
   catch-up spam.
4. Expose dev diagnostics but create no standalone reflection until Phase 4.

Exit gate: first scan emits nothing; threshold/cooldown/merge cases pass; save/load preserves pending
evidence; absent DLC/tracker cleanly resets/baselines.

### Phase 4 — Belief-reflection event and rest scheduling

1. Add event type/data/spec/signal/catalog registration.
2. Add Reflection group, exact event prompt, dedicated template, fallback text, pipeline flag, domain,
   and event noun.
3. Implement recent-event, pending-drift, ideology-change, and quiet trigger selection.
4. Insert the rest-flush priority and mark source evidence consumed on event creation.

Exit gate: each trigger can deterministically produce one page; cooldown/quadrum caps and reflection
priority hold; disabled/no-DLC paths produce none; day/arc/quadrum behavior remains intact.

### Phase 5 — Tune automatic coverage and exceptional corrections

1. Measure structural versus lexical match rates, rejection rates, runner-up gaps, and false positives
   in dev diagnostics without logging player-authored text.
2. Add high-value event evidence fields or localized semantic aliases for social, thought, role,
   authority, ritual, apostasy, and DLC cases discovered during playtests; do not add per-precept lists.
3. Verify newly loaded Biotech/Anomaly/Odyssey issues resolve from their ordinary thought/history/text
   metadata. Add an exact force/exclude correction only for a demonstrated metadata-poor exception,
   with reason, gate, and focused test.
4. Extend conservative mod-component field projection only when a real mod uses a safe public
   `ThoughtDef`/`HistoryEventDef` shape not covered by §7.1; never invoke arbitrary getters.
5. Tune lexical confidence/margin, structure/deity frequency, and repetition from prompt previews and
   playtests.

Exit gate: ordinary unrelated events stay unchanged; exact correlations consistently beat lexical
matches and impact; at least one arbitrary-ID mod fixture works with no override; performance remains
bounded with many pawns and many modded precepts.

### Phase 6 — Compatibility, documentation, and release hardening

1. Run loaded-Def contracts, pure suites, focused RimTests, save/load round trip, Debug build, and
   repository verify hook.
2. Test base game, Ideology alone, Ideology + Royalty, at least one DLC-defined live precept such as
   Anomaly `PsychicRituals` or Odyssey `SpaceHabitat`, and at least one modded/custom ideoligion—with
   all exact-ID correction overrides disabled.
3. Inspect prompt previews for factuality, certainty voice, structure flavor, deity restraint,
   compact budgets, and duplicate pages.
4. Finalize docs, prompt map, test matrix, changelog, localization, and rebuilt committed DLL.

Exit gate: every item in §3 and §17 is checked with evidence.

## 16. Test plan

### 16.1 Pure `BeliefContextTests`

Cover at minimum:

- empty/no-ideology snapshots;
- certainty band boundaries at exactly below/equal/above each threshold;
- rising/falling/stable and minor/meaningful/major delta boundaries;
- event-evidence rules expanding exact def/group/facet/mutation facts into deduplicated topics/aliases
  independently of doctrine selection;
- runtime source-precept instance id selecting the exact live precept, with defName fallback and
  duplicate-def fixtures;
- arbitrary synthetic precept/issue/meme IDs resolving through matching thought/history correlation
  facts with an empty override catalog;
- direct source/thought/history correlation outranking issue identity, lexical score, XML alias,
  association, and impact;
- narrative facet/topic/alias evidence ranking only matching live precepts, with unknown tags as
  no-ops;
- `pawnCanKnow=false` evidence producing no belief claim and no hidden-fact leakage;
- the same topic resolving differently for two ideoligions and resolving empty for a third with no
  relevant precept;
- exact issue identity and high-confidence issue/correlation text outranking an unrelated high-impact
  precept;
- guarded lexical normalization: Unicode/case/markup/whitespace, CamelCase/underscore Def tokens,
  phrase priority, dynamic common-token suppression, and bounded fuzzy similarity;
- single generic-token rejection, below-threshold rejection, tied/near-runner-up rejection, and unique
  distinctive-token acceptance at its configured boundary;
- no polarity inferred from stance words; positive/negative/mixed/unknown comes only from projected
  thought mood/opinion offsets;
- an organ/body-modification event preserving despise-over-approve parity without configured precept
  IDs;
- a cannibal meal selecting a live cannibalism stance while ignoring a stronger unrelated slavery or
  darkness precept;
- a raid without explicit aid/refusal facts never acquiring the `charity` topic;
- impact ordering without inspecting localized labels;
- role-fit bonus and recent-repetition penalty;
- event enrichment never using quiet fallback, while quiet reflection may use it;
- one stance by default, an independently supported non-redundant second stance, same-issue collapse,
  hard cap two, weighted selection without replacement, and stable tie ordering;
- deterministic output for identical seed and diversity for distinct seeds;
- max precept/meme/deity/line/character budgets;
- description whole-word trimming and hostile/multiline custom text sanitation inputs;
- structure separated from ordinary memes;
- related-meme deity > key deity > permitted deterministic alternative > none;
- duplicate/blank/custom/modded facts;
- default empty correction list plus one optional force/exclude override regression fixture;
- full/balanced/compact formatting;
- reflection trigger priority, quiet roll `0`/`1`, cooldown, quadrum cap, source-event reuse;
- first-scan baseline, passive-delta accumulation/merge, ideology change, and disabled reset;
- mutation merge keeping earliest before/latest after across nested calls.

### 16.2 Existing pure suites

`DiaryCapturePolicyTests`:

- `BeliefReflection` catalog registration and every Decide gate;
- ability downstream-covered payload skips the lazy roll, pure policy drops it, and ordinary
  abilities retain their existing roll/order;
- event context formats contain stable tokens and omit free-form unsafe values.

`DiaryPipelineTests`:

- `belief_reflection=` domain classification;
- payload reflection flag and `SoloBeliefReflection` template priority;
- exact defName → group → classifier → domain prompt precedence;
- per-POV `BeliefContext` mapping and opposite-POV isolation;
- required status for belief reflection and score by conversion/ritual/crisis/generic event;
- empty old-save field omission;
- full/balanced/compact field output;
- prompt-lab source parity and golden output.

### 16.3 RimTest scenarios

All cases must use disposable pawns/state and restore everything in `PawnDiaryRimTestScope` even after
failure. Gate Ideology/Royalty cases and report not-applicable when the DLC is inactive.

1. Live snapshot returns the current ideoligion name, certainty, structure, a precept/meme, projected
   thought/history correlations with offset valence, and a valid related/key deity when present.
2. `IdeoChange` mental state produces one dedicated crisis event with truthful success and failure
   mutation data.
3. Random conversion interaction success/failure produces a pair event with distinct POV contexts.
4. Convert ability produces the PlayLog pair event and no generic Ability event.
5. Reassure restores certainty, uses support wording, and produces no generic duplicate.
6. Conversion ritual records actual target before/after state and correct fan-out roles; its activation
   does not create a second page.
7. Throne speech keeps Royal facts primary and adds only available/relevant belief facts.
8. First passive scan baselines; a controlled meaningful certainty change becomes pending evidence.
9. Rest flush creates one belief reflection, obeys Arc/Quadrum priority, cooldown, and no-repeat rules.
10. Save/load round trip preserves event belief text and pending `PawnBeliefState` without catch-up.
11. An ordinary ability, unrelated thought, and ordinary chitchat retain prior behavior.
12. Base-game/no-Ideology initialization, tick, event capture, save, and prompt preview show no errors or
    empty belief headings.
13. With Ideology plus a DLC-defined issue loaded, an existing event (preferably Anomaly psychic ritual)
    selects its live precept through ordinary correlation/text metadata with the exact-ID override list
    empty; with Ideology or that precept absent, the source emits its normal page without belief context.
14. A body-modification event returns the same existing approve/despise attitude before and after
    migration, including despise precedence, with the legacy precept-ID lists removed.
15. A gained ideological memory with non-null `Thought.sourcePrecept` resolves that exact live precept
    even when its Def/issue names are absent from Pawn Diary XML.
16. Recording a matching `HistoryEvent` populates/consumes the bounded evidence sidecar for the correct
    visible pawn and tick but creates zero diary pages on its own; stale/unrelated facts do not attach.

The 2026-07-22 crisis slice compiles scenario 2 as a deterministic, restored-RNG loaded fixture. It
forces the real `MentalState_IdeoChange.PreStart` boundary on a spawned disposable pawn, requires one
existing solo page and one non-consumed coalesced mutation row, checks exact previous/current identity
and certainty mechanics, verifies PlayLog is unchanged, and reports inactive-DLC/classic-mode branches
as not applicable. Pure fixtures cover changed/unchanged results plus wrong-pawn, stale, future, and
missing-evidence fallbacks; the existing real Berserk fixture remains the unrelated-state regression.
The first loaded run exposed vanilla's nested silent wander transition as a second captured page; the
exact companion suppression above fixes the production path while preserving the fixture's original
one-total-page assertion. The corrected loaded rerun passed the complete 377/377 suite.

Never retry chance-driven cases until they happen. Override XML-effective chance to `0`/`1` or inject
a deterministic pure decision.

### 16.4 Verification commands

Use the repository's actual project names/configurations discovered at implementation time. Expected
commands include:

```powershell
dotnet run --project tests\BeliefContextTests\BeliefContextTests.csproj
dotnet run --project tests\DiaryCapturePolicyTests\DiaryCapturePolicyTests.csproj
dotnet run --project tests\DiaryPipelineTests\DiaryPipelineTests.csproj
MSBuild tests\PawnDiary.RimTest\PawnDiary.RimTest.csproj /t:Build /p:Configuration=Debug
MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug
powershell -NoProfile -ExecutionPolicy Bypass -File .\.githooks\verify.ps1
```

If MSBuild is absent from `PATH`, use the `vswhere` process documented in `AGENTS.md`. Any C# phase
must include the rebuilt `1.6/Assemblies/PawnDiary.dll` in its reviewed/staged files.

## 17. Acceptance scenarios

Use these as final human-readable checks in addition to automated assertions:

1. **Fervent converter:** a high-certainty moral guide tries to convert a low-certainty pawn. The two
   entries use their own ideoligion names and certainty voices; success/failure matches the game.
2. **Failed conversion:** the target loses certainty but does not change faith. The diary describes a
   challenge or doubt, never a completed conversion.
3. **Successful conversion:** old and new ideoligion names are preserved in the event-time context,
   and a later save/load does not rewrite them.
4. **Crisis of belief:** a very low-certainty pawn sounds actively doubtful and refers only to supplied
   candidate/current doctrine.
5. **Reassurance:** certainty rises within the same faith; prose is supportive rather than adversarial.
6. **Conversion ritual:** organizer, target, and witness contexts differ appropriately; ritual quality
   and actual conversion outcome do not contradict each other.
7. **Throne speech:** belief influences the speech only when an authority/role/structure connection is
   available; otherwise the Royal event remains natural and secular.
8. **Quiet reflection:** with no recent event, a pawn reflects on a strong doctrine; structure provides
   flavor and a related/key god may be named, but unrelated doctrines are absent.
9. **Recent-event reflection:** a relevant saved event is revisited once after rest, then blocked by
   source-id/cooldown rules.
10. **Unrelated life:** meals, chores, chitchat, and ordinary abilities do not become religious by
    default.
11. **Custom/modded faith:** custom names and descriptions survive sanitation and limits; a modded
    precept using ordinary source-precept/thought/history metadata resolves without any Pawn Diary ID
    entry or vanilla-English name assumption.
12. **No DLC:** the same build loads, ticks, saves, and generates ordinary diary entries without
    Ideology installed or active.
13. **Same event, different faiths:** an organ implant resolves body-modification doctrine for a pawn
    whose live ideoligion has it, a different stance for an opposing faith, and no doctrine for a faith
    without a matching precept, while the exact-ID override list is empty.
14. **Relevant meal:** a known cannibal meal can select cannibalism doctrine, but cannot select a
    stronger unrelated slavery, darkness, or proselytizing precept.
15. **Conservative raid:** a raid may resolve violence/raiding/honorable-combat doctrine supported by
    its facts; it does not resolve charity, execution, or slavery without those specific visible facts.
16. **Bounded multi-topic event:** an event with two independently supported issues may include two
    stances; same-issue variants collapse, a third is dropped, and an event with no match has an empty
    belief field while all existing output remains unchanged.
17. **Ambiguous text:** an event sharing only generic words with several precepts produces no belief
    context; after an exact thought/history correlation is supplied, the correct live precept wins
    regardless of labels, impact, or mod namespace.

## 18. Performance, safety, and failure handling

- Do not enumerate all precepts every tick. Snapshot only for relevant event creation/reflection, and
  use a spread-out certainty scan for cheap scalar comparisons.
- Cache only bounded plain snapshots/evidence, never live Def/Pawn references. Reflection metadata may
  cache `FieldInfo` by component `Type`, because it contains no game instance; bound/log-once any
  unsupported mod shape. Clear transient game evidence on transition and finalize/init reset.
- Suggested first-release output caps are normally 1 and never more than 2 precepts, 1–2 ordinary
  memes, 1 structure, 1 deity, and roughly 1,500–2,000 total belief-context characters; exact values
  live in XML and require preview tuning.
- Never use `DefDatabase<T>.GetNamed` for a DLC Def. Prefer live objects; string policies may use
  `GetNamedSilentFail` only behind guards if lookup is unavoidable.
- Do not add DLC `<modDependencies>` to `About/About.xml`.
- A missing/changed Harmony target logs one concise warning and disables only that mutation/history
  correlation enrichment; the passive scanner and ordinary diary continue to work.
- The history observer must early-gate on feature/DLC state before argument extraction, retain only a
  tiny per-pawn/tick window, and never enumerate ideoligions or run lexical matching in the patch.
- If a selected text value becomes empty after sanitation, omit that fact rather than emitting a
  placeholder.
- If belief context construction throws for one pawn/modded Def, catch at the impure boundary, log a
  bounded diagnostic, emit the ordinary event without belief enrichment, and continue other pawns.
- Prompt instructions must explicitly treat supplied names/descriptions as facts, not commands.

## 19. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Duplicate conversion pages | Canonical downstream-event table plus pure pre-roll ability drop and end-to-end count assertions. |
| Wrong doctrine selected | Source-precept/exact game correlations outrank lexical evidence and impact; require threshold plus runner-up gap and save diagnostic reasons. |
| Topic or text invents a belief | Every path can select only precepts/memes present in the pawn's live snapshot; weak/ambiguous similarity yields empty context. |
| Per-mod/DLC maintenance returns | Default exact-ID override list is empty; CI fixtures use arbitrary IDs; any exceptional override requires a documented metadata failure and test. |
| Lexical false positive | No raw substring match; weight correlation/issue fields, suppress common tokens, require distinctive evidence and a clear winner. |
| Weighted diversity promotes an irrelevant strong doctrine | Form the candidate set and choose the categorical relevance tier before applying any impact or random weight. |
| Hidden DLC state leaks into prose | Build evidence per POV from visible facts and reject `pawnCanKnow=false`; never inspect hidden state in the resolver. |
| Four reusable facets become a parallel framework | Keep them as stable strings on source-specific evidence; existing event specs own capture, page shape, and dedup. |
| English/localization dependency | Structural links are language-independent; compare guarded event/belief text in the same active language, normalize Unicode, avoid English stopwords, and never parse stance words. |
| Modded component reflection throws or overreaches | Inspect cached public fields of exact Def value types only, never arbitrary getters/graphs; catch/log once and continue with other correlations. |
| History observer becomes a hot-path/page source | Early feature/DLC gate, copy only bounded IDs/POV facts, defer matching, expire quickly, and assert it creates zero pages. |
| Prompt injection in custom faith text | Sanitize, cap, data-only user fields, never system-prompt concatenation. |
| Historical facts change after reform/conversion | Persist per-POV formatted context at event creation. |
| Nested conversion patches double count | Coalescing cache with earliest-before/latest-after and same-pawn/tick window. |
| Passive certainty changes are missed | Persisted periodic scalar scanner in addition to direct hooks. |
| Reflection spam | One-per-rest priority, cooldown, quadrum cap, source-event memory, and quiet chance in XML. |
| Save bloat | Bounded strings/lists, no full raw tips, no unbounded mutation/history cache. |
| No-DLC runtime break | Double guards before all live Ideology projection/observer work, optional XML policy, base-game test matrix. |
| Mod/version API drift | Registrar null checks, local assembly verification, safe ordinary-event fallback. |

## 20. Follow-on opportunities after the required release

### 20.1 Reviewed cross-DLC roadmap

The broader DLC research is sound and should remain a companion roadmap rather than expanding the
required scope above. The Ideology feature should land early because its stance resolver can interpret
all later story arcs. After that foundation, use this order while allowing the two tiny data-only
fixes to ship independently:

| Slice | Narrative facet(s) | Integration with this plan | Review note |
|---|---|---|---|
| Anomaly ritual-specific XML families | Source ritual; `identity_transition` only for genuinely transformative outcomes | Existing ritual signal supplies `psychic_rituals` and exact ritual topics to the resolver | All 16 installed psychic ritual Defs currently match one `PsychicRitual` catchall. Prefer a few semantic families (summoning, theft/extraction, coercion, mortality/identity) before the catchall rather than 16 almost-duplicate pipelines. Adds no pages. |
| Odyssey seasonal-flood correction | `ambient_pressure` | A flood observer may later provide nature/home topics | Confirmed mismatch: Pawn Diary matches `Flooding`, while 1.6.4871 defines incident `SeasonalFlooding` and persistent thing `SeasonalFlood`. This is a separate small correctness fix, not part of belief implementation. |
| Biotech growth moments and family continuity | `identity_transition`, `bond_lifecycle` | Child/parent POVs submit `child_labor`, `growth_vat`, family, and other fact-supported topics | `ChoiceLetter_GrowthMoment.MakeChoices` is a stable post-choice point in the installed assembly. Dedup/merge it with birthday and trait/passion progression so one choice produces one page. |
| Odyssey journey/landing chapters | `journey_chapter`, sometimes `ambient_pressure` | Submit `space_habitat` and only destination/activity topics the pawn can know | Current `GravshipLaunch` ritual is captured before destination selection; its prompt mentions landing but cannot provide an actual landed destination. Capture successful landing separately with origin/destination/biome/site facts and aggregate routine hops. |
| Royalty persona-weapon lifecycle | `bond_lifecycle` | Submit `weapons`, `bonding`, violence, and role topics | Strong narrative fit, but perform a hook/state feasibility spike first. Bond, separation, recovery, destruction, and first consequential kill do not necessarily share one stable vanilla event. Preserve stable weapon identity and suppress equipment-swap noise. |
| Biotech salient genes/mechanitor/pollution | `identity_transition`, `bond_lifecycle`, `ambient_pressure` | Submit `body_modification`, `autonomous_weapons`, and fact-supported environmental topics | Select themes, not gene lists; milestones, not bandwidth/mech-gestation churn. Pollution belongs primarily in observed-condition thresholds. |
| Anomaly knowledge/containment/monolith arcs | `journey_chapter`, `ambient_pressure`, occasional `identity_transition` | `psychic_rituals` and visible temptation/knowledge topics | Keep the existing metalhorror rule as the epistemic model: no hidden host, creepjoiner downside, or unrevealed outcome enters evidence. |
| Royal succession, dramatic permits, and ascent | `identity_transition`, `journey_chapter` | Authority, charity, violence, and role topics when live doctrine supports them | Verify that grantor/heir/cause facts survive at the chosen hook before promising them. Never infer “wanted the title,” obligation, or succession from title rank alone. |

The “why would this pawn remember it?” filter should be shared policy vocabulary, not one universal
page gate. Source policies still decide:

- permanent identity change → dedicated source page and possible later reflection;
- meaningful relationship change → pair/subject continuity;
- colony chapter boundary → rare chapter page;
- persistent pressure → observed context, with bounded start/escalation/end pages;
- routine action → ignore or aggregate;
- hidden information → omit until visibly known.

This ordering separates **product priority** from **implementation cost**. Biotech growth/family and
Odyssey journeys remain the highest-value new arcs, while Anomaly prompt families and the flood-name
correction are smaller independent wins that can safely ship first.

### 20.2 Ideology-specific follow-ons

These fit the same contracts and should be separate reviewed slices:

- **Fluid ideoligion reform reflection:** snapshot added/removed memes/precepts and let a leader, moral
  guide, and at most one ordinary believer react; cap colony-wide fan-out.
- **Role appointment/loss:** compare ideological role identity in progression state and reflect on
  authority, duty, or loss of status.
- **Mixed-faith relationship tension:** enrich romance, marriage, breakup, and family events only when
  ideoligions differ and a supplied diversity/proselytizing precept makes it relevant.
- **Apostasy memory arc:** let later arc reflections revisit a saved conversion/crisis without reading
  current live doctrine as historical truth.
- **Relic, funeral, festival, and sacred-site context:** string-match ritual/event defs and select only
  live relevant precepts/memes.
- **Ideological consistency diagnostics:** dev-only warning when LLM prose names an unsupplied faith or
  deity; never mutate or censor the player's source text silently.
- **UI filter/color cue:** optional belief-reflection filter/icon/color defined in XML after usability
  testing, without making all enriched events look like the new diary type.
- **Public integration API facts:** allow adapters to supply plain belief evidence tokens without
  accepting live `Ideo`/`Precept` objects.

## 21. Coding-agent handoff checklist

Before each phase:

- read `AGENTS.md`, the engineering skill, relevant `DOCUMENTATION.md`, and this plan;
- inspect `.codegraph/` first and map callers/tests for every touched symbol;
- inspect `git status` and preserve unrelated user changes;
- confirm the exact RimWorld 1.6 member signatures locally;
- state the smallest phase plan and its validation commands.

Before declaring a phase complete:

- pure logic is separated and tested;
- all tunable policy/prompt text is in XML with safe code fallbacks;
- all UI/LLM-facing text is localized through the correct Keyed/DefInjected route;
- no-DLC, null tracker, old-save, and custom/modded text paths are covered;
- relevant tests and Debug build pass;
- docs, prompt map/test matrix where applicable, changelog, comments, and committed DLL are updated;
- `git diff --check` and a focused diff review show no unrelated or accidental changes.

If implementation discovers a conflict with a user-owned dirty file, an unstable game API, or a
requirement that would broaden scope materially, stop that phase and record the evidence/decision
instead of silently choosing a new architecture.
