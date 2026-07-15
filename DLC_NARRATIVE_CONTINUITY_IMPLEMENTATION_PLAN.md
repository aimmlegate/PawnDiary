# Pawn Diary — cross-DLC narrative continuity implementation plan

> **Status:** implementation-ready architecture plan; no production behavior is changed by this
> document.
>
> **Scheduling:** `DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md` dictates when N0–N5 and each
> provider-specific slice are implemented. This document dictates how the shared layer works.
>
> **Authority:** this is the canonical shared-layer plan for Royalty, Ideology, Biotech, Anomaly,
> and Odyssey integration. The source-specific DLC plans remain authoritative for capture hooks,
> truth, deduplication, state machines, and page ownership. When a shared-continuity detail conflicts
> with an older optional-facet note in a DLC plan, this document controls the shared contract.
>
> **Required workflow:** follow `AGENTS.md` and `skills/pawndiary-engineering/SKILL.md` for every
> implementation slice: inspect first, make the smallest safe change, keep game reads at impure
> edges, test pure policy, keep tuning in XML, update docs/changelog, build, and preserve no-DLC play.

## 1. Review outcome

The five DLC plans already define strong source-owned stories, but their common integration stops at
four facet words and a future Ideology consumer. That is not enough to make entries feel connected:

- Royalty and Biotech propose compatible facet/topic strings, but no shared selector consumes them.
- Anomaly treats the facet seam as optional and Odyssey does not yet define concrete emissions.
- Each plan invents useful local identities—bond epoch, family arc, mechanitor arc, monolith chapter,
  journey ID—but saved diary pages cannot refer to those identities in one common shape.
- The prompt pipeline has no bounded cross-DLC narrative-context field.
- The rest scheduler already arbitrates arc, quadrum, and day reflections; adding belief and future
  DLC reflections independently would create starvation or duplicate retrospective pages.

The solution is a small **Narrative Continuity Layer**. It does not capture game events and never
authorizes an immediate page. It accepts source-owned, event-time evidence; asks guarded providers
for relevant lenses; selects at most one or two; persists lightweight references; and coordinates
rare later reflection.

## 2. Product outcome

After the shared layer and participating DLC slices are implemented:

1. A DLC entry remains truthful and recognizable as its source story without naming the DLC.
2. A pawn's relevant title, belief, family bond, transformation, mobile home, or ongoing pressure may
   shape that entry when it materially changes the interpretation.
3. Unrelated DLC facts do not enter the prompt merely because the content pack is active.
4. Later pages can find an earlier page from the same bond, subject, family, or journey even after the
   earlier hot event has been compacted into the archive.
5. A landing can remember departure; a recovery can remember separation; a later transformation can
   connect to a family or identity arc; a rare reflection can cross DLC boundaries when the memories
   describe one coherent change.
6. Ideology is an important interpretation provider, not the owner or prerequisite of cross-DLC
   continuity.
7. The shared layer never reveals hidden Anomaly state, guesses a quest outcome, invents kinship, or
   treats a mechanical tracker value as lived experience.
8. One gameplay action still has one canonical page owner.
9. Compact prompts remain bounded: normally zero or one selected lens, maximum two.
10. Any combination of zero to five DLCs loads, saves, and generates safely.

## 3. Scope and non-goals

### 3.1 Required scope

- Plain shared evidence, reference, candidate, request, selection, and reflection contracts.
- A pure deterministic selector with XML-owned weights, caps, affinities, decay, and repetition
  policy.
- Per-POV event-time narrative evidence and selected-context persistence.
- Compact archive preservation and indexing of continuity references.
- One optional prompt field for selected narrative context.
- Guarded provider orchestration that only runs for an already-authorized event/POV.
- One reflection coordinator over arc, belief, quadrum, and day opportunities.
- Required emission obligations in all five DLC plans.
- Pure tests, RimTests, save migration, no-DLC tests, XML validation, localization, documentation,
  changelog, and committed DLL updates for behavior slices.

