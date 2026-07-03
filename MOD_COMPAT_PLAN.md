# External Mod Compatibility — Target Survey & Patch Plan

Status: **proposal for review — no behavior changed by this document.**

Continuation of the integration work shipped as API v1 (`INTEGRATIONS.md`). That milestone built
the *machinery*; this document picks the *targets*: which popular mods deserve first-party
compatibility patches, what each patch looks like, and in what order to ship them. Mods that
extend or overhaul **social interaction** are preferred throughout, because they generate exactly
the moments a diary is about.

Sources: `INTEGRATIONS.md`, `EVENT_COVERAGE_PLAN.md`, repo XML/C# under `1.6/Defs/` and `Source/`,
Steam Workshop / mod repository listings surveyed 2026-07, and — for every target with public
source — the mod's own GitHub repo (About.xml and Defs read directly; cited per mod in §4).
Workshop pages were not reachable from this environment, so packageIds/defNames come from source
repos and must be **re-confirmed against the shipped Workshop release in-game** before each PR
(§6) — a repo can drift from what players actually run (SpeakUp's repo About.xml demonstrably
lags its Workshop release, see §4.1).

---

## 1. Where integration stands today (recap)

Three integration mechanisms exist or are planned, in increasing order of cost:

| Mechanism | Status | What it gives a target mod |
|---|---|---|
| **XML-only compatibility group** | shipped | If the mod's content flows through vanilla systems we already hook (InteractionDefs via `PlayLog.Add`, ThoughtDefs, MentalStateDefs, HediffDefs, TaleDefs), a `DiaryInteractionGroupDef` with `matchPackageIds`/defName matchers + `enableWhenPackageIdsLoaded` claims that content and owns its prompt policy. No C#, theirs or ours. |
| **Inbound API v1** (`PawnDiaryApi.SubmitEvent`) | shipped | The mod (or a small bridge mod) pushes moments that *don't* flow through vanilla systems. Requires C# on the adapter side + an External-domain group claiming the `eventKey`. |
| **API v2/v3** (context providers / outbound snapshot) | roadmap | Personality mods enrich our prompts (v2); chat mods read diary persona + entries as memory (v3). Requires C# on our side first. |

Two facts shape everything below:

- **Every domain has a catch-all group**, so content from other mods is *already captured* today —
  it just lands in a generic group with bland wording ("chatted with X"). An XML compat group
  therefore changes **wording quality only, never page volume**. This is the same
  zero-volume-first principle as `EVENT_COVERAGE_PLAN.md`.
- **`enableWhenPackageIdsLoaded` makes compat groups free to ship in core.** A group gated on an
  absent mod sits inert: no load-order requirement, no separate patch mod, no `MayRequire`. So
  Tier A below ships inside Pawn Diary itself, one Def file per target mod.

## 2. Selection criteria

A mod earns a first-party patch when it scores on most of these:

1. **Social-interaction weight** (preferred per this plan): it adds or overhauls pawn-to-pawn
   moments — conversations, quarrels, romance, teaching, hosting.
2. **Popularity / list prevalence**: prominent in Workshop search, "best mods" round-ups, or a
   staple of large modlists (Vanilla Expanded series, Hospitality-class staples).
3. **1.6 support and active maintenance**: a patch against an abandoned mod is dead weight.
4. **Mechanism fit**: content flows through systems we already hook (cheap XML tier), or the mod
   is the motivating consumer for a roadmap API (v2 personality, v3 chat).
5. **Scope fit**: SFW, base-game-safe, and diary-relevant (a moment a person would write about).

## 3. Survey — candidates and verdicts

### Tier A — XML-only compatibility groups (ship first; zero C#)

