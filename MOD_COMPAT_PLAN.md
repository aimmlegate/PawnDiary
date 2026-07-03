# External Mod Compatibility — Target Survey & Patch Plan

Status: **proposal for review — no behavior changed by this document.**

Continuation of the integration work shipped as API v1 (`INTEGRATIONS.md`). That milestone built
the *machinery*; this document picks the *targets*: which popular mods deserve first-party
compatibility patches, what each patch looks like, and in what order to ship them. Mods that
extend or overhaul **social interaction** are preferred throughout, because they generate exactly
the moments a diary is about.

Sources: `INTEGRATIONS.md`, `EVENT_COVERAGE_PLAN.md`, repo XML/C# under `1.6/Defs/` and `Source/`,
and Steam Workshop / mod repository listings surveyed 2026-07 (see per-mod notes). Workshop pages
were not reachable from this environment for subscriber counts, so popularity is judged from
search prominence, curated "best mods" lists, and ecosystem activity — every packageId and
defName below is **unverified until checked in-game** (see §6).

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

| Mod | What it adds socially | Why it flows through our hooks | Verdict |
|---|---|---|---|
| **Vanilla Social Interactions Expanded** (Vanilla Expanded / Oskar Potocki; 1.4–1.6) | The de-facto standard social overhaul: opinion-driven interactions, quarrels and reconciliations, mentorship/teaching moments, group activities, aspirations with mood payoffs | New InteractionDefs land in `PlayLog.Add`; aspiration/mood payoffs are ThoughtDefs; both already hooked | **Patch — flagship Tier A target** |
| **Hospitality (Continued)** (Orion, continued maintenance; 1.5–1.6) | Guests visit, are entertained, recruited; vending; faction-relations play | Colonist↔guest interactions are vanilla PlayLog entries from the colonist's POV; hosting/recruiting outcomes are ThoughtDefs | **Patch** (guest-facing moments are strong diary material: strangers under your roof) |
| **SpeakUp** (Jptrrs; 1.6) | Conversation framework giving pawns actual dialogue lines; extends interactions into multi-turn exchanges | Explicitly built on vanilla InteractionDefs; this is the mod class `captureRenderedGameText=false` was designed for (schedules follow-up dialogue during grammar rendering) | **Patch** — must set `captureRenderedGameText` to `false` on its groups |
| **Way Better Romance** (divineDerivative; 1.4–1.6) | Romance overhaul: orientations, dates and hangouts as joy activities, casual hookups | Lover/spouse/ex transitions already arrive mod-agnostically via `Pawn_RelationsTracker.AddDirectRelation`; dates/hangouts are InteractionDefs/JobDefs with ThoughtDef payoffs | **Patch** (date/hangout groups only; relation transitions need nothing) |
| **Positive Connections** (1.5+) | Eight new positive interactions (compliments, knowledge sharing, …) | Plain InteractionDefs | **Patch — smallest, good template PR** |
| **Romance On The Rim** (1.3–1.6) | Romance need, automatic romantic interactions (pillow talk in bed, …) | InteractionDefs + ThoughtDefs | **Patch, lower priority than Way Better Romance** (overlapping niche; do second) |

### Tier B — personality context (motivates API v2)

These don't primarily add *events*; they add *who the pawn is*. The diary prompt's pawn summary
is where they belong — which is exactly roadmap **v2: `RegisterPawnContextProvider`**.

| Mod | Notes | Verdict |
|---|---|---|
| **RimPsyche — Personality Core** (1.6, actively developed) | Modern, performance-focused personality system inspired by Psychology; separate Sexuality module | **Primary v2 design partner** — current-generation, 1.6-native |
| **Psychology (unofficial)** (1.1–1.6) | The classic total social/psyche overhaul: personalities, expanded conversations, elections, therapy, trait-driven mental breaks | **Two-part**: its conversation/election/therapy *events* are Tier A XML (InteractionDefs + MentalStateDefs we already hook); its *personality readout* waits for v2 |
| **1-2-3 Personalities Mk.2** | Personality + social modules; **not yet updated to 1.6** (author: in progress) | **Watch list** — revisit when 1.6 lands; named in the v2 roadmap, don't design against a moving target |