### 3.2 Explicit non-goals

- No generic `IdentityTransitionEventData`, `BondLifecycleEventData`, `JourneyChapterEventData`, or
  `AmbientPressureEventData` page-producing source.
- No cross-DLC Harmony patch and no pairwise Royalty×Biotech-style integration classes.
- No global dump of every active DLC state into every prompt.
- No prompt-time reread of live game state after the event has been queued.
- No retroactive inference or catch-up pages for old saves.
- No player-facing DLC labels or top-level "cross-DLC integration" toggle.
- No external/public API expansion in the first release. Public DTO exposure can be reviewed after
  the internal save and prompt contracts stabilize.
- No UI graph/timeline in the first release; this plan supplies metadata that could support one later.

## 4. Architectural invariants

1. **The source owns truth.** Royalty, Ideology, Biotech, Anomaly, or Odyssey code decides what
   happened and whether a page exists before the shared layer runs.
2. **Facets are evidence, not events.** They describe why an existing event matters.
3. **Knowledge is per POV.** Evidence defaults to unknown and fails closed; only explicit
   `pawnCanKnow=true` can reach a prompt or later reflection.
4. **Cross-DLC state is projected, not shared live.** Providers return plain snapshots and never pass
   `Pawn`, `Def`, trackers, maps, weapons, gravships, or anomaly objects to pure code.
5. **Selection is bounded and deterministic.** The caller supplies a stable seed. Tie-breaking uses
   stable keys, not `Verse.Rand` inside pure policy.
6. **Event-time text is frozen.** Selected localized facts are copied before async generation.
7. **Policy is XML-owned.** Weights, caps, topic affinities, category conflicts, age decay, and
   reflection cooldowns live in a Def with safe fallbacks.
8. **References do not own state.** A saved `arcKey` points at related pages; the DLC-specific saved
   model remains the authority on whether an arc is active or ended.
9. **No provider can create fan-out.** POV selection remains source-specific.
10. **Absence is normal.** Missing DLC, missing policy, missing provider facts, old-save empty lists,
    and no relevant candidate all produce the ordinary unchanged entry.

## 5. Target flow

```text
source-specific live callback / existing signal
        |
        v
source-specific plain event facts + capture decision
        |
        | page authorized; POVs and ownership frozen
        v
per-POV NarrativeEvidence (event-time, knowledge-gated)
        |
        +--> guarded DLC providers project zero or more plain NarrativeLensCandidate values
        |
        v
pure NarrativeContextSelector + XML policy snapshot
        |
        v
NarrativeContextSelection (0..2 lenses + references + diagnostics)
        |
        +--> copy bounded prompt text into the POV slot
        +--> persist references on the POV/archive row
        +--> offer reflection metadata to the shared coordinator
        |
        v
existing prompt planner -> existing queue/transport/UI
```

The shared builder runs after capture policy has authorized an event but before the `DiaryEvent` is
persisted and queued. A failure logs once where appropriate and returns an empty selection; it does
not cancel the source page.

## 6. Stable plain contracts

Place pure contracts under `Source/Pipeline/Narrative/`. Names may be adjusted during Phase N0, but
their responsibilities and save tokens must be frozen before behavior ships.

### 6.1 `NarrativeEvidence`

One source-owned statement of why this event matters to this POV:

```text
eventId / tick / povPawnId / povRole
facet                 identity_transition | bond_lifecycle | journey_chapter | ambient_pressure
phase                 stable source-appropriate token
subjectKind           pawn | weapon | mech | animal | family | colony | ship | entity | place
subjectId             stable identity when known
subjectLabel          bounded sanitized event-time label
arcKey                 optional stable continuity identity
relatedEventId         optional earlier canonical event already known by the source
beliefTopics           bounded stable token list
salience               minor | meaningful | major | terminal
pawnCanKnow            nullable/tri-state in adapters; only explicit true survives
sourceDomain           stable diagnostics token, never prompt prose
sourceDefName          exact captured source key
```