| Mod | packageId (source-verified) | What it adds socially | Verdict |
|---|---|---|---|
| **Vanilla Social Interactions Expanded** (Vanilla Expanded; 1.4–1.6) | `VanillaExpanded.VanillaSocialInteractionsExpanded` | Venting, discord/quarrels, per-skill teaching, gatherings, social incidents, aspirations | **Patch — flagship Tier A target** |
| **Hospitality** (Orion; 1.0–1.6 in repo) + **Hospitality (Continued)** | `Orion.Hospitality` (Continued fork id TBD — gate on both) | Guests visit, are charmed/recruited; diplomacy interactions | **Patch** (strangers under your roof are strong diary material) |
| **SpeakUp** (Jptrrs) | `JPT.speakup` (repo About lags at 1.2–1.3; Workshop lists 1.6 — verify) | Conversation framework giving pawns dialogue on vanilla InteractionDefs | **Patch** — needs `captureRenderedGameText=false` |
| **Way Better Romance** (divineDerivative; 1.4–1.6) | `divineDerivative.Romance` | Orientations, hookup/date/hangout interactions | **Patch** (interaction groups only; relation transitions already covered mod-agnostically) |
| **Positive Connections** (cemacmillan; 1.4–1.6) | `cem.PositiveConnections` | Eight positive interactions (comfort, mediation, compliments, gift, storytelling, …) | **Patch — smallest, good template PR** |
| **RimTalk (interaction log slice)** (1.5–1.6) | `cj.rimtalk` | Its AI conversations log through a vanilla InteractionDef (`RimTalkInteraction`) | **Patch now** — the deeper v3 work (§3 Tier C) is separate |
| **Romance On The Rim** (1.3–1.6) | TBD (no public source found in survey) | Romance need, pillow talk, automatic romantic interactions | **Patch, lower priority** (overlaps Way Better Romance; needs in-game def dump first) |

### Tier B — personality context (motivates API v2)

| Mod | packageId | Notes | Verdict |
|---|---|---|---|
| **RimPsyche — Personality Core** (1.6, active; Disposition/Sexuality/Relationship modules) | `Maux36.Rimpsyche` | Documented modder surface already exists: `PsycheDataUtil.GetPsycheData(Pawn)` (15 facets, sexuality, memories) and an `InteractionHook` exposing conversation topic/alignment/opinion (§4.2) | **Primary v2 design partner** |
| **Psychology (unofficial)** (1.1–1.6) | `Community.Psychology.UnofficialUpdate` (from the unofficial-update repo; confirm the 1.6 upload kept it) | Classic total overhaul: psyche personality, expanded conversations, elections, therapy | **Two-part**: event slice is Tier A XML *if* its conversations flow through `PlayLog.Add` (unverified, §4.2); personality readout waits for v2 |
| **1-2-3 Personalities Mk.2** | — | **Not yet updated to 1.6** (author: in progress) | **Watch list** — revisit when 1.6 lands |

### Tier C — chat/LLM mods (motivates API v3 + inbound adapters)

| Mod | Notes | Verdict |
|---|---|---|
| **RimTalk** (`cj.rimtalk`) | Reads pawn mood/traits/relations/thoughts into LLM prompts; **exposes its own extension API** (`ContextHookRegistry.RegisterPawnVariable` / `RegisterPawnHook`, Scriban template variables) — so diary-as-memory can ship as a bridge *without waiting for RimTalk changes* (§4.3) | **Primary v3 design partner** |
| **Social Interactions: Expanded & AI-Powered**, **RimChat**, **RiMind**, **[CAP] Interactive Chat** | Newer/smaller LLM chat mods surfaced in the same survey | **Watch list** — v3, once designed for RimTalk, serves all of them |

### Non-targets (surveyed, deliberately skipped)

- **Interaction Bubbles** (Jaxe) — display-only overlay; nothing to integrate (it's a RimTalk
  dependency, but that costs us nothing).
- **Gastronomy / venue mods** — social *venues*, not social *interactions*; their dining moments
  already flow through vanilla thoughts and the existing catch-alls read fine.
- **Intimacy — A Lovin' Expansion / RJW-adjacent** — adult-content scope; out of scope for
  first-party patches.
- **Vanilla Traits Expanded and trait packs** — traits are already read via vanilla trait access
  in the pawn summary; nothing extra to claim.

## 4. What each tier concretely requires

### 4.1 Tier A — per-mod requirements (source-verified detail)