### Tier C — chat/LLM mods (motivates API v3 + inbound adapters)

| Mod | Notes | Verdict |
|---|---|---|
| **RimTalk** (1.5–1.6, active; satellite mods RimTalk Event+ etc.) | LLM-generated pawn dialogue (Gemini/OpenAI/local models), context-aware bubbles | **Primary v3 design partner.** Inbound: its conversations become diary events (their side calls `SubmitEvent` — reach out / provide a PR; `rimtalk_*` eventKey examples already sit in `INTEGRATIONS.md`). Outbound: diary persona + recent entries as chat memory so bubble-dialogue and diary voice agree. |
| **Social Interactions: Expanded & AI-Powered**, **RimChat**, **RiMind**, **[CAP] Interactive Chat** | Newer/smaller LLM chat mods surfaced in the same survey | **Watch list** — v3, once designed for RimTalk, serves all of them; no bespoke work now |

### Non-targets (surveyed, deliberately skipped)

- **Interaction Bubbles** (Jaxe) — display-only overlay; nothing to integrate.
- **Gastronomy / venue mods** — social *venues*, not social *interactions*; their dining moments
  already flow through vanilla thoughts and the existing catch-alls read fine.
- **Intimacy — A Lovin' Expansion / RJW-adjacent** — adult-content scope; out of scope for
  first-party patches.
- **Vanilla Traits Expanded and trait packs** — traits are already read via vanilla trait access
  in the pawn summary; nothing extra to claim.

## 4. What a Tier A patch looks like (pattern)

One Def file per target mod, shipped in core:

- `1.6/Defs/Compat/DiaryCompat_<ModName>.xml`, every group gated with
  `<enableWhenPackageIdsLoaded><li>the.target.packageid</li></enableWhenPackageIdsLoaded>`.
- Prefer **`matchPackageIds`** (claims every InteractionDef the target ships, survives their
  content updates) for the broad group; add narrow defName/prefix groups only where a specific
  moment deserves its own instruction/tone (e.g. VSIE quarrel vs. VSIE mentorship).
- `important=false`, unique-ish `order` inside the domain below all built-in specific groups but
  above the catch-all; default dedup; `defaultEnabled=true` (each group is player-toggleable in
  settings like every other group).
- Conversation-framework mods (SpeakUp) additionally set `captureRenderedGameText=false`.
- EN + RU localization: `label`/`instruction`/`tone` via DefInjected, any new fallback lines via
  Keyed — `DOCUMENTATION.md §12` rules, Russian written natively, not literally translated.
- DLC-safety unchanged: matchers are plain strings; a game without the target mod (or any DLC)
  must load with zero errors and zero behavior change.

## 5. Todos

### PR 1 — Tier A wave 1: template + flagship
- [ ] Verify packageIds from each target's `About.xml` (VSIE expected
      `VanillaExpanded.VanillaSocialInteractionsExpanded`; Positive Connections TBD) and their
      InteractionDef/ThoughtDef defNames from the mods' published Defs or dev-mode logs (§6).
- [ ] **Positive Connections** compat file (smallest surface — establishes the
      `1.6/Defs/Compat/` pattern, file naming, and review template).
- [ ] **Vanilla Social Interactions Expanded** compat file: one broad package-matched Interaction
      group + narrow groups for the distinct moment families (quarrel/reconciliation, mentorship,
      group activity, aspiration fulfilled — final list from verified defNames), Thought-domain
      groups only where a payoff thought carries a story (aspiration completed).
- [ ] EN+RU DefInjected for all new groups; XML-parse touched files; run
      `DiaryPipelineTests` (group matching is XML-driven policy).