Rules:

- A source may emit multiple facets, but the initial cap is three evidence rows per POV.
- `phase`, `subjectKind`, facet, topics, and salience are stable English schema tokens.
- `subjectLabel` is localized/sanitized on the main thread.
- `arcKey` contains stable IDs, never localized labels. Representative shapes:
  `royalty-persona|<weaponId>|<bondEpoch>`, `biotech-family|<familyArcId>`,
  `anomaly-monolith|<campaignEpoch>`, `odyssey-journey|<journeyId>`.
- Empty subject/arc fields are allowed when the event still carries useful facet/topic evidence.
- Hidden or speculative facts are omitted rather than marked with a suggestive sentinel.

### 6.2 `NarrativeReference`

The minimal saved and archived continuity pointer:

```text
facet / phase / subjectKind / subjectId / arcKey / sourceEventId / sourceTick
```

It contains no prompt prose and no live reference. `sourceEventId` is the event that produced the
reference; `relatedEventId` remains event evidence and is not required for lookup.

### 6.3 `NarrativeLensCandidate`

One provider's proposed cross-event/context lens:

```text
candidateKey           stable repetition/tie-break key
provider               royalty | ideology | biotech | anomaly | odyssey | core
category               identity | bond | interpretation | chapter | pressure | home
text                    bounded localized event-time factual text
facet / subjectKind / subjectId / arcKey
topicTokens             bounded stable tokens
sourceEventId / sourceTick
salience
relationship            exact_arc | exact_subject | direct_topic | direct_facet | ambient | none
pawnCanKnow
isPrimaryEventFact       normally false for provider candidates
```

Providers must not write instructions, tone, or invented emotional conclusions. Candidate text says
what is true; XML prompt policy tells the model how to use selected context.

### 6.4 Selection contracts

`NarrativeContextRequest` contains:

- current POV evidence;
- candidate list;
- plain XML policy snapshot;
- current tick;
- stable deterministic seed;
- bounded recent selected candidate keys;
- context detail level and prompt budget.

`NarrativeContextSelection` contains:

- zero to two ordered candidates;
- bounded formatted narrative-context text;
- references to persist;
- stable selection reason tokens and rejected-candidate diagnostics;
- whether one selected candidate consumes the interpretation slot;
- no live objects and no settings/Def references.

### 6.5 Reflection contracts

`ReflectionOpportunity` describes one possible page without creating it:

```text
kind                   major_arc | cross_arc | belief | quadrum | day
pawnId / nowTick
sourceEventIds / arcKeys
candidateMemoryCount
importance / due / alreadyWritten / cooldownSatisfied
```

`ReflectionPlan` returns one selected opportunity or none, plus stable rejection reasons and exact
state-consumption instructions. The impure scheduler remains responsible for building/dispatching the
chosen existing reflection signal.

## 7. XML policy

Add `DiaryNarrativeContinuityDef` plus `1.6/Defs/DiaryNarrativeContinuityDefs.xml`. The Def owns:

- feature enabled fallback and maximum evidence/candidate/selected counts;
- Full/Balanced/Compact lens caps and character budgets;
- base score by relationship, facet, category, and salience;
- age-decay windows and floors;
- repeated-candidate cooldown/penalty;
- category coexistence rules (for example identity + pressure allowed; two pressure rows normally not);
- topic/facet affinity rows;
- provider-specific corrections only when a generic rule cannot express a verified need;
- reflection priorities, global/per-kind cooldowns, minimum linked memories, memory cap, and maximum
  span;
- prompt field label/instruction references and safe fallbacks.

Do not put localized candidate prose in topic-affinity rows. Def labels/instructions use DefInjected;
player-facing fallback strings use Keyed translation. Exact schema tokens remain English by the
localization carve-out.

## 8. Selection policy

### 8.1 Hard gates

Reject a candidate when any is true:

- knowledge is not explicitly true;
- text or stable candidate key is empty;
- provider DLC is inactive or its guarded snapshot is absent;
- relationship is `none` and no XML affinity matches current evidence;
- it exposes the same primary fact already present in the event context without adding a distinct
  relationship or interpretation;
- its source tick is in the future or beyond the configured maximum age;
- its subject conflicts with exact source evidence;
- it is an ambient pressure from another map/location without a verified POV connection.

### 8.2 Score order

The pure selector scores in this priority order:

1. exact `arcKey` match;
2. exact subject kind + ID match;
3. direct topic affinity supported by source evidence;
4. direct facet/category affinity;
5. current active home/pressure that applies to the POV's event location;
6. salience and recency;
7. XML provider/category weight;
8. repetition and redundancy penalties.

Exact arc/subject matches must beat DLC novelty. A terminal but unrelated candidate still loses.

### 8.3 Composition

- Default maximum: two selected candidates.
- Normal target: zero or one.
- At most one from each category.
- At most one `interpretation` candidate; an Ideology stance consumes this slot.
- At most one `pressure`/`home` candidate in Compact mode.
- Two candidates require different categories and non-redundant text/topics.
- Stable tie-break: score descending, relationship rank, source tick descending, candidate key
  ordinal.
- Formatting preserves candidate order and enforces the XML character budget after selection.
- If truncation would cut a factual unit, drop the lower-ranked candidate instead.

### 8.4 Repetition

Persist the candidate keys actually selected for the POV. Recent-key history is bounded and may be
reconstructed from hot events plus compact archive rows. Repetition normally reduces score; it does
not hard-block an exact arc continuation such as separated -> recovered.

## 9. Prompt and save integration

### 9.1 `DiaryEvent` and POV slots

Add per-POV:

- `List<NarrativeEvidenceState> narrativeEvidence`;
- `List<NarrativeReferenceState> narrativeReferences`;
- `string narrativeContext`;
- bounded selected candidate keys for repetition diagnostics.

Use deep Scribe for state rows, initialize lists at declaration, normalize nulls in PostLoadInit, and
omit no-DLC empty rows. Do not save live provider candidates after selection.

### 9.2 Compact archive

Extend `ArchivedDiaryEntry` with the bounded reference list and selected candidate keys needed for
continuity/repetition. `DiaryArchiveRepository` adds indexes for non-empty `arcKey` and
`subjectKind|subjectId`, scoped by pawn/POV entry. Indexes are rebuilt after load and never serialized.

Archive compaction must copy these fields before removing the hot event. Retention may drop old prose
normally; it must not preserve unlimited continuity metadata beyond the archive row cap.

### 9.3 Pipeline payload

Add `narrativeContext` to `DiaryPovPayload` and a `NarrativeContext` prompt source. Do not overload the
existing `continuity`/relationship field: pair relationship and cross-event story continuity are
different facts and need independent budgets.

Add the optional field to first-person templates only. Neutral death/arrival summaries receive it only
after a separate truth review; the first shared release leaves neutral templates unchanged.

The context-detail selector applies:

- Full: up to two selected lenses within the full budget;
- Balanced: up to one, or two very short exact-arc lenses if XML permits;
- Compact: at most one exact/high-confidence lens;
- omitted/empty: no label is rendered.

### 9.4 Old saves

- Missing lists normalize to empty.
- No historical event is reclassified or backfilled.
- DLC-specific state models baseline according to their own plans.
- Empty continuity state means ordinary pre-feature behavior.
- Save schema/version markers distinguish "not initialized" from a valid initialized empty index.
- Loading, changing settings, or enabling a DLC must not generate catch-up pages or false firsts.

## 10. Provider orchestration

Add `NarrativeContextBuilder` in the impure generation/adapter layer:

1. Return empty when the source supplied no authorized evidence.
2. Copy the XML Def to a plain policy snapshot on the main thread.
3. Query only providers whose cheap applicability check matches at least one evidence facet/topic.
4. Providers use `DlcContext` or their source-owned guarded adapter to capture plain facts.
5. Sanitize/localize provider text on the main thread.
6. Call the pure selector.
7. Copy selected text/references/keys into the event POV before queueing.

