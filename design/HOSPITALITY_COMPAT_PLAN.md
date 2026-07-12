# Implementation plan — Hospitality compat (XML pack + optional small adapter)

> Status: planned 2026-07-12; not implemented. Written for a coding agent. Ranked #3 in
> [`MOD_SUPPORT_RESEARCH.md`](MOD_SUPPORT_RESEARCH.md). Phase 1 is **pure XML in the core mod**
> (no C#, no API change). Phase 2 is an optional micro-adapter for the "guests are here"
> context line — ship Phase 1 first; Phase 2 is independently droppable.

## Target mod facts (verified from source 2026-07-12 — re-verify against the installed 1.6 Defs)

- **Hospitality**, author Orion, packageId `Orion.Hospitality`, Workshop 753498552, source
  `github.com/OrionFive/Hospitality`, versions 1.0–1.6 (community-maintained; Orion accepts
  PRs). The community fork "Hospitality (Continued)" (Workshop 3509486825) **keeps the same
  packageId**, so every gate/matcher below covers both distributions automatically.
- InteractionDefs: `GuestDiplomacy` (improve relationship), `CharmGuestAttempt`,
  `ScroungeFoodAttempt`.
- Memory ThoughtDefs: `GuestClaimedBed` (7 price stages), `GuestAngered`,
  `GuestRecruitmentForced`, `GuestOffendedRelationship`, `GuestPleasedRelationship`,
  `EndorsedByRecruiter`, `GuestDismissiveAttitude`, `GuestExpensiveFood`, `GuestCheapFood`.
  Situational: `GuestCantAffordBed`, `GuestHasNoFood`, `GuestBedCount`.
- IncidentDefs: `VisitorGroup`, `VisitorGroupMax`, `VisitorGroupSelectFaction`
  (FactionArrival category), `HappyGuestJoins` (Misc category).
- **No** TaleDefs/HediffDefs/MentalStateDefs/PawnRelationDefs. defNames share **no reliable
  prefix** — match by exact defName or packageId, never by prefix.

## The perspective problem (design constraint for every group below)

Most Hospitality thoughts land on the **guest**, and guests are not diary-eligible (not
colonists). What is capturable from the colonist side:

- Interactions where a **colonist initiates** on a guest (`GuestDiplomacy`,
  `CharmGuestAttempt`) — the colonist is the interaction initiator, so normal Interaction
  capture applies to the colonist's diary; the pair partner (guest) simply has no diary.
  **Verify early**: trigger `GuestDiplomacy` in-game and confirm the capture pipeline records
  for the initiating colonist when the recipient is a non-colonist guest. If pairwise capture
  requires both pawns eligible, this is the one blocking finding — report back and fall back to
  ambient solo notes.
- The two guest thoughts that are *social thoughts about a colonist*
  (`GuestOffendedRelationship`/`GuestPleasedRelationship`) are on the guest → **not capturable;
  skip.**
- `HappyGuestJoins` (a guest joins the colony) — a colony moment; captured as an event window.
- `EndorsedByRecruiter` — verify in the installed XML **who** holds this thought; capture only
  if it lands on a colonist.

## Ground truth to read first

1. `1.6/Defs/DiaryInteractionGroupDefs.xml` — `recruit` group (order 30) is the closest core
   sibling in tone; Interaction compat orders in use: 12–18; `other` catch-all 150; Thought
   compat orders 479–487 (check all shipped compat files for collisions).
2. `1.6/Defs/DiaryEventWindowDefs.xml` + display-group idiom (`eventWindowMechCluster`,
   orders 137–144) — `HappyGuestJoins` and `VisitorGroup` are **not raid-like**
   (`Source/Patches/DiaryEventSignalPatches.cs` `IsRaidLikeIncident`), so incidents route via
   `source=Incident, signal=executed` event windows, not the Raid domain.
3. `integrations/PawnDiary.Vsie/` — the shape for Phase 2 (adapter folded next to XML, tiny
   assembly, `PatchAll` skipped unless target active) and the arrival/gathering instruction
   register.

## Phase 1 deliverables (core mod, pure XML)

```
1.6/Defs/Compat/DiaryCompat_Hospitality.xml       interaction + thought groups
1.6/Defs/Compat/DiaryEventWindows_Hospitality.xml two incident windows + display groups
Languages/Russian (Русский)/DefInjected/…         RU for all new defs
```

All defs gated `enableWhenPackageIdsLoaded: Orion.Hospitality`.

### Groups (`DiaryCompat_Hospitality.xml`)

| Group | Domain | Order | Matchers | Shape / instruction intent |
|---|---|---|---|---|
| `hospitality_guestwork` | Interaction | 17 | `matchDefNames: GuestDiplomacy, CharmGuestAttempt` | `AmbientDayNote` batch cloned from `vsie_teaching` (`windowTicks` 60000, `minEventsToWrite` 4 — visits are shorter than a day, keep the bar lower than VSIE's 6, `maxSampleLines` 3) **plus** a light promotion block cloned from `vsie_vent` so one charged charm attempt can surface alone. Instruction: working a guest — hospitality as labor and theater; the colonist's read of the outsider; never invent the guest's answer. |
| `hospitality_scrounge` | Interaction | 17 (same group file; if the classifier requires unique orders, use 18 after verifying `speakup_chitchat`'s 18 is core-RimTalk-gated — safest: 19) | `matchDefNames: ScroungeFoodAttempt` | Not batched, `important=false`. Rare and characterful: a guest caught scrounging — irritation or pity. **Verify the colonist is a participant** (recipient) before shipping; if only guests participate, drop this row. |
| `hospitality_thoughts` | Thought | 485 | `matchDefNames: EndorsedByRecruiter` + any other thought verified to land on colonists (audit the installed XML; do **not** use matchPackageIds — most of the package's thoughts are guest-held or bookkeeping) | Theming only. Instruction: the afterglow or bruise of hosting — reputation among outsiders. |

### Event windows (`DiaryEventWindows_Hospitality.xml`)

| Window | Signal | recordScope | Instruction intent |
|---|---|---|---|
| `HospitalityGuestsArrived` | `source=Incident, signal=executed, matchDefNames: VisitorGroup, VisitorGroupMax, VisitorGroupSelectFaction` | SubjectPawn | strangers inside the walls: curiosity, commerce, wariness; `dedupTicks` ≥ 30000 so back-to-back groups don't double-write |
| `HospitalityGuestJoined` | `source=Incident, signal=executed, matchDefNames: HappyGuestJoins` | SubjectPawn | someone chose us: what it says about the colony that an outsider stayed |

Each with a display group (Interaction domain, orders 148–149, labels "Guests arrived" /
"A guest joined"), `recordStartEvent=true`, `timeoutTicks=-1`, `promptEnabled=false`.
Consider `promptEnabled=true, promptWeight=2` on `HospitalityGuestsArrived` with a short
`promptDecayTicks` (~30000) as a cheap "guests are here" tint substitute — decide in testing;
if it works, Phase 2 shrinks or dies.

## Phase 2 (optional) — guest-presence context line

Only if the event-window tint above proves insufficient in testing:

```
integrations/PawnDiary.Hospitality/
  About/About.xml                       loadAfter Pawn Diary + Hospitality; modDependency on both
  Source/PawnDiaryHospitality.csproj    net472, refs PawnDiary.dll only — NO Hospitality reference
  Source/GuestPresenceProvider.cs       RegisterPawnContextProvider; counts guests reflectively
  1.6/…                                 nothing (XML stays in core)
```

- Provider returns `guests=3 visiting (Outlander union)` while guest pawns are on the pawn's
  map, else empty. Detect guests **without referencing Hospitality types**: a humanlike,
  non-hostile, non-colonist spawned pawn whose `CompGuest` comp's type FullName starts with
  `Hospitality.` — one cached `Type` lookup by string, the same string-matcher spirit as
  `VsieGatheringMap`. Cache per map per ~2500 ticks; the provider runs on every prompt build.
- Pure formatting in `Source/Pure/GuestLineFormat.cs` + `tests/HospitalityBridgeLogicTests/`.

## Localization

Same rules as the other packs: English inline, Russian DefInjected for groups/windows, Keyed
for any window start-text keys. Guest terminology: check `GLOSSARY.md` first; use «гость» and
keep faction names as the game renders them.

## Verification (extend `tests/SAVE_COMPATIBILITY_SMOKETEST.md`)

1. With Hospitality: receive a visitor group → one `HospitalityGuestsArrived` page for one
   colonist; no page-per-colonist fan-out.
2. Order a colonist to charm/diplomacy a guest repeatedly → ambient day-note for the colonist;
   confirm capture works with a non-colonist recipient (the blocking check from the
   perspective section — do this FIRST, before writing the rest).
3. Recruit a guest to joining (`HappyGuestJoins`) → one joined page.
4. Without Hospitality: inert, no warnings, nothing capturable listed.
5. With "Hospitality (Continued)" instead of the original: everything still fires (same
   packageId).

## Risks / open questions

- **Blocking verify**: pairwise Interaction capture with a non-colonist recipient (see above).
  If unsupported, the interactions fall back to solo ambient capture for the initiator or get
  dropped — decide with a maintainer, don't silently work around.
- `VisitorGroupSelectFaction` may be a dev/utility incident — confirm it fires in normal play;
  drop from matchers if dev-only.
- Guest-side thoughts must never be force-captured: if a future Hospitality version moves a
  thought onto colonists, the exact-defName matchers make adoption a deliberate edit, not an
  accident (this is why `matchPackageIds` is banned in this file).
- The scrounge group depends on colonist participation — verified, or cut.