- [ ] `DOCUMENTATION.md` (compat-file pattern, §5 group inventory) + `CHANGELOG.md` +
      `INTEGRATIONS.md` (point "XML-only compatibility" section at `1.6/Defs/Compat/` as the
      canonical examples).

### PR 2 — Tier A wave 2: conversations + guests
- [ ] **SpeakUp** compat file with `captureRenderedGameText=false`; play-test that captured
      lines are the interaction seed, not mid-conversation grammar fragments.
- [ ] **Hospitality (Continued)** compat file: guest-chat Interaction group (colonist POV),
      Thought-domain groups for hosting/recruit-success moments; confirm both original and
      continued packageIds and gate on both.
- [ ] Same localization/test/docs bar as PR 1.

### PR 3 — Tier A wave 3: romance
- [ ] **Way Better Romance** compat file: date/hangout groups (warm, specific instructions);
      verify none of its relation changes bypass `AddDirectRelation` (expected: they don't).
- [ ] **Romance On The Rim** compat file: pillow-talk and romance-need moments; check overlap
      rules if both romance mods are loaded (orders must not fight).
- [ ] **Psychology (unofficial)** Tier A slice: conversation/election/therapy groups —
      time-boxed; if its conversation system bypasses `PlayLog.Add`, log findings in this file
      and defer the remainder to v2/v3 work instead of forcing it.

### PR 4 — API v2 design (separate plan doc before code)
- [ ] Draft `RegisterPawnContextProvider(id, Func<Pawn, string>)` contract: registration timing,
      main-thread rule, per-provider length cap + sanitation (mirror `extraContext` rules),
      failure isolation (a throwing provider is dropped and logged once, never crashes prompt
      building), settings toggle per provider id.
- [ ] Validate the draft against **RimPsyche** (primary) and **Psychology (unofficial)**
      (secondary) — what would each actually emit? Contact maintainers / open a design issue.
- [ ] Bump `ApiVersion` to 2 additively; extend `integrations/PawnDiary.ExampleAdapter` with a
      provider example; update `INTEGRATIONS.md` roadmap → shipped.

### PR 5 — API v3 design (separate plan doc before code)
- [ ] Draft the read-only snapshot DTO (persona line, N recent entry summaries, strictly
      plain-string, no live objects) + staleness/threading semantics.
- [ ] Validate against **RimTalk** as the motivating consumer; offer them a `SubmitEvent`
      bridge PR (inbound) in the same conversation — inbound needs no new API and could even
      precede v3.
- [ ] Watch-list check: revisit **1-2-3 Personalities** (1.6 status) and the smaller LLM chat
      mods before locking the v3 surface.

## 6. Verification checklist (before each PR)

1. **PackageIds from `About.xml`** of the actual release targeted, never from memory —
   `INTEGRATIONS.md` itself warns ids change between development and Workshop releases. Gate
   continued/forked mods on *all* known ids.
2. **DefNames in-game**: load the target mod, use dev-mode Debug Actions / Def database dumps to
   list its InteractionDefs/ThoughtDefs; never guess (lesson learned from the dead
   `HarbingerTree` matcher, `CHANGELOG.md` 2026-07-03).
3. **Volume audit**: with the target loaded, one in-game day of play; new groups must not
   increase page count versus the catch-all baseline (wording-only claim holds).
4. **Absence audit**: with the target *not* loaded, zero load errors, zero settings-menu noise
   beyond the inert gated groups, zero behavior change.
5. XML-parse every touched Def file; run `DiaryPipelineTests` (+ `PromptVariantsTests` if prompt
   text changed); build the mod.
6. Docs in the same change: `DOCUMENTATION.md`, `CHANGELOG.md`, and this file's todo boxes.

## 7. Order and rationale

Positive Connections first (template), VSIE second (flagship value), then conversations/guests,
then romance — all XML, each independently shippable. API v2 and v3 come after Tier A because
each needs its own design doc and an external design partner, and because Tier A patches make
Pawn Diary visibly mod-friendly, which is the best opening for the v2/v3 maintainer
conversations (RimPsyche, RimTalk).