Use a fixed internal provider list rather than reflection or a service locator. Provider absence is
normal. Each provider must expose a cheap `CouldApply` check so an ordinary minor event does not scan
all DLC trackers.

### 10.1 Provider obligations

| Provider | May propose | Must not propose |
|---|---|---|
| Royalty | exact current title/duty, matching persona bond, active court pressure | generic rank on unrelated daily events; inferred ambition |
| Ideology | high-confidence event-relative live stance and certainty framing | random doctrine; hidden/irrelevant precept; moral valence guessed from prose |
| Biotech | matching family/psychic/mech bond, salient identity, applicable pollution pressure | gene lists; every mech; inferred parent feeling |
| Anomaly | visibly known transformation/chapter/containment pressure | infection, downside, entity identity, or outcome unknown to the POV |
| Odyssey | active mobile-home/journey/homecoming and location pressure | hidden site threats; uncommitted destination; engine/fuel dump |

## 11. Source-plan emission contract

Once Phase N1 is available, a new source-specific DLC event in scope must attach the following
evidence in the same slice that creates the canonical page. A DLC slice may still ship before N1, but
its plan must contain a named retrofit phase and frozen mapping so metadata is not forgotten.

| DLC moment | Facet / phase | Required identity |
|---|---|---|
| Persona bond formed/separated/recovered/ended | `bond_lifecycle` / exact lifecycle phase | weapon ID + bond epoch arc |
| Royal title transition/succession | `identity_transition` / promoted, demoted, inherited, lost | pawn subject; faction/title facts stay source context |
| Royal Ascent start/end | `journey_chapter` / started, completed, failed | quest/window arc key |
| Conversion/certainty identity change | `identity_transition` / converted, apostasy, destabilized | pawn + ideology identity when visible |
| Growth moment/xenotype/mechlink | `identity_transition` / exact source phase | pawn subject + family/mechanitor arc when applicable |
| Birth/psychic bond/significant mech loss | `bond_lifecycle` / formed, tested, broken, ended | exact pawn/mech/family subject |
| Mechanitor boss tier | `journey_chapter` / called, defeated | mechanitor arc key |
| Pollution | `ambient_pressure` / started, escalated, eased, ended | map/home scope |
| Ghoul transformation | `identity_transition` / transformed | pawn subject |
| Monolith/void progression | `journey_chapter` / exact visible chapter | campaign/monolith arc key |
| Containment breach | `ambient_pressure` / breached, recovered | map/entity scope only when visible |
| Gravship departure/landing | `journey_chapter` / departed, arrived, returned | journey ID + ship ID |
| Active gravship home/vacuum/flood | `ambient_pressure` or provider `home` lens | exact ship/map/location scope |

## 12. Reflection coordination

Replace independent "try this, return; try that" growth with one pure planning step. The initial
priority is:

```text
major source-owned arc closure
-> linked cross-arc reflection
-> belief reflection
-> quadrum reflection
-> day reflection
```

Rules:

- One rest opportunity creates at most one reflection page.
- Immediate source pages never create an immediate second reflection.
- A cross-arc reflection requires at least two memories connected by the same pawn plus an exact arc,
  subject, relationship, home, or meaningful before/after transition.
- Merely coming from different DLCs is not a connection.
- Selected memories should normally span at least two phases and include a change or consequence.
- Do not summarize prior reflection pages.
- Consume pending reflection state only after the selected reflection event is successfully created.
- Disabling a reflection group advances/bounds its pending state so re-enabling cannot dump backlog.
- Reuse `ArcReflection` for the first cross-arc release unless its truth/template contract proves
  insufficient; do not add `CrossDlcReflectionEventData` by default.

## 13. Ownership and deduplication

The shared layer never arbitrates raw Harmony callbacks. Existing/source-specific transactions still
own duplicate suppression. It participates after one owner is chosen:

| Situation | Canonical owner | Shared-layer behavior |
|---|---|---|
| persona first kill + combat Tale | combat/Tale page | attach persona bond evidence/context |
| growth choice + birthday/progression | Biotech growth page | attach identity + family evidence |
| title inheritance + title scan | Royalty progression page | attach identity/succession evidence |
| launch ritual + takeoff | Ritual departure page | attach journey departure reference when committed correlation is truthful; no second page |
| landing + quest site | Odyssey landing page for arrival; Quest owns quest lifecycle | share place/journey references, never claim completion |
| psychic ritual + thoughts | Ritual page | attach belief/Anomaly evidence; thoughts remain suppressed/ordinary per source policy |
| breach + assault/injury | Anomaly breach owner where authorized | pressure context may shade later injury pages without replaying breach |

`relatedEventId` can link a source-owned enrichment to the canonical page, but it must never be used
to suppress an event without the source-specific verified ownership transaction.

## 14. File-level change map

### 14.1 Proposed new production files

- `Source/Pipeline/Narrative/NarrativeContracts.cs`
- `Source/Pipeline/Narrative/NarrativeContextSelector.cs`
- `Source/Pipeline/Narrative/NarrativeReferencePolicy.cs`
- `Source/Pipeline/Narrative/ReflectionCoordinator.cs`
- `Source/Defs/DiaryNarrativeContinuityDef.cs`
- `Source/Generation/NarrativeContextBuilder.cs`
- `Source/Generation/NarrativeContextProviders.cs` (or small provider files grouped by DLC)
- `Source/Models/NarrativeEvidenceState.cs`
- `Source/Models/NarrativeReferenceState.cs`
- `Source/Core/DiaryGameComponent.NarrativeContinuity.cs`
- `1.6/Defs/DiaryNarrativeContinuityDefs.xml`
- `tests/NarrativeContinuityTests/NarrativeContinuityTests.csproj`
- `tests/NarrativeContinuityTests/Program.cs`

### 14.2 Existing production files likely to change

- `Source/Models/DiaryEvent.cs`
- `Source/Models/ArchivedDiaryEntry.cs`
- `Source/Core/DiaryArchiveRepository.cs`
- `Source/Core/DiaryGameComponent.EventRetention.cs`
- `Source/Core/DiaryGameComponent.DaySummary.cs`
- `Source/Generation/DiaryPipelineAdapters.cs`
- `Source/Generation/DlcContext.cs` or DLC-grouped guarded partials following its one-home rule
- `Source/Pipeline/DiaryPipelineContracts.cs`
- `Source/Pipeline/DiaryPromptPlanner.cs`
- prompt-context selection/budget contracts
- `1.6/Defs/DiaryPromptTemplateDefs.xml`
- `Languages/English/Keyed/PawnDiary.xml`
- relevant DefInjected localization files
- `DOCUMENTATION.md`, `CHANGELOG.md`, `TEST_COVERAGE_PLAN.md`

### 14.3 Existing tests likely to change

- `tests/DiaryPipelineTests`
- `tests/DiaryCapturePolicyTests`
- `tests/PawnDiary.RimTest/PawnDiaryScribeRoundTripFixtureTests.cs`
- `tests/PawnDiary.RimTest/PawnDiaryRepositoryRebuildFixtureTests.cs`
- `tests/PawnDiary.RimTest/PawnDiaryReflectionTemplateFixtureTests.cs`
- `tests/PawnDiary.RimTest/PawnDiaryDlcSafetyFixtureTests.cs`
- source-specific DLC fixtures added by their implementation plans

## 15. Phased implementation sequence

Every phase is independently reviewable and buildable. Do not combine N0–N4 into one diff.

### Phase N0 — Freeze shared contracts and policy (no behavior)

