# Pawn Diary — DLC support master implementation order

> **Status:** authoritative execution schedule; no production behavior is changed by this document.
>
> **Purpose:** this document dictates **what is implemented next and in what order**. The individual
> Royalty, Ideology, Biotech, Anomaly, Odyssey, and Narrative Continuity plans remain the technical
> authority for their own contracts, hooks, policies, tests, and acceptance scenarios. They do not
> independently choose release order.
>
> **Conflict rule:** when documents disagree about scheduling, this master order wins. When they
> disagree about how a source-specific feature works, the source-specific plan wins. When they
> disagree about shared evidence, selection, references, prompt context, or reflection coordination,
> `DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md` wins.

## 1. How an implementation agent uses this plan

1. Start at Wave 0 and find the first wave whose exit gate is not proven by current code, tests,
   documentation, and changelog.
2. Complete only that wave's next listed slice. Each subordinate phase is still a separate smallest
   safe change unless this plan explicitly groups an XML-only change.
3. Do not begin a later wave because its hook looks easier or its code is nearby.
4. After every slice: run its focused tests, validate touched XML, update `DOCUMENTATION.md` and
   `CHANGELOG.md`, run the Debug build, and rebuild/stage `1.6/Assemblies/PawnDiary.dll` when C# changed.
5. Record the subordinate phase and master wave in the changelog, for example
   `Master Wave 3 / Biotech Phase 2`.
6. When a wave is already partly implemented, verify every missing exit-gate item; do not assume that
   a class or Def name means the behavior shipped.
7. Stop at a release checkpoint for review/in-game smoke testing before proceeding to the next wave.

Parallel implementation is **off by default**. Two agents may work simultaneously only when their
files, save contracts, XML order ranges, and event ownership do not overlap and both slices depend
only on a completed earlier wave. Merge and validate them one at a time in this plan's order.

## 2. Global rules for every wave

- Source-specific capture owns truth, page authorization, fan-out, and duplicate suppression.
- Shared facets are metadata, never generic page-producing event types.
- New DLC reads stay in guarded `DlcContext` accessors or their source-owned guarded adapter.
- All prompt-visible facts are copied on the main thread at event time before async generation.
- Tunable policy and prompt prose live in XML with safe fallbacks and correct localization.
- Every new pure decision has a standalone pure test.
- Every save change has old-save normalization, no-catch-up behavior, and Scribe round-trip coverage.
- Every DLC slice proves inactive/no-DLC behavior, not merely compilation.
- One gameplay action produces one canonical page.
- No wave may broaden scope to an unverified ending, hidden Anomaly state, or guessed DLC outcome.

## 3. Dependency spine

These dependencies are absolute:

```text
N0 shared contracts
  -> N1 persistence/prompt seam
     -> first source event persistence
        -> source provider/emissions (N2/N3 slices)
           -> linked reflections (N4)
              -> global hardening (N5)
```

Exceptions:

- Anomaly A0.0–A0.1 is XML-only classifier/prompt work and may ship after N1 without an Anomaly
  provider.
- A source's pure-contract phase may be prepared after N0 while N1 is being reviewed, but its live
  persistence or page source cannot land before N1.
- Existing shipped DLC behavior continues unchanged; this order governs the new standalone plans.

## 4. Master wave summary

| Wave | Deliverable | Subordinate phases | Release checkpoint |
|---|---|---|---|
| 0 | Freeze shared architecture | Narrative N0 | No behavior; contract review |
| 1 | Land shared persistence/prompt seam | Narrative N1 | Infrastructure release |
| 2 | Correct Anomaly semantics cheaply | Anomaly A0.0–A0.2 | Prompt-quality release |
| 3 | Biotech family flagship | Biotech 0–4 + Narrative N2-B | B1 release |
| 4 | Odyssey journey flagship | Odyssey O1.0–O1.5 + Narrative N2-O | O1 release |
| 5 | Royalty persona/title flagship | Royalty 0–4 + Narrative N3-R | R1 release |
| 6 | Biotech identity/mechanitor depth | Biotech 5–6 + Narrative N3-B | B2 core release |
| 7 | Anomaly knowledge/reveal depth | Anomaly A1.0–A2.2 + Narrative N3-A | A1/A2 release |
| 8 | Odyssey environmental depth | Odyssey O2 + Narrative N3-O | O2 release |
| 9 | Royalty succession/permits/ascent | Royalty 5–7 + Narrative N3-R extension | R2/R3 release |
| 10 | Ideology stance/conversion foundation | Ideology 0–3 + Narrative N3-I | Belief enrichment release |
| 11 | Secondary bonds and terminal chapters | Biotech 7–8, Anomaly A3.0, Odyssey O3 | Feature-complete DLC sources |
| 12 | Unified reflection system | Narrative N4 + Ideology 4–5 | Reflection release |
| 13 | Full compatibility and hardening | Royalty 8, Ideology 6, Narrative N5 | Final integrated release |

