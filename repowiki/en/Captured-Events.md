# Captured events

Pawn Diary notices far more than social-log lines, but it does not treat every signal the same way. For exhaustive identifiers, triggers, and expected evidence, use the [event catalog](reference/Event-Catalog.md) and [observed-condition/window reference](reference/Observed-Conditions-and-Windows.md).

## Four ways a moment reaches the diary

| Mechanism | What the player experiences | Typical examples |
|---|---|---|
| Immediate game notification | Pawn Diary listens when RimWorld confirms that something happened. | social logs, Tales, mental states, incidents, abilities, quests, health changes |
| Scheduled state check | Pawn Diary periodically compares current state with the last stored state. | work, thought progression, health progression, skills, birthdays, anniversaries, beliefs |
| Multi-step correlation | Several related notifications are joined into one verified event. | birth and growth, psychic bonds, royal succession, permits, gravship journeys, anomaly surgery and outcomes |
| External submission | A supported bridge or another mod calls Pawn Diary's public API. | RimTalk, SpeakUp, Rimpsyche, first-party adapters, and third-party integrations |

The mechanism tells you how the facts arrive. The matched policy then decides whether they create an immediate page, wait in a batch, contribute to a reflection, or only affect later prompt context.

## Social, relationships, and beliefs

Captured families include ordinary conversations, compliments, insults, arguments, romance, breakups, proposals and marriage, relationship changes, recruitment, prisoner and slavery events, teaching, counseling, conversions, and rituals. Pair-capable routes preserve the participants' roles so each eligible pawn can write an independent account.

Compatibility groups add named interactions from supported social mods. Rich bridges can submit a whole displayed conversation or a particularly charged exchange rather than relying only on individual interaction identifiers.

## Inner life, work, and health

Pawn Diary can react to confirmed mental breaks, social fights, inspirations, new or progressing thoughts, broad colony mood events, and sampled work experiences. Health families cover injuries and illness, healing or worsening conditions, surgery, body-part changes, pregnancy, labor, birth, and selected long-running conditions.

Many of these signals are deliberately low-salience. A small work moment or health change may become day-reflection evidence instead of an immediate page, and most lasting observed conditions exist to alter context rather than create another notification-shaped page.

## Combat, colony life, and recorded deeds

Combat and Tale capture cover fights, injuries, kills, rescues, captures, deaths, surgery, hunting, crafting, art, research, construction, disasters, and quieter milestones that RimWorld records as Tales. Raids and infestations can fan out to admitted colonists on the affected map. Finished artwork can also correlate its generated art tale with an earlier colony deed and create a low-salience “immortalized in art” page.

Dedicated sources take ownership where a generic Tale would lose important facts. Duplicate detection then suppresses the redundant version instead of producing two pages about one occurrence.

## Membership, quests, abilities, and progression

The diary has explicit lifetime boundaries for arrivals and deaths. It also notices colony membership changes, quest phases, successful ability uses, birthdays, skill milestones, titles, genes, records, anniversaries, remembered losses, and scheduled day, quadrum, belief, and narrative-arc reflections.

Quest acceptance is **disabled by default**. Quest completion and failure are enabled. A player can change those group toggles in the Events settings tab, and an XML patch mod can change the default for players who have not already saved an override.

## Optional DLC families

- **Royalty:** titles, succession, permits, persona-weapon bonds and milestones, and Royalty-flavored quests or conditions.

- **Ideology:** rituals, conversions, roles, belief reflections, and Ideology interactions or thoughts.

- **Biotech:** growth moments, births and family POVs, genes and xenotypes, psychic bonds, deathrest, pollution, pregnancy, and labor.

- **Anomaly:** study breakthroughs, containment and escape episodes, visible transformations and terminal outcomes, plus lasting anomaly conditions used as context.

- **Odyssey:** gravship journeys, source-owned quest outcomes, Odyssey rituals, progression, incidents, and environmental conditions.

These families are optional. Core capture relies on base-game choke points, plain identifier matching, ownership checks, and guarded DLC state reads. If a DLC is absent, its content never matches or the guarded route remains inert; Pawn Diary does not require that DLC to load.

## What the outcome names mean

| Outcome | What to expect |
|---|---|
| Immediate page | An admitted event can start generation as soon as its verified signal is dispatched. |
| Batched page | Related moments wait inside a bounded pair, Tale, or ambient batch and are summarized when it flushes. |
| Reflection evidence | The fact is retained for a later scheduled reflection instead of making its own page. |
| Prompt-only observation | A lasting state can change the context or tone of another page but normally creates no page itself. |
| Event window | Start/end/timeout signals represent one bounded episode, such as a visit or threat, rather than one raw notification. |

Only a small subset of important observations and windows create their own transition pages; most observations are prompt-only. Even for page-producing routes, pawn eligibility, group settings, chance, pacing, deduplication, batching, lane readiness, and failures can all explain why no page appears.

Pair events always create separate POV pages for the eligible participants. They never create one shared omniscient entry.

## Compatibility and test detail

[Compatibility](reference/Compatibility.md) lists every XML slice and first-party adapter shipped in this repository, including dependency-absent behavior. Unknown third-party API users are intentionally not presented as a closed list.

For manual verification, start with:

- [runtime routes and all core groups](reference/Event-Catalog.md);
- [lasting conditions and bounded windows](reference/Observed-Conditions-and-Windows.md); and
- [shipped compatibility policies and adapters](reference/Compatibility.md).

## Source of truth

- Runtime registry: [DiaryEventCatalog](../../Source/Capture/Catalog/DiaryEventCatalog.cs).
- Immediate hooks: [Source/Patches](../../Source/Patches/).
- Scheduled and correlated capture: [Source/Core](../../Source/Core/), [Source/Ingestion](../../Source/Ingestion/), and [Source/Capture](../../Source/Capture/).
- Core classification: [DiaryInteractionGroupDefs.xml](../../1.6/Defs/DiaryInteractionGroupDefs.xml).