1. Add plain contracts and stable token constants.
2. Add `DiaryNarrativeContinuityDef`, XML defaults, and plain policy snapshot conversion.
3. Implement selector/reference/reflection pure shells and the standalone test project.
4. Freeze save keys, arc-key grammar, category tokens, selection diagnostics, and old-save defaults.
5. Add no live providers, no save fields, and no prompt field.

Exit gate: pure tests cover hard gates, score precedence, category caps, stable tie-breaks, formatting
budget, reference equality, and reflection priority; XML parses; no RimWorld references enter tests.

### Phase N1 — Per-POV persistence and prompt seam

1. Add event/POV state, Scribe normalization, pipeline payload field, and archive copying/indexes.
2. Add the optional narrative-context prompt field and Full/Balanced/Compact budget behavior.
3. Implement `NarrativeContextBuilder` with a synthetic/core test provider only.
4. Verify a zero-candidate event produces byte-for-byte equivalent field omission.
5. Add save/load, archive rebuild, retention, prompt-capture, and no-DLC RimTests.

Exit gate: old and new saves load; archive compaction preserves bounded references; empty context is
invisible; selected context is event-time frozen; no real DLC behavior changes yet.

### Phase N2 — First real providers and source emissions

Implement alongside the first flagship DLC slice, recommended Biotech B1 and Odyssey O1:

1. Add Biotech family/identity provider and required B1 emissions.
2. Add Odyssey journey/home provider and departure/landing references.
3. Test family + journey combinations with exact location/knowledge gates.
4. Add Royalty/Ideology/Anomaly provider stubs that return empty until their relevant plans ship.
5. Document provider diagnostics and XML tuning.

Exit gate: a growth or family event aboard a committed gravship may select relevant home context; a
landing may select an exact family/home lens; unrelated colonists/events remain unchanged.

### Phase N3 — Ideology interpretation and remaining providers

1. Route high-confidence `EventRelativeStanceResolver` output through the shared interpretation
   category for ordinary event enrichment.
2. Add Royalty persona/title/court candidates with exact applicability gates.
3. Add Anomaly visible-state/pressure candidates with spoiler-firewall tests.
4. Add Biotech mechanitor/pollution and Odyssey environmental extensions as their source phases ship.
5. Verify two-lens composition and repetition across every provider pair.

Exit gate: every active provider can be absent independently; Ideology-off combinations still receive
other relevant continuity; no prompt exceeds the configured cap.

### Phase N4 — Reflection coordinator and linked memory selection

1. Introduce the pure reflection opportunity/plan layer.
2. Adapt the existing arc/quadrum/day flow without changing individual event truth.
3. Integrate Ideology belief reflection as one opportunity rather than a separate hardcoded branch.
4. Select archive/hot memories through exact references and bounded subject fallback.
5. Add cross-arc reflection prompt policy only if the existing arc template cannot express the linked
   before/after facts safely.

Exit gate: one rest yields at most one reflection; exact priority/cooldown/consumption tests pass;
linked memories survive compaction; no unrelated multi-DLC recap is possible.

### Phase N5 — Hardening, tuning, and release

1. Run the complete pure suite, RimTest suite, XML validation, and Debug build.
2. Test zero DLC, each DLC alone, representative two-DLC pairs, and all available DLCs active.
3. Profile provider invocation on minor events and verify cheap applicability gates.
4. Test save/load between source event and generation, after compaction, and during pending reflection.
5. Audit localization and prompt-capture output at every context detail level.
6. Update `DOCUMENTATION.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`, and rebuild/stage the DLL.

Exit gate: all definition-of-done and acceptance scenarios pass with no new warnings or prompt bloat.

## 16. Test matrix

### 16.1 Pure selector tests

- null/empty request and all hard-gate failures;
- exact arc > exact subject > topic > facet > ambient;
- unrelated terminal candidate loses to relevant meaningful candidate;
- knowledge false/unknown always rejected;
- category and detail-level caps;
- redundant text/topic collapse;
- deterministic score/tie order;
- repetition penalty and exact-continuation exception;
- age decay boundaries and future-tick rejection;
- character budget drops a unit rather than truncating it;
- missing/invalid XML values normalize to safe fallbacks.