**Common pattern** for every mod below: one Def file `1.6/Defs/Compat/DiaryCompat_<ModName>.xml`;
every group gated `<enableWhenPackageIdsLoaded>`; `important=false`; unique `order` below
built-in specific groups, above the domain catch-all; `defaultEnabled=true` (player-toggleable
like every group); EN + RU DefInjected for `label`/`instruction`/`tone` (Russian written
natively); plain-string matchers only (DLC-safe, mod-absent-safe).

**Vanilla Social Interactions Expanded** — [source](https://github.com/Vanilla-Expanded/VanillaSocialInteractionsExpanded).
Everything it ships is cleanly **`VSIE_`-prefixed**, and its content spans several def types we
already hook: `InteractionDefs` (`VSIE_Vent`, `VSIE_Discord`, `VSIE_Teaching` + per-skill
`VSIE_Teaching_*`), `ThoughtDefs` (memory + situational social thoughts, incl. aspiration
payoffs), plus `GatheringDefs`/`IncidentDefs` (flow through the gathering/incident hooks we
already listen to). Work:
- Interaction domain: one `matchPrefixes: VSIE_` safety-net group (warm generic instruction) plus
  narrow groups for the three distinct moment families — **venting** (unburdening, one-sided
  relief), **discord** (a quarrel that stung), **teaching** (`matchPrefixes: VSIE_Teaching` —
  passing a skill on / being taught). ~4 groups.
- Thought domain: one `matchPrefixes: VSIE_` group biased toward the aspiration-fulfilled beat
  (verify exact thought defNames from the def dump; split out a narrow group only if the
  aspiration thoughts are distinguishable by name).
- Confirm in dev mode that VSIE's gatherings/incidents already read acceptably through the
  existing catch-alls; only add groups if the wording is actively wrong.

**Positive Connections** — [source](https://github.com/cemacmillan/PositiveConnections).
Eight vanilla InteractionDefs, all **`DIL_`-prefixed**: `DIL_GiveComfort`, `DIL_Mediation`,
`DIL_Compliment`, `DIL_SkillShare`, `DIL_DiscussIdeoligion`, `DIL_GiveGift`,
`DIL_SharedPassion`, `DIL_Storytelling`. Work: one `matchPrefixes: DIL_` group with a warm
default, plus (optionally) narrow groups for the two most distinct beats (comfort after distress;
mediation of a conflict). `DIL_DiscussIdeoligion` needs no `MayRequire` — string matchers stay
inert without Ideology. Smallest patch; do it first as the template.

**Hospitality** — [source](https://github.com/OrionFive/Hospitality) (repo supports 1.6;
original author in maintenance mode — gate on `Orion.Hospitality` **and** the Continued fork's
packageId once confirmed). Three vanilla InteractionDefs, **unprefixed**: `GuestDiplomacy`,
`CharmGuestAttempt`, `ScroungeFoodAttempt` — exact `matchDefNames` only, never prefixes. Two
findings simplify this patch:
- Its guest ThoughtDefs (`GuestAngered`, `GuestPleasedRelationship`, …) land on the **guest**,
  who is not diary-eligible — no Thought-domain groups needed.
- A successfully recruited guest joins via faction change, which the existing **Arrival** hook
  (`Pawn.SetFaction`) already turns into a diary page mod-agnostically — recruitment needs no
  compat group, just a play-test confirming the arrival page reads well for ex-guests.
So the patch is one Interaction-domain group (colonist hosting/charming a guest — instruction
should acknowledge the partner may be a stranger/visitor, since the guest side won't write).

**Way Better Romance** — [source](https://github.com/divineDerivative/WayBetterRomance).
Three vanilla InteractionDefs, **unprefixed**: `TriedHookupWith`, `AskedForDate`,
`AskedForHangout`; rejection ThoughtDefs also unprefixed (`RebuffedMyDateAttempt*`,
`FailedDateAttemptOnMe`, …). Lover/spouse/ex transitions ride the existing
`AddDirectRelation` hook — nothing to do there. Work: one Interaction-domain group per beat
(hookup attempt / date / hangout — distinct tones), exact `matchDefNames`; one Thought-domain
group for the rebuffed/rejected family (a sore, private beat that suits a diary). Verify in
source that date *outcomes* (the date actually happening — it's a JobDef/JoyDef) produce any
loggable signal; if not, note it as a future inbound-API suggestion to the maintainer rather
than forcing it.

**SpeakUp** — [source](https://github.com/jptrrs/SpeakUp). packageId `JPT.speakup`; the repo's
About.xml stops at 1.3 while the Workshop build advertises 1.6 — confirm from the shipped
About.xml which id/versions players actually run. It extends **vanilla** interactions into
multi-turn dialogue scheduled during grammar rendering — exactly the class
`captureRenderedGameText=false` exists for. Work: one Interaction-domain group,
`matchPackageIds` on its dialogue defs if it ships its own InteractionDefs (verify via def dump)
or rely on the flag alone if it only decorates vanilla defs; play-test that captured seeds are
the interaction, not mid-conversation fragments.

**RimTalk (log slice)** — [source](https://github.com/jlibrary/RimTalk). Its conversations post
a vanilla social-log entry through InteractionDef **`RimTalkInteraction`**. Work: one
Interaction-domain group on that exact defName. Two things to verify in-game: whether the logged
text is the AI dialogue or a generic line (decides `captureRenderedGameText`), and volume —
RimTalk chats constantly, so this group may need conservative odds/dedup in its XML policy (it
still only *re-words* pages the catch-all would have claimed, but sampling policy should favor
other groups).

**Romance On The Rim** — no public source located in this survey. Requires the in-game def dump
first (packageId + InteractionDef names for pillow talk etc.); pattern will mirror Way Better
Romance. Keep last in Tier A.

### 4.2 Tier B — what API v2 must provide (and to whom)

The deliverable is roadmap **v2: `RegisterPawnContextProvider(id, Func<Pawn, string>)`** plus its
first real consumers. Source-verified interface facts to design against:

- **RimPsyche** ([wiki, "For Modders"](https://github.com/jagerguy36/Rimpsyche/wiki/For-Modders))
  already exposes everything a provider needs: `PsycheDataUtil.GetPsycheData(Pawn)` returns a
  `PsycheData` with **15 personality facets** (−50..50), sexuality data, and memories. A
  bridge — theirs, ours, or a standalone mod — turns the strongest facets into one compact
  pawn-summary line (e.g. `personality=blunt, curious, slow to trust`). RimPsyche also offers
  `InteractionHook` (initiator, recipient, **topic**, alignment, opinion offset), which is a
  ready-made **inbound v1 bridge** opportunity independent of v2: submit conversations as diary
  events with `extraContext` (`topic=…`, `alignment=…`) — richer than any XML matcher could get.
- **Psychology (unofficial)**: personality lives in its psyche comp (per-pawn nodes); no
  documented modder API found — the v2 bridge would read its comp directly, so it belongs on
  *their* side or in a standalone bridge, not in our core. Also verify whether its conversation
  system posts `PlayLog` entries (decides whether the Tier A slice in §5 PR 3 is real).

What v2 itself must therefore specify (design doc before code):
1. Registration: id + `Func<Pawn, string>`, registered once at startup (main-thread), feature-
   detectable via `ApiVersion >= 2`.
2. Safety: per-provider output cap + sanitation identical to `extraContext` rules (single line,
   `;`→`,`); a throwing provider is disabled and logged **once**, never crashes prompt building.
3. Placement: provider lines join the pawn summary next to the `DlcContext` fields (trait/faith/
   title lines), so the LLM sees personality as *who the pawn is*, not as an event.
4. Player control: per-provider toggle in settings (mirrors per-group toggles).
5. Purity: providers run in the impure snapshot phase; their strings enter the plain prompt
   payload — no live objects into pure code (AGENTS.md barrier holds unchanged).
6. Ship with: `ApiVersion = 2`, example provider in `integrations/PawnDiary.ExampleAdapter`,
   `INTEGRATIONS.md` v2 section, maintainer outreach to RimPsyche with a concrete snippet.

### 4.3 Tier C — what API v3 must provide (and the RimTalk shortcut)

The deliverable is roadmap **v3: outbound read-only context**. Source-verified facts to design
against ([RimTalk repo](https://github.com/jlibrary/RimTalk)):

- RimTalk builds its prompts from configurable pawn context (mood, traits, relations, thoughts,
  backstory, …) via Scriban templates, and **exposes `ContextHookRegistry`**
  (`RegisterContextVariable`, `RegisterPawnVariable`, `RegisterPawnHook`) so other mods can add
  template variables.
- Consequence: once we have a public read-only snapshot, **diary-as-memory ships as a small
  bridge that registers a `pawndiary` variable in RimTalk** — no RimTalk-side changes required.
  Bridge home: a third assembly under `integrations/` (like the ExampleAdapter), *not* core —
  core must never reference RimTalk types.

What v3 itself must specify (design doc before code):
1. Surface: `PawnDiaryApi.TryGetContextSnapshot(Pawn, out DiaryContextSnapshot)` — plain-string
   DTO: persona line, N most recent entry summaries (title + first sentence, not full prose),
   entry count/day-range. No live objects, no mutable references.
2. Semantics: main-thread, cheap (reads the already-persisted diary model, no LLM calls),
   `false` when no game/component; N capped and XML-tunable.
3. Privacy/consistency: outbound text is the *player-visible* diary content only — nothing from
   in-flight generation.
4. Inbound counterpart needs **no new API**: RimTalk conversations could be submitted via
   existing v1 `SubmitEvent` (`rimtalk_*` keys are already the worked example in
   `INTEGRATIONS.md`) — but the §4.1 log-slice group may make this redundant; decide after the
   Tier A patch is play-tested (avoid double-writing the same chat).
5. Ship with: `ApiVersion = 3`, the RimTalk bridge as the reference consumer, maintainer
   outreach offering it upstream.

## 5. Todos

### PR 1 — Tier A wave 1: template + flagship
- [x] PackageIds/defNames researched from source repos (§4.1) — VSIE, Positive Connections,
      Hospitality, Way Better Romance, SpeakUp, RimTalk, RimPsyche, Psychology.
- [ ] Re-confirm against shipped Workshop releases in-game (§6.1–6.2) for the two mods in this
      PR; dump their def lists in dev mode.
- [ ] **Positive Connections** compat file (`DIL_` prefix group + optional comfort/mediation
      narrows) — establishes the `1.6/Defs/Compat/` pattern, file naming, and review template.
- [ ] **Vanilla Social Interactions Expanded** compat file: `VSIE_` prefix safety net + vent /
      discord / teaching narrow groups; Thought-domain `VSIE_` group (aspiration bias);
      check gatherings/incidents read fine via existing catch-alls.
- [ ] EN+RU DefInjected for all new groups; XML-parse touched files; run `DiaryPipelineTests`.
- [ ] `DOCUMENTATION.md` (compat-file pattern, §5 group inventory) + `CHANGELOG.md` +
      `INTEGRATIONS.md` (point "XML-only compatibility" at `1.6/Defs/Compat/` as canonical
      examples).

### PR 2 — Tier A wave 2: conversations + guests
- [ ] **SpeakUp** compat group with `captureRenderedGameText=false`; confirm shipped packageId
      (repo lags Workshop); play-test seed quality.
- [ ] **Hospitality** compat group for `GuestDiplomacy`/`CharmGuestAttempt` (exact names);
      confirm Continued-fork packageId and gate on both; play-test that guest recruitment
      already reads well via the Arrival page (expected: yes, no group needed).
- [ ] **RimTalk log-slice** group on `RimTalkInteraction`; decide `captureRenderedGameText`
      from what the log entry actually contains; tune odds/dedup for chattiness.
- [ ] Same localization/test/docs bar as PR 1.

### PR 3 — Tier A wave 3: romance
- [ ] **Way Better Romance** compat file: hookup/date/hangout groups (exact `matchDefNames` —
      names are unprefixed) + rebuffed/rejected Thought group; check whether completed dates
      emit any loggable signal (if not: note as inbound suggestion to maintainer).
- [ ] **Romance On The Rim**: in-game def dump first (no public source); then mirror the WBR
      pattern; check group-order interplay if both romance mods are loaded.
- [ ] **Psychology (unofficial)** Tier A slice — *conditional*: verify its conversations post
      PlayLog entries; if yes, conversation/election groups; if no, log findings here and defer
      to the v2/bridge track.

### PR 4 — API v2: pawn-context providers (design doc before code)
- [ ] Write the v2 design doc per §4.2 (registration, sanitation caps, failure isolation,
      settings toggle, pawn-summary placement, purity boundary).
- [ ] Validate the draft against RimPsyche's real surface (`PsycheDataUtil`, 15 facets): write
      the provider snippet we'd hand their maintainer; open a design issue / contact Maux36.
- [ ] Decide the Psychology story: standalone bridge vs. their-side provider (we ship neither
      in core).
- [ ] Implement: `ApiVersion = 2`, example provider in the ExampleAdapter, `INTEGRATIONS.md`
      v2 section moves roadmap → shipped, tests for the sanitation/failure-isolation pure parts.
- [ ] Separately evaluate the **RimPsyche inbound bridge** (their `InteractionHook` →
      `SubmitEvent` with `topic=`/`alignment=` extraContext) — v1-only, could ship any time.

### PR 5 — API v3: outbound context (design doc before code)
- [ ] Write the v3 design doc per §4.3 (snapshot DTO shape, recency cap, main-thread cheap-read
      semantics, player-visible-content-only rule).
- [ ] Implement `TryGetContextSnapshot` + `ApiVersion = 3` + `INTEGRATIONS.md` update; pure
      tests for snapshot summarization.
- [ ] Build the **RimTalk bridge** under `integrations/` registering a `pawndiary` Scriban
      variable via `ContextHookRegistry.RegisterPawnVariable`; offer it upstream to jlibrary.
- [ ] Decide inbound-vs-log-slice for RimTalk chats (no double-writing) based on PR 2 play-test.
- [ ] Watch-list check before locking the surface: 1-2-3 Personalities 1.6 status; RimChat /
      RiMind / SIE&AI-Powered adoption.

## 6. Verification checklist (before each PR)

1. **PackageIds from the shipped release's `About.xml`**, not just the source repo —
   `INTEGRATIONS.md` itself warns ids change between releases, and SpeakUp's repo already lags
   its Workshop build. Gate continued/forked mods on *all* known ids.
2. **DefNames in-game**: load the target mod, dump its InteractionDefs/ThoughtDefs via dev-mode
   Debug Actions; source-read names (§4.1) are strong priors, not proof (lesson: the dead
   `HarbingerTree` matcher, `CHANGELOG.md` 2026-07-03).
3. **Volume audit**: with the target loaded, one in-game day of play; new groups must not
   increase page count versus the catch-all baseline (wording-only claim holds). Extra scrutiny
   for RimTalk (chatty by design).
4. **Absence audit**: with the target *not* loaded, zero load errors, zero settings-menu noise
   beyond the inert gated groups, zero behavior change.
5. XML-parse every touched Def file; run `DiaryPipelineTests` (+ `PromptVariantsTests` if prompt
   text changed); build the mod.
6. Docs in the same change: `DOCUMENTATION.md`, `CHANGELOG.md`, and this file's todo boxes.

## 7. Order and rationale

Positive Connections first (template), VSIE second (flagship value), then conversations/guests
(including the cheap RimTalk log-slice), then romance — all XML, each independently shippable.
API v2 and v3 follow because each needs its own design doc and an external partner — and the
research above found both partners already meet us halfway: RimPsyche documents its psyche-data
and conversation hooks for modders, and RimTalk's `ContextHookRegistry` means diary-as-memory
needs no upstream buy-in at all, just our v3 snapshot plus a small bridge. Tier A patches make
Pawn Diary visibly mod-friendly first, which is the best opening for those maintainer
conversations.
