# Implementation plan — SpeakUp support as an explicit adapter mod

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Goal: Pawn Diary
> currently supports SpeakUp *generically* (one core catch-all group + core capture-safety
> plumbing); this plan extracts the content support into a **dedicated, explicitly-installed
> adapter mod** `integrations/PawnDiary.SpeakUp/`, following the exact supersede pattern the
> RimTalk integration already shipped (core fallback group + `disableWhenPackageIdsLoaded` on
> the adapter's packageId — see `DOCUMENTATION.md` on `rimtalk_chatter`). **No public-API
> change; no new core mechanism.**

## Verified SpeakUp facts (2026-07-12 — re-verify About.xml/source at build time)

- packageId **`JPT.speakup`**, Workshop **2502518544**, name "SpeakUp", original author jptrrs.
  The original GitHub repo (`jptrrs/SpeakUp`) is abandoned at 1.3 (last commit 2021-08),
  **but the Workshop item was updated in place to 1.6** by the community fork
  `github.com/sergiodinapoli/SpeakUp` (1.6 update 2025-06-23, fixes through 2025-11-18), which
  **keeps the same packageId and Workshop ID**. There is no "(Continued)" re-upload and no
  packageId variant — everything keyed off `JPT.speakup` keeps working. Maintenance is real but
  low-intensity. Hard deps: Harmony + Interaction Bubbles (HugsLib was dropped in the fork).
- Def surface: **186 `InteractionDef`s and nothing else** (no RulePackDefs, no custom def
  classes), one abstract parent `SpeakUpReply`. Notable families (by defName): jokes
  (`Joke_00`–`Joke_15b`, `JokeReaction`), deep-talk conversations (`DeepTalkConvo`,
  `DeepTalkConvoResponse`, `MeaningOfLife`, `ChildhoodDiscussions`), prisoner chats
  (`Prisoner*` — `PrisonerRapport`, `PrisonerAccepts`, `PrisonerRefuses`, skill chats),
  thought reactions (`ReactToThought_*`, ~45 defs), greetings/smalltalk (`WhatsUp`,
  `HowAreYou`/`HowAreYou2`, `ChitChat_generic*`), weather remarks, dream/psychic-vision lines
  (`Dream_nice`, `PsyVision*`), romance replies (`RomanceSuccess/Fail`,
  `ProposalSuccess/Fail/Breakup`), `Animal_Reaction`, generic reply defs (`Slight_generic`,
  `Insult_generic`, `KindWords_generic`). It also patches extra `rulesStrings` into vanilla
  Chitchat/DeepTalk/romance/recruit rule packs (those stay vanilla defs — core capture already
  handles them; not this adapter's business).
- Conversation engine: dialogue lines carry `(tag=<defName>)`; a Harmony postfix on
  `GrammarResolver.TryResolveRecursive` calls **`SpeakUp.DialogManager.Ensue(List<string>)`**
  (still present, unchanged signature in the 2025 fork), which schedules delayed reply
  interactions (`Statement` → vanilla `TryInteractWith`). Runtime state is **public statics**
  on `DialogManager`: `List<Talk> CurrentTalks` (`Talk.nextInitiator/.nextRecipient/
  .latestReplyCount/.remainingReplies/.expireTick`), `List<Statement> Scheduled`
  (`Emitter`, `Reciever` [sic], `IntDef`, `Talk`, `Timing`), `lastInteractionDef`.
  **None of it is saved** — talks are transient and pruned every 60 ticks.
- Mod settings (public statics on `SpeakUpSettings`): `linesPerConversation` (default 3),
  `ticksBetweenLines` (default 60), etc.

## Current generic support in this repo (the extraction inventory)

| Piece | Where | Fate |
|---|---|---|
| `speakup_chitchat` group — order 18, `matchPackageIds: JPT.speakup`, AmbientDayNote batch (`syntheticDefName` `SpeakUpAmbientDay`, `minEventsToWrite` 8) + promotion block | `1.6/Defs/DiaryInteractionGroupDefs.xml` (~line 84) | **Becomes the core fallback**: move to `1.6/Defs/Compat/DiaryCompat_SpeakUp.xml`, unchanged content, plus the two gates below |
| `teaching` group's `disableWhenPackageIdsLoaded: JPT.speakup` | same file (~line 556) | **Stays in core, untouched this pass.** Its rationale is undocumented — likely SpeakUp reply defs colliding with `teaching`'s blunt `matchTokens` (Lesson/Teaching/Baby). Record a follow-up: once the adapter claims all SpeakUp defs at low order, this blunt gate may be narrowable. Do not change it in this pass. |
| `SpeakUpReplySchedulingGuardPatch` (reflection patch on `DialogManager.Ensue`, suppression scope during PlayLog text rendering) | `Source/Patches/SpeakUpReplySchedulingGuardPatch.cs`, registered in `DiaryPatchRegistrar.cs:24`, used in `DiarySocialLogPatches.cs:75` | **Stays in core.** This is capture *safety*, not content support: Pawn Diary must not trigger SpeakUp replies while rendering log text, whether or not the adapter is installed. It is reflection-only and inert without SpeakUp — it already follows the core string-matcher rule. |
| `ShouldRenderInteractionTextFromPlayLog` consulting `CanRenderTaggedGrammarSafely` + `HasTaggedLogGrammar` walk | `Source/Core/DiaryGameComponent.Interactions.cs:36-60` | **Stays in core** (generic tagged-grammar handling, not SpeakUp-specific). |
| English/Russian DefInjected strings for `speakup_chitchat` | `Languages/*/DefInjected/PawnDiary.DiaryInteractionGroupDef/DiaryInteractionGroupDefs.xml` | Move the entries into a new `DiaryCompat_SpeakUp.xml` DefInjected file alongside the def move (keys are per-defName, so translations survive the file move verbatim). |

## Step 1 — core change: fallback + supersede gate

Move `speakup_chitchat` verbatim into `1.6/Defs/Compat/DiaryCompat_SpeakUp.xml` and add:

```xml
<enableWhenPackageIdsLoaded><li>JPT.speakup</li></enableWhenPackageIdsLoaded>
<disableWhenPackageIdsLoaded><li>aimmlegate.pawndiary.adapter.speakup</li></disableWhenPackageIdsLoaded>
```

- `enableWhenPackageIdsLoaded` is a cleanup the group never had: today it appears in the
  settings Events tab even without SpeakUp (matchers just never fire) — verify that claim
  in-game first; either way the gate is correct and matches every other compat group.
- **Continuity rules:** keep defName `speakup_chitchat` (players' per-save toggles are keyed
  by it) and keep `syntheticDefName SpeakUpAmbientDay` (stored on saved diary events). Old
  saves keep displaying their pages regardless (pages are self-contained), but keeping both
  names means a no-adapter player sees zero behavior change from this refactor.
- The header comment documents the supersede contract, mirroring the `rimtalk_chatter` wording
  in `DiaryCompat_RimTalk.xml`.
- Update `DOCUMENTATION.md`'s compat section (it currently describes SpeakUp as ambient
  batching in core) and `EVENT_PROMPT_MAP.md` if it lists the group's file location.

## Step 2 — the adapter (`integrations/PawnDiary.SpeakUp/`)

```
integrations/PawnDiary.SpeakUp/
  About/About.xml                        packageId aimmlegate.pawndiary.adapter.speakup
                                         (matches the Vsie/Personalities naming convention);
                                         modDependencies: Pawn Diary + SpeakUp; loadAfter both
  1.6/Defs/DiaryCompat_SpeakUp_Groups.xml   Tier 1 groups (below)
  1.6/Defs/DiaryExternalGroups_SpeakUp.xml  claims the Tier 2 eventKey
  Source/PawnDiarySpeakUp.csproj         net472; refs PawnDiary.dll + Harmony; SpeakUp access
                                         via reflection helpers (see Tier 2 note)
  Source/SpeakUpBridgeMod.cs             settings: Tier 2 toggle (default on), talk-capture
                                         threshold; PatchAll skipped unless SpeakUp active
                                         (clone VsieBridgeMod's guard)
  Source/TalkCapture.cs                  Tier 2 hook + submission
  Source/Pure/TalkSummaryFormat.cs       pure talk → summary text/decision logic (test-covered)
  Languages/English/Keyed/…              settings labels
  Languages/Russian (Русский)/…          Keyed + DefInjected for all groups
tests/SpeakUpBridgeLogicTests/           console harness, mirrors VsieBridgeLogicTests
```

### Tier 1 — richer XML groups

All gated `enableWhenPackageIdsLoaded: JPT.speakup` (the adapter hard-depends on SpeakUp, but
the gate keeps defs inert if a player force-loads the adapter alone). **New defNames
throughout** — never reuse `speakup_chitchat`: the superseded core def still loads (the
disable gate deactivates it, it does not unload it), so a same-name def would be a duplicate-
defName error. New `syntheticDefName`s for the same reason.

| Group | Order* | Matchers | Shape / intent |
|---|---|---|---|
| `speakupbridge_deeptalk` | 5 | `matchDefNames: DeepTalkConvo, DeepTalkConvoResponse, MeaningOfLife, ChildhoodDiscussions, Dream_nice, PsyVision, PsyVisionGood, PsyVisionBad` | Pairwise `AmbientDayNote` batch with a **generous promotion block** (clone `vsie_vent`'s, `baseChance` 0.02) — a meaning-of-life talk or shared dream deserves pages more often than chitchat. Instruction: a conversation that went below the surface. |
| `speakupbridge_jokes` | 6 | `matchPrefixes: Joke_` + `matchDefNames: JokeReaction` | Ambient batch, high `minEventsToWrite` (6). Instruction: the day's running comedy — who was funny, who groaned. |
| `speakupbridge_prisoner` | 7 | `matchPrefixes: Prisoner` | Ambient batch per pair, `minEventsToWrite` 3. Instruction: talking through the bars — rapport, wariness, the strange intimacy of guarding someone. Verify prisoner-recipient capture (same check as the VIE interrogation group; if prisoners aren't capture-eligible as partners, the initiating colonist's solo view still works). |
| `speakupbridge_reactions` | 8 | `matchPrefixes: ReactToThought` | Ambient batch folded high (`minEventsToWrite` 8) — these fire constantly; they are texture, not events. |
| `speakupbridge_chatter` | 10 | `matchPackageIds: JPT.speakup` | The catch-all for the remaining ~120 defs (weather, WhatsUp, generic replies, romance replies — deliberately not their own group: the underlying romance *event* is already captured by core; the replies are commentary). Content = the old core group verbatim (same instruction/tone/batch/promotion values, new defName `speakupbridge_chatter`, new `syntheticDefName SpeakUpBridgeAmbientDay`). |

\* Orders 5–10 sit below every core and compat Interaction group currently in use (11+) so the
specific families claim before anything else; re-check occupancy against all shipped compat
files at build time.

`captureRenderedGameText`: `true` on all groups — the core guard patch makes rendering safe,
and the rendered line is the whole point (SpeakUp's text is the evidence). The existing
comment on the old group documents this dependency; carry it over.

### Tier 2 — whole-conversation capture (settings toggle, default on)

Today every SpeakUp reply lands as a separate PlayLog row and batches as disconnected
fragments. The adapter can capture the **conversation as one unit** using `DialogManager`'s
public state:

- **Hook:** Harmony postfix on `DialogManager.FireStatement` (fires per delivered reply).
  Track per-`Talk` accumulation in the adapter (participants, tick span, line count, up to N
  sampled rendered lines — reuse what the Tier 1 capture already renders rather than
  re-rendering). On talk completion (`remainingReplies == 0`) or expiry
  (`Find.TickManager.TicksGame > expireTick`, checked by a cheap per-interval sweep), submit
  **one** external event:
  `SubmitEvent { eventKey = "speakupbridge_conversation", subject = initiator, partner =
  recipient, summaryText = <localized "a back-and-forth of N exchanges about ...; sampled
  lines> }`, claimed by a `domain=External` group in `DiaryExternalGroups_SpeakUp.xml`.
- **Threshold:** only talks with `latestReplyCount >= 3` (XML/settings-tunable) submit — a
  two-line exchange is already served by Tier 1 batching. eventKey is save-data: never rename.
- **Double-count control:** when Tier 2 is enabled, suppress the same rows from Tier 1
  ambient batches the simple way — Tier 2's submission replaces nothing structurally; instead
  raise no flags and accept that the ambient note may also mention fragments. If testing shows
  visible duplication, the in-adapter setting flips the Tier 1 reply groups
  (`speakupbridge_chatter` etc.) to disabled via the event-filter API
  (`SetEventFilterEnabled`, ApiVersion 3) — decide from observed output, record the decision
  in the file header.
- **Reflection, not assembly reference:** unlike 1-2-3 Personalities (a stable published API),
  SpeakUp's fork has no versioning discipline and its statics changed hands twice; bind
  `DialogManager`/`Talk`/`Statement` members via cached `AccessTools` lookups with a one-time
  "Tier 2 disabled, SpeakUp internals changed" warning on any miss (the
  `SpeakUpReplySchedulingGuardPatch` in core is the pattern). Talk state is transient and
  unsaved — on load, in-flight accumulations are dropped silently.

## Step 3 — release wiring

- Add the adapter to `scripts/publish.ps1` packaging the same way the other adapters are
  built/deployed (note: `DOCUMENTATION.md` references `scripts/deploy-integrations.ps1`, which
  does not exist in the repo — fix the doc or the script name while touching this).
- `integrations/README.md`: add the adapter row (target mod, packageId, tiers, settings).
- CHANGELOG entry describing the supersede semantics: SpeakUp alone = unchanged fallback;
  SpeakUp + adapter = richer groups + conversation capture; adapter alone = inert.

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. **SpeakUp, no adapter (regression):** behavior byte-for-byte identical to today — ambient
   SpeakUp note still writes, promotion still fires, the settings toggle keyed
   `speakup_chitchat` still holds its saved value, guard patch still suppresses replies during
   render (no reply-storm on capture).
2. **SpeakUp + adapter:** core fallback absent from the Events settings tab (disable gate);
   five adapter groups present; a ≥3-reply conversation produces one
   `speakupbridge_conversation` entry; deep-talk promotion produces standalone pages; no
   unclaimed-key warning.
3. **Adapter, no SpeakUp:** loads inert (groups gated, `PatchAll` skipped, no warnings).
4. **Pre-migration save** (has `SpeakUpAmbientDay` events + a `speakup_chitchat` toggle):
   loads clean in both configurations; old pages display.
5. **Both + gravship/caravan edge:** trigger conversations with pawns leaving the map
   mid-talk (the fork's known NRE zone, fixed upstream 2025-11) — Tier 2 must drop the talk
   gracefully, never throw.
6. Duplication review: read a full in-game day's diary with Tier 2 on; decide the
   double-count-control question above.

## Risks / open questions

- **Fork churn:** `DialogManager` internals changed maintainers twice; Tier 2 is
  reflection-bound and self-disabling, Tier 1 and the core fallback are pure defName/packageId
  matching and survive anything short of a packageId change (verified: none planned — the fork
  deliberately keeps `JPT.speakup`).
- **The teaching gate** (`disableWhenPackageIdsLoaded: JPT.speakup` on core `teaching`)
  predates this plan and its rationale is unrecorded. Follow-up ticket, not this pass:
  reproduce why it exists (likely `matchTokens` collisions with SpeakUp reply defs), and if
  the adapter's low-order claims make it obsolete, narrow it so vanilla lesson capture returns
  for SpeakUp users.
- **186-def churn:** family matchers are prefix-based where the fork's naming is consistent
  (`Joke_`, `Prisoner`, `ReactToThought`); the deep-talk list is enumerated — re-dump it at
  build time and note the verification date in the file header.
- SpeakUp's own settings (`linesPerConversation` up to 5) change conversation length; Tier 2
  thresholds must be tested at 1 and at 5 lines-per-conversation.
