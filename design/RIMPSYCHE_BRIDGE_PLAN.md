# Implementation plan — Rimpsyche bridge (`integrations/PawnDiary.RimpsycheBridge/`)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #1 in
> [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Shipped behavior lands in
> `../repowiki/README.md`; the API contract is `../EXTERNAL_API.md` / `../INTEGRATIONS.md`.
> **Nothing here extends the public API** — every piece reuses a shipped mechanism, and the
> whole adapter is a structural clone of `integrations/PawnDiary.PersonalitiesBridge/`.

## Target mod facts (verified from source 2026-07-12 — re-verify before coding)

- **Rimpsyche – Personality Core**, author Maux, packageId `Maux36.Rimpsyche`, Workshop
  3535112473, source `github.com/jagerguy36/Rimpsyche`. **1.6-only**, Harmony only, very active
  (v1.0.41, 2026-07-03).
- Personality model: 15 hidden facets aggregate into **34 numeric nodes** (−1..1), each a
  `PersonalityDef` (custom def class) with defNames `Rimpsyche_Talkativeness`,
  `Rimpsyche_Sociability`, `Rimpsyche_Tact`, `Rimpsyche_Playfulness`, `Rimpsyche_Openness`,
  `Rimpsyche_Inquisitiveness`, `Rimpsyche_Imagination`, `Rimpsyche_Reflectiveness`,
  `Rimpsyche_Confidence`, `Rimpsyche_Bravery`, `Rimpsyche_Diligence`, `Rimpsyche_Organization`,
  `Rimpsyche_Discipline`, `Rimpsyche_Focus`, `Rimpsyche_Tension`, `Rimpsyche_Emotionality`,
  `Rimpsyche_Stability`, `Rimpsyche_Spontaneity`, `Rimpsyche_Passion`, `Rimpsyche_Morality`,
  `Rimpsyche_Compassion`, `Rimpsyche_Trust`, `Rimpsyche_Loyalty`, `Rimpsyche_Authenticity`,
  `Rimpsyche_Ambition`, `Rimpsyche_Tenacity`, `Rimpsyche_Expectation`,
  `Rimpsyche_Competitiveness`, `Rimpsyche_SelfInterest`, `Rimpsyche_Propriety`,
  `Rimpsyche_Aggressiveness`, `Rimpsyche_Experimentation`, `Rimpsyche_Optimism`,
  `Rimpsyche_Deliberation`. **Re-verify this list against the installed
  `1.6/Defs/PersonalityDefs/Personalities.xml` at build time** — it is version-dependent.
- Public modder API (documented in the repo wiki, "For Modders"): `CompPsyche` ThingComp on
  pawns → `.Personality.GetPersonality(PersonalityDef)` float, `.Sexuality`, `.Interests`,
  `.Enabled`; `PsycheDataUtil.GetPsycheData(pawn)`; conversation-outcome hook
  `InteractionHook(Pawn initiator, Pawn recipient, Topic convoTopic, float alignment,
  float initOpinionOffset, float reciOpinionOffset)` — **verify exact signatures against the
  installed DLL/source before writing calls; treat the wiki as intent, the assembly as truth.**
- Own def surface: InteractionDefs `Rimpsyche_Smalltalk`, `Rimpsyche_StartConversation`,
  `Rimpsyche_Conversation`, `Rimpsyche_ConversationAttempt`; ThoughtDefs `Rimpsyche_Smalltalk`,
  `Rimpsyche_ConversationOpinion`, `Rimpsyche_ConvoIgnored`.
- Declares `incompatibleWith` Psychology (unofficial), 1-2-3 Personalities, Personality Plus —
  so this bridge never coexists with the Personalities bridge in one save; no arbitration
  needed beyond the normal source-owned psychotype override rules.

## Ground truth to read first (in this order)

1. `../design/MOD_COMPAT_PLAN.md` — the toolkit table and the standing isolation rule.
2. `integrations/PawnDiary.PersonalitiesBridge/` — **the template this plan clones**: About.xml
   shape, `Source/` layout (`BridgeIds.cs`, `EnneagramSync.cs`,
   `PawnDiaryPersonalities123Mod.cs`, `Personalities123GameComponent.cs`, `Source/Pure/`),
   Languages layout (English Keyed + Russian Keyed/DefInjected), csproj.
3. `integrations/PawnDiary.RimTalkBridge/Source/PersonaSync.cs` — the debounced
   apply/reset/override choreography (`AppliedPersonaHash`, `TouchedPawns()` sweep).
4. `1.6/Defs/DiaryInteractionGroupDefs.xml` header comment + the shipped compat files
   (`integrations/PawnDiary.Vsie/1.6/Defs/DiaryCompat_VSIE.xml`,
   `integrations/PawnDiary.PersonalitiesBridge/1.6/Defs/DiaryCompat_123Personalities.xml`) for
   group XML idiom.
5. `EXTERNAL_API.md` — `RegisterPawnContextProvider`, `SetPsychotypeOverride` /
   `ResetPsychotypeOverride`, `SubmitEvent` + External group contract, ApiVersion gating.

## Deliverables

```
integrations/PawnDiary.RimpsycheBridge/
  About/About.xml                          modDependencies: Pawn Diary + Rimpsyche; loadAfter both
  Source/PawnDiaryRimpsyche.csproj         net472; refs PawnDiary.dll, Harmony, Rimpsyche assembly (<Private>false</Private>)
  Source/BridgeIds.cs                      sourceId + eventKey constants
  Source/PawnDiaryRimpsycheMod.cs          settings: Tier B toggle (default on), Tier C toggle (default on)
  Source/RimpsycheGameComponent.cs         drives the debounced sync loop (clone Personalities123GameComponent)
  Source/PsycheSync.cs                     Tier A provider + Tier B psychotype override (clone EnneagramSync)
  Source/ConversationCapture.cs            Tier C: Harmony postfix on InteractionHook → SubmitEvent
  Source/Pure/PsycheLensMapping.cs         pure node-vector → outlook-rule mapper (test-covered)
  Source/Pure/PsycheSummaryFormat.cs       pure node-vector → context-line formatter (test-covered)
  1.6/Defs/DiaryCompat_Rimpsyche.xml       Tier 1 XML groups (below)
  1.6/Defs/DiaryExternalGroups_Rimpsyche.xml  claims the Tier C eventKey
  Languages/English/Keyed/…                settings labels, outlook rule strings, summary fragments
  Languages/Russian (Русский)/…            Keyed + DefInjected, native register (see localization note)
tests/RimpsycheBridgeLogicTests/           console harness, mirrors tests/Personalities123BridgeLogicTests
```

Standing rule: the **core mod gets zero changes**. A real assembly reference to Rimpsyche is
allowed only inside this adapter (its API is public and intended for this — same call as the
1-2-3 Personalities decision). Wrap every Rimpsyche-typed method in
`[MethodImpl(MethodImplOptions.NoInlining)]` behind an active-mod guard, exactly like
`EnneagramSync`.

## Tier 1 — XML groups (`1.6/Defs/DiaryCompat_Rimpsyche.xml`)

Both groups gated `enableWhenPackageIdsLoaded: Maux36.Rimpsyche`; matchers are plain strings.

| Group | Domain | Order | Matchers | Shape |
|---|---|---|---|---|
| `rimpsyche_conversations` | Interaction | 11 (verify unused; must sit below core `romance`=20 and compat 12–18) | `matchPrefixes: Rimpsyche_` | Clone `speakup_chitchat`/`vsie_vent`: `AmbientDayNote` batch (`windowTicks` 60000, `minEventsToWrite` 6, `maxSampleLines` 3, `syntheticDefName` RimpsycheAmbientDay, shared `PawnDiary.Event.AmbientSocial*` keys) **plus** a promotion block cloned from `vsie_vent` — Rimpsyche conversations carry topic/alignment weight, so a charged one may deserve its own page. Instruction theme: an exchange that went somewhere — ease, friction, or a topic that stuck; never a transcript. |
| `rimpsyche_thoughts` | Thought | 479 (below core 490+, unique vs. 480/481) | `matchPackageIds: Maux36.Rimpsyche` | No `<batch>` (Thought groups only theme; the global mood-offset policy is the flood guard — see the note in `DiaryCompat_VSIE.xml`). Instruction theme: the afterfeel of a conversation — an opinion shifting, being ignored. |

`captureRenderedGameText`: keep `true` unless in-game verification shows Rimpsyche schedules
work during grammar rendering (RimTalk needed `false`; test this explicitly — trigger 20+
conversations with the bridge active and watch for reentrancy errors).

## Tier A — context line (always on while bridge active)

- One `RegisterPawnContextProvider` registration at startup (clone the registration site from
  `EnneagramSync`). Provider returns a single line, e.g.
  `psyche=blunt, fearless, restless; interests=machines, stories`.
- Composition rule (in `PsycheSummaryFormat`, pure): take the 3 nodes with the largest
  `|value|` above a 0.35 magnitude floor, map each to one **bucketed adjective** per sign
  (34×2 lookup table, localized via Keyed with English fallback); append up to 2 interests if
  the Interests tracker exposes labels. Raw floats never reach the prompt. Return empty string
  (provider contract: line omitted) when `CompPsyche` is missing or `.Enabled` is false.
- Provider must be **read-only and allocation-light**: it runs inside every prompt build.
  Cache the formatted line per pawn keyed by a hash of the node vector (see Tier B hashing) and
  invalidate on hash change.

## Tier B — psychotype override (settings toggle, default on)

Clone the `EnneagramSync` / `PersonaSync` Tier B choreography verbatim:

- Map the node vector to a 1–2 sentence outlook rule in `PsycheLensMapping` (pure): pick the
  **dominant axis pair** (two largest-magnitude nodes from disjoint clusters — cluster the 34
  nodes into ~6 families in a static table: social, mind, drive, emotion, moral, order) and
  emit the rule from a families×sign lookup (write rules in the register of
  `PSYCHOTYPE_PLAN.md`'s appendix catalog: behavioral, mechanics-free, node names never echoed;
  localized with English source + Russian translation like the Enneagram rules).
- Apply via `PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.SourceId, rule)`.
- Change detection: hash the rounded node vector (2 decimals) — the analogue of
  `AppliedPersonaHash`. Re-apply only on hash change; debounce with the same cadence
  `Personalities123GameComponent` uses.
- Reset-on-toggle-off and reset-on-new-game must walk the same broad pawn set as
  `PersonaSync.TouchedPawns()` (colonists incl. dead/kidnapped — copy the enumeration).
- Guard: feature-detect `PawnDiaryApi.ApiVersion` for every member used, per `EXTERNAL_API.md`.

## Tier C — conversation-outcome capture (settings toggle, default on)

The one capability no other personality mod offers: Rimpsyche's `InteractionHook` carries the
conversation **topic** and a signed **alignment** (how well it went).

- Harmony **Postfix** on the hook method (locate it precisely in the installed assembly first;
  if the signature differs from the wiki, adapt — match by name + parameter shape, and if it
  cannot be found, log one warning and disable Tier C gracefully; never hard-fail load).
- Filter: both pawns diary-eligible; `|alignment|` above an XML-tunable threshold so only
  charged conversations submit (target ≲2 submissions/pawn/day worst case — the API's rolling
  token budget is the backstop, not the primary guard).
- Submit `PawnDiaryApi.SubmitEvent(new ExternalEventRequest { sourceId = BridgeIds.SourceId,
  eventKey = "rimpsyche_conversation", subject = initiator, partner = recipient,
  summaryText = <localized "talked about {topic}; it {landed well/badly}"> })` — build the
  summary from topic label + bucketed alignment only; no raw floats, no invented quotes.
- Claim the key in `1.6/Defs/DiaryExternalGroups_Rimpsyche.xml`: one `domain=External` group,
  `matchDefNames: rimpsyche_conversation`, instruction themed on a conversation that mattered —
  what the topic stirred, where the two people ended up relative to each other. eventKey is
  save-data: **never rename after ship**.
- Dedup: the pipeline's own dedup plus a per-pair in-adapter cooldown (mirror the gathering
  bridge's approach in `VsieGatheringBridge`).

## Localization

- English: inline def strings + `Languages/English/Keyed/PawnDiaryRimpsyche.xml` (settings,
  rule strings, adjective table, summary fragments).
- Russian: native-register translations (rules from `MOD_COMPAT_PLAN.md`: «поселенец» never
  «пешка», capitalized group labels, the core's `тема: пиши…` instruction pattern; see the
  Personalities bridge RU files and `Languages/Russian (Русский)/GLOSSARY.md`). DefInjected for
  both def files' label/instruction/instructions/tone/tones; Keyed for everything the C# emits.
  The outlook rules follow the Enneagram pattern: per-rule translation key resolved through
  RimWorld's Keyed system with English source fallback.

## Tests (`tests/RimpsycheBridgeLogicTests/`)

Console harness, RimWorld-free, mirroring `tests/Personalities123BridgeLogicTests`:
- `PsycheLensMapping`: every families×sign cell yields a non-empty rule; dominant-pair
  selection is deterministic and stable under tiny value jitter (hysteresis via rounding);
  hash function stable across runs.
- `PsycheSummaryFormat`: floor filtering, top-3 selection, empty-comp → empty string,
  no raw floats in output.
- Tier C filter: threshold gating and per-pair cooldown as pure functions.

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. Rimpsyche + Pawn Diary + bridge: trigger conversations; confirm the ambient day-note
   appears, a high-alignment conversation produces a `rimpsyche_conversation` entry, and no
   unclaimed-key warning fires.
2. `GetPsychotype` reflects the override; toggle Tier B off → sweep clears it; new game →
   cleared. Same choreography as the RimTalk/Personalities Tier B smoketest cases.
3. Bridge without Rimpsyche in the mod list: loads inert, no errors, groups not listed as
   capturable, Tier C logs its single disabled-gracefully line at most once.
4. Rimpsyche without the bridge: core untouched (no core change exists to verify — assert no
   PawnDiary log lines mention Rimpsyche).
5. Save with override applied → remove bridge → load: no errors; psychotype falls back per the
   core's normal override-source-missing behavior.

## Risks / open questions

- **Wiki-vs-assembly drift**: all signatures must be re-verified against the installed
  v-current DLL; the hook is the most likely to shift. Tier C degrades to disabled, never
  breaks load.
- **Node list churn**: the 34-node list is data; missing/renamed `PersonalityDef`s must be
  tolerated (skip unknown defNames — never index by position).
- Rimpsyche's companion modules (Disposition/Sexuality/Relationship) are **out of scope** for
  v1; the sexuality tracker is deliberately not read (content-tone risk, and its module's
  Workshop status is unverified).
- Order slots 11/479 must be confirmed free against all shipped compat files at build time.