The `N2-B`, `N2-O`, and `N3-*` labels are provider-specific delivery slices of the broader Narrative
Continuity phases. They do not create separate public phase names or duplicate shared infrastructure.

## 5. Wave 0 — freeze shared architecture

### Implement

Complete Narrative Continuity **Phase N0**:

1. Plain evidence, reference, candidate, selection, and reflection contracts.
2. Stable facet, phase, subject, category, salience, knowledge, and arc-key tokens.
3. `DiaryNarrativeContinuityDef` schema and safe XML fallbacks.
4. Pure selector/reference/reflection policy shells.
5. Standalone `NarrativeContinuityTests` project.
6. Save keys and old-save empty semantics on paper; no save fields yet.

### Do not implement yet

- No `DiaryEvent`/archive fields.
- No prompt-template field.
- No live DLC provider or hook.
- No source-specific page.

### Exit gate

- N0 pure tests pass without RimWorld assemblies.
- XML policy parses.
- Every individual DLC plan's required evidence maps to the contracts without adding a fifth facet.
- Review freezes token spelling and arc-key grammar.

## 6. Wave 1 — land shared persistence and prompt seam

> **Implementation status (2026-07-15): complete.** Narrative N1 now provides only shared save,
> archive-index, and optional prompt infrastructure; it does not add a real DLC provider, hook, or
> source-owned page. Wave 2 now consumes that seam only for exact source-owned monolith evidence.

### Implement

Complete Narrative Continuity **Phase N1**:

1. Per-POV evidence, references, selected keys, and `narrativeContext` persistence.
2. Archive copy/index/rebuild and retention behavior.
3. Pipeline payload and optional first-person prompt field.
4. Full/Balanced/Compact budgets.
5. `NarrativeContextBuilder` with no real DLC provider.
6. Synthetic/core fixture proving zero, one, and two selected lenses.
7. Old-save, archive, prompt-capture, and no-DLC RimTests.

### Exit gate

- Empty shared context produces unchanged prompt-field omission.
- Save/load and hot-to-archive compaction preserve bounded references.
- No real DLC behavior changes.
- Debug build and complete existing tests pass.

### Release checkpoint 1

Ship/review the shared infrastructure alone. Do not hide foundational save/prompt regressions inside
the first DLC feature diff.

## 7. Wave 2 — Anomaly semantic precision

> **Implementation status (2026-07-15): complete.** A0.0 reconfirmed all 16 installed psychic
> ritual keys and the four relevant monolith levels against local RimWorld 1.6 data; A0.1 added six
> exact package-gated ritual families plus the retained generic fallback; A0.2 split Stirring,
> Waking, and Void Awakened into mutually exclusive windows and attached visible source-owned N1
> chapter references. No provider, new Harmony hook, hidden Anomaly state, or extra page source was
> added. The next permitted implementation slice is Wave 3 / Biotech Phase 0.

### Implement in this exact order

1. **Anomaly A0.0** — reconfirm installed keys, classifier ownership, group orders, and package gates.
2. **Anomaly A0.1** — add the six exact psychic-ritual XML groups and DefInjected text.
3. **Anomaly A0.2** — split monolith discovery/activation chapters using existing verified routes.
4. Attach shared evidence to A0.2 only where N1's event-time truth is available; A0.1 remains XML
   classification rather than a new provider.

### Exit gate

- Ritual families classify exactly; generic fallback remains last.
- No settings rows appear without Anomaly.
- Monolith chapters deduplicate existing routes.
- No hidden entity, host, downside, or terminal outcome enters evidence.

### Release checkpoint 2

This is the low-cost prompt-quality release. Do not continue into A1 study/containment yet.

## 8. Wave 3 — Biotech family flagship