### 16.2 Reference/archive tests

- stable arc/subject equality and case rules;
- per-POV knowledge isolation;
- Scribe null normalization and old-save empty defaults;
- hot -> archive copy and index rebuild;
- retention bounds reference history;
- same event/pawn/role dedup remains unchanged;
- no historical backfill or catch-up behavior.

### 16.3 Reflection tests

- exact priority chain;
- at most one selection;
- global/per-kind cooldowns;
- two linked phases qualify, two unrelated DLC memories do not;
- prior reflections excluded;
- failed dispatch does not consume state;
- disabled group advances/bounds debt;
- archive-only linked memory remains eligible.

### 16.4 RimTest combinations

- no DLC: every provider empty, save/prompt unchanged;
- Royalty + Biotech: royal mechanitor context only on authority/mech-relevant events;
- Ideology + Biotech: live stance may interpret growth/xenogerm/mechlink; unrelated faith omitted;
- Biotech + Odyssey: family/home context on departure/landing with exact crew/location gating;
- Royalty + Anomaly: psychic/authority lens only with matching visible ritual/chapter facts;
- Ideology + Anomaly: no hidden host/downside/outcome leaks;
- Ideology + Odyssey: space/home stance only when live doctrine structurally matches;
- all DLC: maximum lens cap, stable order, bounded prompt, no exception;
- save/load before queued generation proves event-time snapshot truth;
- compaction then reflection proves reference survival.

## 17. Performance and failure policy

- Do not enumerate every provider for events with no narrative evidence.
- Do not scan full ideologies, gene lists, bonds, maps, or archives on ticks or hot Harmony paths.
- Candidate and evidence lists are defensively capped before pure selection.
- Archive indexes provide arc/subject lookup; no full archive scan per prompt after N1.
- Cache only plain policy metadata, never live `Pawn`/`Def`/tracker objects.
- Reset static/transient provider caches in `FinalizeInit`.
- A provider exception is caught at its adapter boundary, logged once with its provider key, and the
  remaining candidates continue.
- A selector/formatter failure returns empty shared context and preserves the canonical page.

## 18. Definition of done

The Narrative Continuity Layer is complete only when:

- all contracts and tokens are documented and pure-tested;
- event sources remain the sole capture/page owners;
- every selected fact is per-POV, visible, event-time frozen, and bounded;
- zero/one/two lens selection follows XML policy deterministically;
- references survive save/load and archive compaction without unbounded state;
- the shared prompt field disappears cleanly when empty;
- Ideology is optional and other DLC combinations still integrate;
- all five DLC plans emit or schedule the required shared metadata;
- one reflection coordinator prevents duplicate/starved retrospective pages;
- no-DLC and every supported DLC combination run safely;
- relevant pure tests, RimTests, XML validation, and Debug build pass;
- `DOCUMENTATION.md`, `TEST_COVERAGE_PLAN.md`, `CHANGELOG.md`, localization, and committed DLL are
  current for every behavior slice.

## 19. Agent handoff checklist

Before starting a phase:

1. Read this document, the participating DLC plan phase, `AGENTS.md`, and the engineering skill.
2. Use CodeGraph to inspect the exact current symbols; plans describe responsibilities, not guaranteed
   future filenames.
3. Check the relevant local lore for persistence, prompt/UI, event capture, DLC context, or XML work.
4. Confirm no overlapping user changes in target files.
5. State the smallest phase plan and do not pull later DLC hooks into a shared-foundation diff.

Before finishing a phase:

1. Run focused pure tests and RimTests required by the phase.
2. Validate touched XML.
3. Run the Debug MSBuild build.
4. Inspect no-DLC gates, async event-time truth, save migration, prompt budgets, and dedup ownership.
5. Update documentation/changelog and rebuild the committed DLL when behavior changed.
6. Report changed files, validation results, and any deferred source-plan obligations.
