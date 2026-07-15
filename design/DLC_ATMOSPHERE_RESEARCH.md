# DLC atmosphere research — deeper diary support

> **Status:** research findings, 2026-07-15. No behavior is implemented by this document.
>
> **Scope:** Royalty, Ideology, Biotech, Anomaly, and Odyssey in RimWorld 1.6. The goal is not
> exhaustive Def coverage. It is to identify expansion systems that can make a pawn's diary more
> personal, continuous, and atmospheric without filling it with mechanical notifications.

## Executive conclusion

The best direction is to build deeper story arcs, not simply recognize more DLC Defs.

Each expansion has a distinct diary fantasy:

- **Royalty:** power, obligation, ambition, and a weapon that knows its wielder.
- **Ideology:** the pawn's moral interpretation of events.
- **Biotech:** family, inherited identity, and responsibility for created life.
- **Anomaly:** curiosity becoming knowledge, temptation, and irreversible cost.
- **Odyssey:** home becoming a vehicle, with every significant landing opening a new chapter.

If only one substantial feature were built for each expansion, the strongest choices would be:

1. Royalty — persona-weapon bond and loss.
2. Ideology — event-relative moral stance.
3. Biotech — growth moments connected to a continuing family arc.
4. Anomaly — containment, knowledge, and consequence progression.
5. Odyssey — gravship departure, journey context, and arrival chapters.

## Research method

The findings combine three evidence layers:

1. The official expansion descriptions and Ludeon development previews linked in
   [Sources](#sources).
2. The locally installed RimWorld `1.6.4871 rev590` DLC XML and read-only inspection of public game
   classes and lifecycle methods in `Assembly-CSharp.dll`.
3. Pawn Diary's current event ingestion, context, XML policy, progression scans, and existing
   design notes.

The local game data was important for verifying exact Def names and feasible lifecycle seams. It
also avoids assuming that a feature mentioned on a wiki still has the same 1.6 implementation.

## Current coverage audit

Pawn Diary's current DLC support is broader than a short feature list suggests, but most of it is
event-local rather than continuous:

| DLC | Current meaningful coverage | Main missing dimension |
|---|---|---|
| Royalty | royal-title changes, psylink progression, throne speeches, anima linking, royal thoughts | the lived experience of rank: persona weapons, permits, succession, and court pressure |
| Ideology | conversion interactions, roles, rituals, trials, slavery, and many relevant thoughts | event-specific moral interpretation and long belief arcs |
| Biotech | pregnancy, childbirth, teaching, birthdays, xenotype changes, hemogen, deathrest, and psychic-bond rupture | growth moments, family continuity, salient genes, mechanitor progression, and pollution |
| Anomaly | monolith discovery/activation, psychic rituals, major conditions, entity presences, cube effects, void states, and metalhorror safeguards | specific ritual meaning, study/containment progression, and visible reveal/payoff arcs |
| Odyssey | gravship launch, orbital debris, vacuum harm, and some environmental effects | travel lifecycle, destination context, exploration chapters, and the Mechhive ending |

[`DlcContext`](../Source/Generation/DlcContext.cs) currently provides xenotype, royal title,
ideoligion, ideological role, and two guarded Anomaly states. It provides no mechanitor, family,
salient-gene, persona-weapon, monolith-progress, or Odyssey journey context.

[`DiaryGameComponent.Progression`](../Source/Core/DiaryGameComponent.Progression.cs) scans skill
milestones, psylink level, xenotype changes, royal-title changes, and new traits. It does not scan
mechanitor progression, selected growth-moment passions, ideology conversion, monolith level, or
Odyssey travel.

Anomaly's ambient physical-horror coverage is already particularly strong in
[`DiaryObservedConditionDefs.xml`](../1.6/Defs/DiaryObservedConditionDefs.xml). The earlier
[`EVENT_COVERAGE_PLAN.md`](EVENT_COVERAGE_PLAN.md) was a useful event-by-event first pass, but many
of its proposals have now shipped. It should not be treated as the current gap list.

## Atmosphere selection model

The central design question for every proposed integration is:

> Why would this pawn remember this moment?

The answer should determine the treatment:

| Feature shape | Diary treatment |
|---|---|
| Identity permanently changed | dedicated page plus lingering prompt context |
| Relationship created, tested, or broken | paired perspectives or a continuing bond |
| Colony entered a new chapter | rare chapter page |
| Persistent environmental pressure | ambient observed condition, usually no page |
| Recurring bodily or social need | prompt shading and occasional escalation |
| Routine mechanical action | ignore or aggregate |
| Hidden information the pawn cannot know | never reveal it |

A new dedicated page should normally satisfy at least three of these tests:

- The pawn personally noticed or felt it.
- It changed identity, relationship, home, or future choices.
- It has a meaningful before and after.
- It is rare enough to remain memorable.
- It can be described as lived experience instead of a game mechanic.
- It does not reveal hidden state or future outcomes.

This rejects pages for honor ticks, every gene update, every constructed mech, every fish caught,
every meditation session, every deathrest cycle, or every weather tick.

## Royalty — power should feel personal

### Expansion story systems

Royalty centers on Imperial titles and honor, permits, psycasts and meditation, royal quests and
hospitality, mechanoid threats, prestige equipment, persona weapons, and Royal Ascent.

The current title and psylink scans recognize advancement, but they do not explain what rank feels
like or what the pawn sacrifices to hold it.

### Recommended support

#### 1. Persona-weapon relationship

This is the strongest Royalty opportunity. A persona weapon is equipment mechanically, but a
relationship narratively.

Candidate milestones:

- Initial bonding or coding.
- First consequential fight or kill together.
- The weapon reacting through its persona traits.
- Forced separation, loss, recovery, or transfer.
- Destruction of the weapon or death of its wielder.

The prompt should use only the weapon traits that affect the relationship. Jealous, kill-focused,
kind, calm, sorrowful, or demanding weapons should not all sound the same. Repeated ordinary kills
should not create pages.

The installed class `CompBladelinkWeapon` exposes plausible seams for coding, equip/loss, and kill
notifications. Any implementation should snapshot a plain bond payload at that edge rather than
passing the live weapon into the pure pipeline.

#### 2. Title and succession arc

Enrich the existing title-change event with:

- Previous and new title.
- Whether the title was granted or inherited.
- The pawn's prior social identity.
- Heir appointment and inheritance after death.
- New ceremonial or social expectations when relevant.

Do not record honor or favor transactions. They are progression currency, not memories.

#### 3. Dramatic permit use

Only permits with visible story consequence should qualify: military squads, transport shuttle,
orbital strike, or orbital salvo. They can be framed as spending status, calling in a debt, or
admitting the colony cannot cope alone.

Resource drops, labor teams, and cooldown changes should normally remain invisible.

#### 4. Psychic path identity

Psylink milestones should include a small, event-relevant meditation identity so an anima-linked
tribal psycaster does not sound like an Imperial throne psycaster. Routine meditation belongs in
ordinary context, not event capture.

#### 5. Royal Ascent chapter

The Stellarch's arrival, the pressure of hosting, attacks during the stay, and final ascent can form
a bounded colony arc. The royal pawn, protectors, servants, and ideological skeptics should receive
different perspectives.

#### 6. Court pressure as context

Unmet title expectations, decrees, and ceremonial obligations can shade ordinary entries. They
should not generate recurring complaint pages.

### Royalty anti-noise rules

- No honor/favor tick pages.
- No permit cooldown pages.
- No page for each meditation session or psycast.
- No list of every title entitlement or unmet need.
- Persona-weapon kills must be filtered for consequence and deduplicated with combat tales.

## Ideology — the same event should mean different things

### Expansion story systems

Ideology centers on memes, precepts, conversion, social roles, rituals, relics, slavery, moral
disagreement, and the Archonexus path.

Pawn Diary already sees many Ideology interactions, thoughts, and rituals. The missing piece is not
recognition. It is interpretation through the pawn's actual beliefs.

### Recommended support

#### 1. Event-relative stance resolver

Select one or two beliefs relevant to the current event instead of dumping every precept into the
prompt.

Examples:

- An organ operation asks about body modification and organ-use beliefs.
- A raid asks about violence, execution, charity, outsiders, or honorable combat.
- A meal asks about meat, cannibalism, fungus, or preferred foods.
- Darkness asks about light and darkness beliefs.
- A prisoner event asks about slavery, conversion, proselytizing, or execution.

The selection policy should be pure and XML-owned. `DlcContext` should only collect guarded Def-name
facts; a pure selector should choose the relevant stance from the event domain.

#### 2. Conversion and apostasy arc

Capture old faith, new faith, converter, and whether the transition appeared voluntary, gradual, or
coerced. Mechanical certainty percentages need not enter prose.

#### 3. Role assignment and removal

Becoming or ceasing to be a leader, moral guide, or specialist is an identity transition. It
deserves a page when the assignment changes, not whenever the role ability is used.

#### 4. Relic lifecycle

Relic discovery, recovery, loss, and destruction are bounded faith chapters. Ordinary relic
proximity should remain context.

#### 5. Minority-faith experience

When relevant to a social event, provide whether the pawn belongs to the colony's majority faith or
is isolated among believers of another ideology. Do not make generic faith disagreement dominate
unrelated pages.

#### 6. Archonexus migration

Leaving a developed settlement behind for ideological transcendence is a colony-ending sacrifice,
not a generic quest event. It warrants several perspectives and a final arc reflection.

### Ideology anti-noise rules

- Never include the whole precept list.
- Never expose raw certainty percentages in natural prose.
- Do not turn every mood thought into an ideological declaration.
- Use only beliefs related to the current event.
- Preserve disagreement: two pawns should be allowed to interpret the same event oppositely.

## Biotech — the highest-priority expansion

### Expansion story systems

Biotech adds reproduction and children, growth moments, genes and xenotypes, mechanitors and
controlled mechanoids, pollution, sanguophages, psychic bonds, and super-mechanoid progression.

These systems are especially suitable for diaries because they alter family, body, identity,
responsibility, and inheritance.

### Recommended support

#### 1. Growth moments

This is the single highest-value missing feature.

At ages 7, 10, and 13, the player chooses traits and passions based on the child's upbringing. A
growth-moment page should include:

- The selected trait.
- New passions or interests.
- The quality or breadth of the childhood opportunity without using UI jargon.
- What the child believes they are becoming.
- A parent or guardian perspective when appropriate.

The installed `ChoiceLetter_GrowthMoment.MakeChoices(List<SkillDef>, Trait)` method exposes the
actual completed selections. A postfix can capture a plain payload after the choice is committed.

This event must merge with the existing birthday and trait scans. One growth moment should produce
one strong page, not separate birthday, trait, passion, and work-unlock pages.

#### 2. Family continuity

Pregnancy, labor, childbirth, teaching, and birthdays are already individually visible. They need
to become one remembered family arc:

- Pregnancy expectations and fear.
- Birth from both parent and other-parent perspectives.
- Naming and family relationships.
- Infant illness or difficult postpartum recovery.
- Teaching and caregiving summarized over time.
- Growth moments reflecting the childhood that preceded them.

Crying, feeding, play, and lesson interactions should remain daily texture unless they accumulate
into a reflection.

#### 3. Gene identity instead of gene lists

The current progression scan records xenotype-label changes. Custom xenotypes remain narratively
thin unless the prompt understands their important genes.

Select two to four salient themes through XML policy:

- Bodily appearance.
- Aging and lifespan.
- Hunger, sleep, temperature, or environmental dependency.
- Violence or emotional disposition.
- A defining ability.
- Hemogen, deathrest, or another defining need.
- Social consequences.

A xenogerm implantation can then describe what meaningfully changed without reciting twenty stat
genes. Routine gene recalculation and hidden bookkeeping must not create entries.

#### 4. Mechanitor arc

Mechanitor support should remain centered on the human controller rather than making every mech the
protagonist.

Good milestones:

- Mechlink installation.
- First controlled mech.
- First significant combat fought through mechs.
- Loss of a named or long-serving mech.
- Calling and defeating Diabolus, War Queen, and Apocriton.
- Unlocking a new command tier.
- Losing the mechlink or control network.

The installed `Pawn_MechanitorTracker` exposes controlled pawns and bandwidth state, while the boss
game component exposes boss-group call and defeat notifications. Threshold and significance policy
should be XML-owned. Do not record each constructed mech or bandwidth fluctuation.

#### 5. Pollution as a changing home

Pollution should be an observed condition with thresholds:

- First meaningful contamination.
- Colony becoming dependent on polluted terrain.
- Escalation to illness or environmental collapse.
- Successful reclamation.

Wastepack creation and movement are logistics, not memories.

#### 6. Sanguophage and psychic-bond identity

Strong moments include implantation, transformation, the first severe hemogen crisis, interrupted
deathrest, bond creation, and bond rupture. Feeding and routine deathrest belong in prompt context.

### Biotech anti-noise rules

- No raw gene dump.
- No page for each feeding, lesson, play interaction, deathrest, or gestated mech.
- No bandwidth-delta pages.
- Merge growth moments with birthday and trait signals.
- Keep parents and children connected through stable relationship IDs rather than prompt prose
  alone.

## Anomaly — deepen knowledge and consequence

### Expansion story systems

Anomaly's central themes are paranoia, containment, study, temptation, psychic ritual, and the
choice to exploit what should perhaps remain unknown. Monster assaults are only the visible layer.

Pawn Diary already covers the physical horror set pieces well. The strongest additions concern the
colonists' participation in the horror.

### Recommended support

#### 1. Specific psychic-ritual identities

All Anomaly psychic rituals currently classify through the generic `ritualAnomalyPsychic` group in
[`DiaryInteractionGroupDefs.xml`](../1.6/Defs/DiaryInteractionGroupDefs.xml). Exact groups placed
before that fallback can give each ritual a moral and sensory identity without adding pages.

The installed 1.6 ritual Defs are:

| Rituals | Diary emphasis |
|---|---|
| `VoidProvocation` | curiosity and deliberately inviting something in |
| `ImbueDeathRefusal` | bargaining with mortality and accepting a cost |
| `Philophagy` | theft of skill, brain injury, invoker gain versus victim loss |
| `Chronophagy` | stolen youth and unequal time |
| `Psychophagy` | consumption or transfer of mind/personhood |
| `Brainwipe` | identity erasure and coercion |
| `PleasurePulse` | colony-wide psychic pleasure and compromised consent |
| `NeurosisPulse` | deliberate psychic distress and social cruelty |
| `SkipAbduction` / `SkipAbductionPlayer` | complicity in disappearance |
| `SummonAnimals` | drawing living creatures through the void |
| `SummonShamblers` | deliberately inviting the dead |
| `SummonPitGate` | knowingly opening a catastrophic breach |
| `SummonFleshbeasts` / `SummonFleshbeastsPlayer` | calling hostile flesh into the world |
| `BloodRain` | choosing a colony-wide bodily and environmental curse |

This is a low-effort, high-payoff XML improvement with zero additional page volume.

#### 2. Monolith chapter progression

Go beyond discovery and initial activation. The installed anomaly component exposes level changes
covering inactive, stirring, waking, gleaming, void-awakened, embraced, and disrupted states.

Only major transitions should produce pages. Study increments belong in the eventual chapter
summary, not separate entries.

#### 3. Knowledge milestones

Entity codex discoveries and meaningful study breakthroughs should belong to the researcher who
made them. The installed `EntityCodex.SetDiscovered` and study-unlock path offer better seams than
polling research progress.

#### 4. Containment breaches

A held entity escaping is an ideal rare page: responsibility, warning signs, panic, and consequence
have a clear before and after. The holding-platform target component exposes held, released, and
escape transitions.

An intentional release should not automatically be described as an accidental breach.

#### 5. Creepjoiner reveal arc

Record arrival, then only visible outcomes: revealed downside, rejection, aggression, or departure.
Never reveal the hidden downside before colonists can know it.

#### 6. Ghoul transformation

Treat transformation as a terminal identity boundary. The pawn may deserve a final
pre-transformation page, while relatives, the surgeon, or controller describe what remains
afterward.

#### 7. Final void outcome

Closing, disrupting, or embracing the void should be an arc conclusion with distinct moral tones.
The generic tale event is insufficient for the participants who made the choice.

### Anomaly anti-noise and spoiler rules

- The diary must never know what the pawn does not know.
- Never identify a hidden metalhorror host.
- Do not expose a creepjoiner's unrevealed downside.
- Do not record study ticks or containment strength changes.
- Merge breach, assault, and victim thoughts into one incident where they describe the same event.
- Preserve current uncertainty language around suspicion and unseen danger.

## Odyssey — write journeys, not isolated events

### Expansion story systems

Odyssey adds gravships as mobile colonies, new biomes and landmarks, orbit and vacuum survival,
exploration quests, wildlife and fishing, unique weapons, and the Mechhive endgame.

Its central diary fantasy is:

> Our home can leave, and each significant landing changes what home means.

Pawn Diary currently captures `GravshipLaunch` as a ritual, but no equivalent arrival chapter or
continuing journey context exists.

### Recommended support

#### 1. Gravship journey lifecycle

Add a landing/arrival chapter containing only facts the crew would know:

- Origin and destination.
- Orbit versus planetary surface.
- Biome, landmark, or quest-site category.
- Pilot and crew perspectives.
- Whether the journey was exploration, escape, salvage, combat, or homecoming.

The installed `WorldComponent_GravshipController` exposes takeoff and landing lifecycle methods.
`LandingEnded` is private, so any patch should be registered defensively through
`DiaryPatchRegistrar`, with target lookup and a warning if the method changes.

Frequent short hops should be aggregated or filtered. First orbit, first landing in a new biome,
and major quest sites deserve pages.

#### 2. Mobile-home context

Ordinary entries aboard a gravship should know that the pawn lives on a vessel, whether it is in
orbit or landed, and what kind of place lies outside. This is prompt context, not a separate page.

Keep the context human-readable. Fuel, engine statistics, cell counts, and every ship subsystem do
not belong in a diary prompt.

#### 3. Environmental atmosphere

The installed Odyssey condition and incident inventory includes:

- `BioluminescentSpores`
- `DarkenedSkies`
- `DeepFreeze`
- `Drought` / `DroughtInitial`
- `GillRot`
- `LavaFlow`
- `VolcanicAsh`
- `VolcanicDebris`
- `Windy`
- `SeasonalFlooding` as an incident

The current weather group matches `Flooding`, but the actual incident Def is `SeasonalFlooding`,
and persistent seasonal flood state is not a normal `GameCondition`. The present matcher therefore
cannot provide its intended flood atmosphere. Correct support needs an incident-specific rule or a
small seasonal-flood observer, not only a renamed GameCondition matcher.

`LavaFlow`, `VolcanicDebris`, drought, deep freeze, darkened skies, and bioluminescent spores are
strong ambient candidates. Fishing disease such as Gill Rot should matter only when fishing is an
actual food pillar for the pawn or colony.

#### 4. Vacuum and life-support crisis

Vacuum exposure is already represented when a pawn is injured. Add colony-wide urgency only when
pressure or life support is visibly failing. Normal life in orbit should feel strange and enclosed,
not constantly catastrophic.

#### 5. Exploration chapters

Good milestones include:

- First orbit.
- First landing in each new biome category.
- Recovery of a gravcore.
- A unique landmark, wreck, asteroid, station, or platform.
- Leaving a long-term settlement behind.
- Returning to a former home.

Do not create a page for every generated map or ordinary opportunity site.

#### 6. Mechhive ending

Destroying versus controlling the Mechhive is a major moral and strategic ending choice. It
deserves participant perspectives and an arc reflection afterward.

#### 7. Optional secondary stories

- A signature unique weapon can reuse the Royalty bond lifecycle without claiming it is sentient.
- A sentience catalyst used on a beloved animal is an identity transformation with owner and animal
  relationship consequences.
- Alpha thrumbo and hive-queen outcomes can receive exact quest framing.
- Fishing should remain quiet work texture or an occasional reflection, not page spam.
- `GravNausea` is a good low-cost prompt enchantment for the bodily experience of travel.

### Odyssey anti-noise rules

- No page for every launch, landing, fish, animal-training step, or minor site.
- Deduplicate gravship launch ritual and takeoff lifecycle signals.
- Do not frame normal orbit as an emergency.
- Prefer first-time, new-biome, long-journey, and major-purpose thresholds.
- Keep destination facts descriptive rather than exposing world-generation internals.

## Cross-DLC narrative primitives

The implementation should avoid five disconnected feature pipelines. Four small reusable story
primitives cover most high-value opportunities:

### `IdentityTransition`

Examples:

- Ideology conversion.
- Royal title or role.
- Xenotype or salient gene change.
- Mechlink installation.
- Ghoul transformation.
- Animal sentience.

Useful plain fields: subject, previous identity, current identity, cause, other pawn, voluntary
status when knowable, permanence, and perspective.

### `BondLifecycle`

Examples:

- Persona weapon and wielder.
- Mechanitor and long-serving mech.
- Psychic-bond partners.
- Colonist and sentient animal.
- Odyssey signature weapon without assuming sentience.

Useful phases: formed, tested, separated, recovered, broken, and ended.

### `JourneyChapter`

Examples:

- Gravship departure and arrival.
- Archonexus migration.
- Royal Ascent.
- Monolith progression and final void outcome.
- Major mechanitor boss tiers.

Useful fields: origin, destination/state, purpose, participants, outcome, novelty, and whether it
closes a prior chapter.

### `AmbientPressure`

Examples:

- Pollution.
- Odyssey biome/weather pressure.
- Vacuum danger.
- Anomaly dread and containment pressure.
- Royal or ideological social obligations.

These facts should usually tint ordinary prompts rather than generate pages.

The game-facing adapter should collect these into plain DTOs. Pure selectors should then decide
salience, perspective, merge behavior, and prompt facts. XML Defs should own thresholds, weights,
tones, cooldowns, and exact Def-name policy.

## Cross-DLC combinations

The expansions should be allowed to interact without dumping all DLC state into every prompt.

Examples:

- A royal mechanitor may treat mechs as servants, soldiers, or proof of status.
- An ideologically opposed pawn may see xenogerm implantation as liberation or desecration.
- A sanguophage parent may experience a child's aging very differently.
- A psycaster performing an Anomaly ritual may understand the psychic pressure but fear the source.
- A gravship landing can be homecoming for one pawn and exile for another.

For any event, choose at most one or two DLC facts that materially change its interpretation. Event
relevance should beat DLC novelty.

## Prioritized roadmap

| Priority | Feature | Atmosphere payoff | Expected implementation shape |
|---|---|---|---|
| 1 | Biotech growth moments and family continuity | exceptional | one completed-choice hook, DTO, pure merge/dedup policy, prompt context |
| 2 | Odyssey journey and landing chapters | exceptional | guarded landing hook, journey snapshot, novelty/cooldown policy |
| 3 | Anomaly ritual-specific guidance | high for low cost | XML-only exact groups before the generic ritual fallback |
| 4 | Royalty persona-weapon lifecycle | very high | bond/loss hooks, plain bond snapshot, combat deduplication |
| 5 | Biotech salient-gene context | very high | guarded `DlcContext` read, pure XML-driven salience selector |
| 6 | Biotech mechanitor milestones | high | mechlink and boss lifecycle capture, bounded thresholds |
| 7 | Anomaly monolith, study, containment, creepjoiner, and ghoul arcs | high | several rare lifecycle hooks sharing generic story DTOs |
| 8 | Odyssey conditions and Seasonal Flooding correction | medium-high | XML conditions plus a small flood observer if persistent tone is desired |
| 9 | Royal succession, dramatic permits, and Royal Ascent | medium-high | title/bond/chapter extensions with permit allowlist |
| 10 | Ideology stance and conversion plan | very high | proceed through the separate Ideology implementation plan |

### Suggested delivery phases

#### Phase A — prompt quality with little or no new page volume

- Split Anomaly psychic rituals into exact XML groups.
- Add event-relative Ideology stance selection.
- Add salient-gene context.
- Add Odyssey ship/location and exact environmental context.
- Correct the `Flooding` assumption.

#### Phase B — one flagship arc per under-covered DLC

- Biotech growth moment.
- Odyssey landing/arrival.
- Royalty persona-weapon bond/loss.

#### Phase C — progression and consequence

- Mechanitor boss tiers and meaningful mech loss.
- Monolith levels, entity discoveries, containment breaches, and ghoul transformation.
- Royal succession and dramatic permit use.
- Pollution and life-support pressure.

#### Phase D — endings and cross-DLC polish

- Mechhive choice.
- Royal Ascent.
- Archonexus departure.
- Void embrace/disruption.
- Cross-DLC event-relative fact selection.

## Implementation guardrails

All future implementation should follow the repository's existing safety rules:

1. Prefer exact Def-name string matchers in XML when the existing event hook already sees the
   feature.
2. Put new DLC pawn-state reads in `DlcContext`, double-guarded by `ModsConfig.<Dlc>Active` and
   null checks.
3. Pass plain typed snapshots into pure selection and merge logic; never pass live `Pawn`, `Def`,
   gravship, weapon, tracker, or anomaly objects through the pure pipeline.
4. Register fragile or private DLC lifecycle targets defensively through `DiaryPatchRegistrar`
   with a null check and warning.
5. Keep prompt policy, exact Def lists, thresholds, odds, cooldowns, and tones in XML.
6. Add pure tests for salience, merge, order, cooldown, novelty, and perspective selection.
7. Preserve no-DLC operation. Every feature must cleanly no-op when its content pack is inactive.
8. Localize all prompt prose through Keyed or DefInjected text according to
   [`DOCUMENTATION.md` §12](../DOCUMENTATION.md#12-localization).

## Deduplication requirements

The deeper support will fail if it produces multiple pages for one story moment. Explicit merge
rules are required for at least:

- Growth moment + birthday + new trait/passions.
- Xenogerm implantation + xenotype progression + added hediff/thought.
- Royal title inheritance + title progression scan.
- Persona-weapon kill + combat tale.
- Gravship launch ritual + takeoff lifecycle.
- Landing + quest/site arrival.
- Psychic ritual + participant/victim thoughts.
- Containment breach + entity assault + injury thoughts.
- Monolith level change + quest/tale completion.

The dedicated lifecycle event should normally own the page, while lower-level signals enrich its
context or are suppressed inside a short merge window.

## Success criteria

Deeper DLC support is successful when:

- A player can identify the expansion's emotional theme from diary entries without seeing a DLC
  label.
- The same mechanical event reads differently for pawns with different relationships, beliefs, or
  responsibilities.
- Major DLC arcs have a beginning, pressure, transformation, and resolution.
- Ordinary play produces richer context without a noticeable increase in page spam.
- Custom xenotypes and cross-DLC pawns remain understandable without prompt bloat.
- Anomaly entries never spoil hidden information.
- A no-DLC game runs cleanly and silently.

It is not successful merely because every DLC Def appears in a matcher list.

## Sources

Official expansion pages:

- [RimWorld — Royalty](https://rimworldgame.com/index.php/royalty)
- [RimWorld — Ideology](https://rimworldgame.com/index.php/ideology)
- [RimWorld — Biotech](https://rimworldgame.com/index.php/biotech)
- [RimWorld — Anomaly](https://rimworldgame.com/anomaly/)
- [RimWorld — Odyssey](https://rimworldgame.com/odyssey/)

Official feature previews used to identify the intended story focus:

- [Ideology adds social roles and rituals](https://ludeon.com/blog/2021/07/ideology-adds-social-roles-and-rituals/)
- [Biotech preview 1 — mechanitors and labor mechs](https://ludeon.com/blog/2022/10/biotech-preview-1-mechanitor-infrastructure-and-labor-mechs/)
- [Biotech preview 2 — combat mechs, pollution, and bosses](https://ludeon.com/blog/2022/10/biotech-preview-2-combat-mechanoids-pollution-and-super-mechanoid-bosses/)
- [Biotech preview 3 — reproduction, children, and genetics](https://ludeon.com/blog/2022/10/biotech-preview-3-reproduction-children-genetic-modification-release-date/)
- [Biotech preview 4 — xenotypes and sanguophages](https://ludeon.com/blog/2022/10/biotech-preview-4-xenotypes-world-factions-and-the-dark-blood-drinkers/)
- [Anomaly preview 2 — containment and creatures](https://ludeon.com/blog/2024/03/anomaly-preview-2-containment-facilities-creatures-and-release-date/)
- [Anomaly preview 3 — cultists and psychic rituals](https://ludeon.com/blog/2024/04/anomaly-preview-3-cultists-hate-chanters-and-rituals/)
- [Anomaly ambient horror support](https://ludeon.com/blog/2024/05/new-ambient-horror-setting-and-tribal-anomaly-support/)
- [Odyssey preview 1 — map features, landmarks, and biomes](https://ludeon.com/blog/2025/06/odyssey-preview-1-map-features-landmarks-and-biomes/)
- [Odyssey preview 2 — gravships and space](https://ludeon.com/blog/2025/06/odyssey-preview-2-gravships-and-space/)
- [Odyssey preview 3 — animals, training, and fishing](https://ludeon.com/blog/2025/07/odyssey-preview-3-animals-training-and-fishing/)
- [Odyssey preview 4 — quests and the Mechhive](https://ludeon.com/blog/2025/07/odyssey-preview-4-quests-the-mechhive-endgame-drones-and-more/)