> **Implementation status (2026-07-15): in progress.** Biotech Phase 0 is complete: B1 contracts,
> pure policy, stable event/arc/save/context keys, XML policy/groups, localization, and legacy settings
> inheritance are frozen with no live hook or page behavior. The next permitted slice is Biotech
> Phase 1 / growth observation and ordinary birthday fallback.

### Implement in this exact order

1. **Biotech 0** — freeze growth/family DTOs, policy, event IDs, settings migration, and shared arc keys.
2. **Biotech 1** — observe growth birthdays and preserve the ordinary fallback.
3. **Biotech 2** — persist family arcs and select truthful supporter/parent POVs.
4. **Narrative N2-B** — add the Biotech family/identity provider and required evidence emissions.
5. **Biotech 3** — canonical birth/naming ownership and duplicate suppression.
6. **Biotech 4** — B1 compatibility, no-DLC, save/load, prompt, and release hardening.

### Required acceptance story

A growth moment produces one composite page, connects to the correct saved family arc, and can carry
one relevant shared lens. It never reveals predicted genes/xenotype or invents a parent's feelings.

### Exit gate

- Growth choice + birthday + trait/passion changes merge into one page.
- Birth/labor/naming ownership is exact.
- Family arc IDs survive save/load and archive compaction.
- Biotech inactive is silent.

### Release checkpoint 3 — B1

Release family/growth before genes, mechanitors, pollution, or secondary Biotech stories.

## 9. Wave 4 — Odyssey journey flagship

### Implement in this exact order

1. **O1.0** — reconfirm signatures, ownership, IDs, journey arc grammar, and old-save semantics.
2. **O1.1** — pure location, novelty, history, cooldown, writer, and context policy.
3. **O1.2** — guarded location capture, journey/history persistence, mobile-home context.
4. **O1.3** — state-only takeoff/travel/landing lifecycle hooks; no pages yet.
5. **Narrative N2-O** — Odyssey journey/home provider and departure/landing references.
6. **O1.4** — landing page plus corrected launch truth and deduplication.
7. **O1.5** — hardening and delivery.

### Required cross-DLC acceptance story

With Biotech active, a family event aboard a committed gravship may select exact mobile-home context,
and a meaningful landing may select an exact family/home lens. Either DLC alone remains complete.

### Exit gate

- Ritual owns ceremony; travel commits state; landing owns arrival.
- Routine hops update history without pages.
- Pilot/copilot/crew selection is bounded.
- Quest-site landing never claims quest completion.
- Save/load cannot create a false first journey or landing.

### Release checkpoint 4 — O1

Do not add seasonal flooding, life support, or Mechhive choices in this wave.

## 10. Wave 5 — Royalty persona and progression flagship

### Implement in this exact order

1. **Royalty 0** — pure persona/title/psylink contracts and policy.
2. **Royalty 1** — guarded collection, persistence, and silent baseline.
3. **Narrative N3-R core** — persona/title provider plus required bond/identity evidence.
4. **Royalty 2** — persona lifecycle pages.
5. **Royalty 3** — first consequential kill and death integration through existing owners.
6. **Royalty 4** — immediate title/psylink cause correctness and scanner fallback.

### Exit gate

- Persona bond epoch and weapon identity are stable.
- Formation/separation/recovery/end pages do not duplicate combat/death pages.
- Title changes preserve faction, before/after, and cause.
- Bestowing/anima/neuroformer paths do not double-write progression.
- Royalty inactive is silent.

### Release checkpoint 5 — R1

Do not include succession, permits, or Royal Ascent yet.

## 11. Wave 6 — Biotech identity and mechanitor depth

### Implement in this exact order

1. **Biotech 5** — salient-gene context using the XML-driven pure selector.
2. Extend **N3-B** with exact identity candidates and repetition policy.
3. **Biotech 6** — mechlink, first controlled mech, first consequential mech combat, significant loss,
   and boss-tier mechanitor lifecycle.
4. Extend **N3-B** with mechanitor bond/chapter references.

### Exit gate

- Prompts receive themes, not gene dumps.
- Custom xenotypes work without hardcoded vanilla catalogs.
- Routine mech creation/bandwidth churn stays silent.
- Boss and meaningful-loss ownership is exact and bounded.

### Release checkpoint 6 — B2 core

Pollution, psychic bonds, and interrupted deathrest remain deferred to Wave 11.

## 12. Wave 7 — Anomaly knowledge, containment, and reveal

### Implement in this exact order

1. **A1.0** — pure contracts, policy Def, knowledge/containment planners, and shared token mapping.
2. **A1.1** — Anomaly catalog route/persistence and **N3-A** provider skeleton.
3. **A1.2** — study/knowledge milestones.
4. **A1.3** — exact containment breach ownership.
5. **A1.4** — A1 hardening/delivery.
6. **A2.0** — visible creepjoiner state and old-save baseline.
7. **A2.1** — surgical visible disclosure/outcome ownership.
8. **A2.2** — ghoul transformation.
9. Extend **N3-A** only with visibly authorized chapter, pressure, and identity candidates.

### Exit gate

- Study/containment/reveal events have exact visible evidence.
- Deferred Tale transactions fail open.
- No hidden state reaches prompt, save metadata, diagnostics, or reflection candidates.
- Anomaly inactive is silent.

### Release checkpoint 7 — A1/A2

Terminal void outcomes remain deferred to the endings wave.

## 13. Wave 8 — Odyssey environmental depth

### Implement

Complete **Odyssey O2** and extend **N3-O**:

1. Seasonal Flooding correction through the existing observer model.
2. Audit visible active conditions before adding bespoke observers.
3. Add bounded `GravNausea` context.
4. Add negative landing consequences only after the exact outcome hook is verified.
5. Keep life-support crisis deferred unless its separate spike proves a safe visible seam.

### Exit gate

- Environmental pressure is location-scoped and bounded.
- Generic visible-condition context is not duplicated.
- No recurring flood/weather page spam.
- No guessed life-support behavior.

### Release checkpoint 8 — O2

## 14. Wave 9 — Royalty succession, permits, and ascent

### Implement in this exact order

1. **Royalty 5** — exact inheritance correlation and succession evidence.
2. **Royalty 6** — successful dramatic permit families and friendly-raid ownership.
3. **Royalty 7** — Royal Ascent start, bounded pressure, and terminal chapter.
4. Extend **N3-R** with succession identity, permit authority, Ascent journey, and court-pressure
   candidates.

### Exit gate

- Succession names deceased/heir only from exact vanilla facts.
- Routine permits remain silent.
- Ascent produces beginning, bounded pressure, and one exact ending.
- No arrival/outcome is claimed before its verified signal.

### Release checkpoint 9 — R2/R3

Royalty Phase 8 final compatibility remains in Wave 13 so it covers the completed shared system.

## 15. Wave 10 — Ideology stance and conversion foundation

### Implement in this exact order

1. **Ideology 0** — complete pure correlation/lexical resolver and belief policy.
2. **Ideology 1** — guarded live snapshot and saved event-time belief source.
3. **Narrative N3-I** — adapt high-confidence stance output into the single shared
   `interpretation` candidate category.
4. **Ideology 2** — conversion/crisis/ritual/speech mutation capture and required event integrations.
5. **Ideology 3** — bounded passive certainty/ideology tracking for future reflection evidence.
6. Complete the common N3 selector/provider tests across Royalty, Biotech, Anomaly, and Odyssey.

### Exit gate

- Structural live correlations beat lexical fallback.
- Ambiguous/unrelated beliefs return empty.
- Ordinary enrichment consumes the shared interpretation slot and obeys the global two-lens cap.
- Other DLC combinations still integrate with Ideology inactive.
- No standalone belief reflection yet.

### Release checkpoint 10 — belief enrichment

This deliberately ships useful event-relative belief context before reflection scheduling.

## 16. Wave 11 — secondary bonds and terminal source chapters

Implement these independent source slices in this exact order, with a build/release-quality gate after
each one:

1. **Biotech 7** — pollution pressure/escalation through observed-condition policy.
2. **Biotech 8** — psychic-bond lifecycle and interrupted deathrest.
3. **Anomaly A3.0** — exact terminal void outcome and source-owned reflection evidence only.
4. **Odyssey O3** — verified major exploration/Mechhive source and choice facts only; unresolved hooks
   stay explicitly deferred.
5. Add/extend N3 provider emissions for each verified source slice.

### Exit gate

- Every supported source arc has exact beginning/change/ending references where facts permit.
- Unsupported life-support/Mechhive/optional stories remain absent rather than guessed.
- N3 is complete for every implemented provider.

### Release checkpoint 11 — feature-complete sources

No new immediate DLC page sources are added after this checkpoint until the integrated system ships.

## 17. Wave 12 — unified reflections

### Implement in this exact order

1. **Narrative N4** — pure `ReflectionCoordinator`, opportunity contracts, linked-memory selection,
   archive lookup, cooldowns, and consumption rules.
2. Adapt existing arc/quadrum/day scheduling to the coordinator without changing source truth.
3. **Ideology 4** — belief-reflection page and scheduling as one coordinator opportunity.
4. Connect only already-planned source-owned terminal reflection evidence from Royalty, Biotech,
   Anomaly, and Odyssey.
5. **Ideology 5** — tune automatic coverage and exceptional corrections after real combined prompt
   fixtures exist.

### Exit gate

- One rest opportunity creates at most one reflection.
- Major arc -> linked cross-arc -> belief -> quadrum -> day priority is deterministic.
- Two unrelated DLC memories cannot form a cross-arc reflection.
- Failed dispatch does not consume pending state.
- Disabled groups cannot accumulate a catch-up flood.

### Release checkpoint 12 — reflections

## 18. Wave 13 — global compatibility and final hardening

### Implement in this exact order

1. **Royalty 8** — compatibility and release hardening against the final shared layer.
2. **Ideology 6** — compatibility, documentation, and release hardening.
3. **Narrative N5** — global tuning, performance, save migration, localization, and combination matrix.
4. Re-run every source-specific hardening scenario whose shared prompt/reflection behavior changed.
5. Update `DOCUMENTATION.md`, `TEST_COVERAGE_PLAN.md`, `EVENT_PROMPT_MAP.md` where applicable,
   localization, and `CHANGELOG.md`.
6. Rebuild and stage the committed DLL.

### Required final matrix

- zero DLC;
- each DLC alone;
- Royalty + Biotech;
- Ideology + Biotech;
- Biotech + Odyssey;
- Royalty + Anomaly;
- Ideology + Anomaly;
- Ideology + Odyssey;
- all available DLCs active;
- save/load before generation, after compaction, and with pending reflection;
- Full/Balanced/Compact prompt capture;
- DLC group settings disabled/re-enabled without backlog;
- no hidden Anomaly or unverified ending facts.

### Final exit gate

- All pure projects and RimTest requirements pass.
- All XML parses.
- Debug build succeeds and committed DLL is current.
- No-DLC operation is clean.
- Prompt lens cap, repetition, archive references, and reflection priority are proven.
- Every subordinate plan's definition of done is either met or explicitly marked deferred with its
  unverified source reason.

## 19. Things an agent must not reorder

- Do not land source save fields before N1.
- Do not implement Odyssey O2/O3 before O1 is released.
- Do not implement Royalty succession/permits/ascent before R1 persona/title correctness.
- Do not implement Anomaly A1/A2 before the A0 classifier/chapter baseline.
- Do not implement Biotech gene/mechanitor breadth before B1 family/growth hardening.
- Do not add belief reflections before N4.
- Do not add terminal/cross-arc reflections while immediate source ownership is incomplete.
- Do not run N5 early; every new provider would invalidate its combination matrix.
- Do not let an attractive optional story displace the current wave.

## 20. Blocker and deferral policy

If a wave hits an unverified private hook or missing visible truth:

1. Complete safe pure/XML/state work that does not depend on the hook.
2. Record the exact unresolved signature/state question in the source plan.
3. Defer only that sub-feature; do not invent a fallback claim.
4. Continue within the same wave only when the remaining slice is independently shippable and its
   exit gate can still be truthful.
5. Do not jump to a later master wave unless the current wave's release checkpoint explicitly permits
   the deferred item.

Examples already authorized for deferral are Odyssey life-support, unverified Mechhive choice state,
and any exact player-facing heir-appointment source that cannot be distinguished from automatic
fallback assignment.

## 21. Completion record template

Use this block in the implementation PR/task summary for every slice:

```text
Master wave:
Subordinate phase:
Behavior shipped:
Behavior explicitly deferred:
Save/XML tokens added or changed:
Dedup/page owner:
No-DLC result:
Pure tests:
RimTests:
XML validation:
Debug build:
Documentation/changelog:
Next permitted slice:
```
