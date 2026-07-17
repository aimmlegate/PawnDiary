# Core improvement plan — post-DLC waves

> Status: planned 2026-07-17; not yet started. To be worked **after DLC integration lands**
> (Wave C1 is collision-free and can slot between DLC waves as a breather). This is a working
> plan, not a contract — shipped behavior lands in `../DOCUMENTATION.md`. Grounded in a full
> code-exploration pass and competitor research on the plan date; `file:line` references are to
> that snapshot — re-verify any load-bearing seam before coding against it. Competitor facts come
> from workshop pages/READMEs, not their source.

Scope: the **core mod only** — DLC event depth has its own master plan
(`../DLC_SUPPORT_MASTER_IMPLEMENTATION_PLAN.md`), external-mod compatibility its own track
(`MOD_COMPAT_PLAN.md`). Hard constraints, restated so no wave drifts:

- **No gameplay effects, no new mechanics.** Read-only against game state; the product is text
  the player reads plus the UI that presents it.
- **Maximum compatibility** with gameplay mods and overhauls.
- **Stay compact.** One thing — the diary — done fully and well. The bar for every item:
  *players should want to open the diary and enjoy reading it, repeatedly*.

Effort tags: S ≈ a day-scale slice, M ≈ a multi-day slice (changelog slice granularity).
Item IDs (A5, C4, …) reference the idea catalog in Appendix C.

---

## The waves

Each wave is one shippable release. Ordering rationale is noted; waves marked ⇄ can be
reordered freely against their neighbors.

### Wave C1 — Reading-quality quick wins *(all S; no save-format changes; zero collision with DLC waves)*

| Item | Work | Seam |
|---|---|---|
| A5 | Identity block (traits / 1-line background / top relationships) added to everyday prompt templates | `../1.6/Defs/DiaryPromptTemplateDefs.xml`, `../Source/Generation/DiaryContextBuilder.cs`; append-only fields to preserve DefInjected label indexes |
| B2 | Mood/slim pawn summary in `SoloInternalState` + batched templates | same seam |
| B1 | Explicit output-language directive; decide fate of English `key=` schema labels for non-EN locales | `../1.6/Defs/DiaryPromptDef.xml` + `../Source/Generation/PromptAssembler.cs`; RU DefInjected pass |
| C4 | Distinct page tint + header rule for all ~15 cues (today only combat/socialFight/mentalBreak) | `../1.6/Defs/DiaryUiStyleDef.xml` color specs (+ resolver extension if the special-casing is hardcoded, `../Source/Defs/DiaryUiStyleDef.cs`) |
| C2 | Quadrum/season dividers in the year scroll | card-layout builder in `../Source/UI/` (`ITab_Pawn_Diary` partials) |
| E4 | Styled date lines (small-caps/spacing/cue glyph) | `EntryCards` header drawing |
| F2 | Un-gate the per-entry copy button for players (currently dev-only) | `../Source/UI/ITab_Pawn_Diary.EntryCards.cs` |

**Acceptance:** prompt-lab golden tests updated (EN + RU); visual pass on the 6k-entry dev
fixture; no new settings required.

### Wave C2 — Memory & continuity *(the flagship; do before competitors close the gap)*

The largest confirmed prompt gap: history context is one entry deep (last opener + last two
sentences), and pawn identity is never *shown* to the model, only used to select style. The
RimTalk ecosystem ("Lucid Chronicle") is converging on layered long-term memory — this wave is
the differentiator.

**Architecture settled 2026-07-17 — full design in Appendix D.** Canonical constraints locked
during design: **no LLM summarization, no full-context dumps** — memory is data selection in
code + weighted random gates, like every other prompt input; strict pure/impure boundary; the
existing context system (thoughts, previous-entry bridge, reflections, day summaries) is **not
rewritten** — memory lands as an additive enhancement in the prompt-enchantment mold.

| Step | Work |
|---|---|
| 1 | Pawn-knowledge facade (`PawnMind`): sectioned snapshot capture + pure views; `BuildPawnSummary` reimplemented on it as a **byte-identical drop-in** (golden-test-proven, zero prompt drift) |
| 2 | Memory enhancement: unified L1–L3 candidate query + one pure selector + an enchantment-style `memory` prompt field (template-gated, XML-tuned, off by default until tuned) |
| 3 | L3 long-term store: tagged/weighted item bag (`MemoryTag` flags enum, machine-only severity, forget-on-query fuzz), centralized deposit policy, caps + eviction |
| 4 | Content-level anti-repetition (A6) via selector dedup keys + capture-side same-story suppression; voice-drift guard (B5) optional garnish |

**Acceptance:** step 1 lands with byte-identical golden output (hard gate); **zero new LLM
calls** introduced by the whole wave; L3 bag save round-trips + caps/eviction covered by pure
tests; memory field gates default conservative; continuity observable across ≥5 consecutive
entries in playtest.

### Wave C3 ⇄ — Library & navigation

C1 search + filter chips (cue/importance/starred) · C5 bookmarks · H5 reader-side "on this day"
divider · C10 typography quick-controls · C8 unread-pages overview on gizmo right-click.

**Acceptance:** search over hot + 10k archive rows without frame drops (virtualization and
height cache intact); filters localized.

### Wave C4 — Keepsakes & sharing *(after C2/C3: compilation uses memoir + stars)*

F1 player-facing export (per-pawn/colony; txt / Markdown / themed HTML) · F3 memoir compilation
(life summary + starred entries + final page) · C6 portraits/mood glyphs on cards.

**Acceptance:** HTML opens standalone with theme colors; exports fully localized; corpse and
departed pawns exportable.

### Wave C5 — New story sources & pacing *(capture-side; schedule away from DLC capture work to avoid seam collisions)*

H2 anniversaries & record milestones (records deltas, birthdays, arrival anniversaries) ·
H1 battle-log beats in combat prompts · H3 letters-archive context for reflections ·
H4 mid-save backfill "story so far" page · B6 digest pacing (route minor events into
DayReflection; per-day soft cap) · B4 length dynamics · H6 art flavor (optional).

**Acceptance:** every new capture behind a group toggle (default sane); RimTest EVT matrix rows
added; dedup/cooldown verified; per-day scan cost profiled.

### Wave C6 — Atmosphere polish *(anytime garnish)*

C7 new-entry reveal animation (alpha/ink sweep — avoid text-length changes that break the
measured-height cache) · E1 drop caps + ornaments · E3 pull-quote epigraphs · C9 parchment/light
theme preset.

### Wave C7 — Onboarding & settings UX *(deliberately last, per author's call — reader and prose quality first)*

D1 provider wizard (OpenAI / OpenRouter / Google-compat / Ollama / LM Studio presets) ·
D2 "write a sample entry now" button · D4 cost-estimator line · G1 per-lane usage counters ·
G2 global pause switch · D3 Main-tab regrouping · D5 style previews in the studios ·
D6 optional per-pawn freeform voice note (competitor parity, layered on top of the auto persona
system).

**Acceptance:** fresh install → first real entry in <3 minutes with only an API key; counters
persist per session; the wizard never blocks the expert path.

### Anytime / infra

G4 build+test CI mirroring `verify.ps1` (11 pure suites + XML well-formedness on PRs).

### Deliberately deferred *(scope discipline)*

C3 full-screen reading dialog · custom fonts · F4 Progress Renderer hook · B3 structured-output
JSON · G3 internal daily budget · A3/A4 open-threads & narrative-continuity providers (belongs
to the DLC narrative-continuity track — `../DLC_NARRATIVE_CONTINUITY_IMPLEMENTATION_PLAN.md`
N-waves — not here).

---

## Appendix A — Current-state ground truth (exploration snapshot, 2026-07-17)

### Generation pipeline

Capture → queue → prompt-build → lane dispatch → OpenAI-compatible LLM call → sanitize → store.
Pure prompt logic in `../Source/Pipeline/` (golden-tested via the Node prompt-lab), adapters in
`../Source/Generation/`, orchestration in `../Source/Core/DiaryGameComponent.*`. Voice stack:
~50 persona styles × independent psychotypes × humor cues (20%) × hediff overrides × live-health
prompt enchantments × 14 per-shape templates × interaction-group tones. Anti-repetition today is
opener/style-level: opener variance, stock-phrase ban, per-shape length variation, weighted
weather/thought/nearby picks.

Confirmed gaps that motivate Waves C1/C2:

1. **History is 1 entry deep** — prompts see only the last opener + last 2 sentences
   (`../1.6/Defs/DiaryTuningDef.xml`: `previousEntryEnding*`). Only reflections sample ≤8 past
   entries (`arcReflectionMaxMemories`).
2. **Pawn profile absent from everyday templates** (`SoloDefault`, `SoloBatched`,
   `SoloInternalState`, `PairDefault`, `PairImportant`, `PairBatched`) — and even where present
   (important/combat/reflection shapes) it carries **no traits/skills/backstory/relationships as
   facts**; identity only *selects* style.
3. **Narrative-continuity layer is scaffolding** — only Biotech/Odyssey providers wired; the
   `narrative context` field is usually empty (`../Source/Pipeline/Narrative/NarrativeProviders.cs`).
4. **Output language is implicit** — inferred from localized prompt text, with English `key=`
   schema labels mixed in; no "respond in X" guardrail.
5. **No content-level anti-repetition** across a pawn's pages.
6. Everyday entries are deliberately short (1–3 sentences, `maxTokens=100` default) — a cost
   choice, all tunable.

### Reader & settings UI

Reader is a hidden inspector `ITab_Pawn_Diary` (12 partials, `../Source/UI/`), gizmo-opened,
year-paged, virtualized collapsible cards with measured-height caching; rich-text pipeline
(light markdown → Unity tags, `[[speech]]` blocks, display-only paragraph reflow, atmosphere
layouts, name highlighting, draw-time decorations); theme def `../1.6/Defs/DiaryUiStyleDef.xml`
(~35 color specs, per-cue accents — but only 3 cues get distinct page tint + header rule).
Animations: expand/collapse, 0.55s fade-in, title pulse, writing dots. Settings: 5 tabs with a
full-featured multi-lane API editor, Prompt/Persona/Psychotype studios, event filters, and a
deep Advanced editor.

Ranked UX gaps: no reader search; year-only filtering (cue/group metadata already on every
entry); no bookmarks; no quadrum dividers or jump-to-date; export dev-only (.txt, whole-colony);
inspect-pane clamp (~720px); no paper theme or typography controls; under-differentiated per-cue
tints; no portraits/mood glyphs; unread badge gizmo-only; limited animation; dense Main settings
tab with no first-run path.

### Data, retention, project

v0.5.0, RimWorld 1.6, first public release; identity = "storytelling only, no gameplay changes,
safe mid-save". 23 typed event pipelines. **Never read anywhere: `Find.BattleLog`,
`pawn.records`, `Find.Archive`** — untapped read-only story sources (Wave C6). Retention: hot
≤100 full events/pawn → cold archive ≤10,000 display-only rows/pawn (indexed by year/arc/
subject); diaries never deleted. No global per-day generation cap (pacing = chance weight ×
batching × scan cadences); the external integration API has a token-budget policy internal
generation doesn't use. Test infra: 11 pure test projects, prompt-lab golden tests, in-game
RimTest suite, `verify.ps1` hooks; no build CI.

## Appendix B — Competitive landscape (2026-07, workshop-page confidence)

| Mod | What it is vs. PawnDiary |
|---|---|
| **RimTalk: Diary** | Only direct competitor — LLM diary per colonist. Shallower capture (generic "recent log events" + lookback vs. 23 typed pipelines); diary reportedly opened via mod settings, not an in-game surface. |
| **RimTalk / RimChat** | Live dialogue bubbles + TTS — different product. Relevance = ecosystem gravity: RimTalk is the hub third parties extend (Diary, Lucid Chronicle, Expand Literature). PawnDiary interops rather than competes. |
| **RimTalk Lucid Chronicle** | Layered first-person memory (daily → evolving long-term memory per pawn) + colony chronicle. The direction Wave C2 answers. |
| **RimGPT** | AI commentator with Azure voices. Different lane. |
| **Diary (AamuLumi)** | Manual journaling, no AI. Beloved: multi-format auto-export + inline Progress Renderer screenshots. |
| **RimStory** | Event logger **with gameplay effects** (anniversary parties) — opposite philosophy. |

**Ahead:** capture depth (typed pipelines, paired POVs with sequential rewrite, DLC arcs);
zero-effort characterization (auto-rolled voice stack — competitors need hand-written personas);
output discipline (industrial response parser, stock-phrase bans, golden-tested prompts);
reading surface rendering; reliability engineering (lanes/failover/retention/tests); genuine
localization of the prompt layer (EN+RU).

**Behind:** setup friction (no wizard, no instant sample entry); BattleLog unused (their combat
entries cite concrete beats); no per-pawn freeform steering; keepsakes/export (dev-only vs.
AamuLumi's bar); ecosystem mindshare (RimTalk is the platform today); cost legibility (no usage
counters; pacing model is smart but opaque).

**Net:** depth leader in a field of breadth plays. The wave order protects the depth lead first
(C1, C2) and closes reader-facing gaps next (C3, C4); the onboarding/settings gaps are real but
deliberately taken last (Wave C7) — reader and prose quality outrank setup friction here.

## Appendix C — Idea catalog (reference)

Constraint check applies to every item: read-only vs. game state, text/UI only, no new
player-facing mechanics.

### A. Narrative memory & history depth

- **A1. Rolling recent-pages window (S/M)** — *superseded 2026-07-17: realized as the L2
  diary-recall projection of the memory system (Appendix D); hot entries surface via the unified
  selector rather than a fixed last-N block.*
- **A2. Per-pawn long-term memoir digest (M/L)** — *vetoed as designed 2026-07-17: LLM
  summarization contradicts the mod's canonical approach (data selection in code + weighted
  gates, no model-written summaries). Replaced by the L3 compact long-term store (Appendix D).*
- **A3. Open-threads ledger (M, deferred → narrative-continuity track)** — extract 1–3
  unresolved threads (grudge, worry, hope) per entry, decay them, feed the top thread back as
  `ongoing:` context. This is what the stubbed narrative-continuity layer wants to be — implement
  as `NarrativeProviders` rows, not a parallel system.
- **A4. Fill narrative-continuity provider stubs (M, deferred → same track)** — wire the
  deliberately-empty rows (romance/feud/health/ideology arcs) so the scoring policy in
  `DiaryNarrativeContinuityDefs.xml` has candidates.
- **A5. Identity block for everyday templates (S)** — compact `traits:` / `background:` (1-line
  backstory) / `relationships:` (closest 1–2 with opinion) via template XML + `DiaryContextBuilder`.
  Cheap tokens, large voice-grounding payoff; respects per-lane trimming (`DiaryContextDetailDef`).
- **A6. Content-level anti-repetition (M)** — track recently-narrated event keys per pawn; add
  "do not retell" guidance or suppress duplicate captures.

### B. Prompt & output tuning

- **B1. Explicit output-language directive (S)** — one localized system line + decide whether to
  localize or strip English `key=` labels for non-EN locales. Removes the model-guessing failure
  mode for the Russian build.
- **B2. Mood-aware everyday entries (S)** — include a slim `PawnSummary` in internal-state and
  batched templates (today they omit even mood).
- **B3. Structured-output experiment (M, deferred)** — optional JSON `{title, body, mood_word}`
  to merge the separate title call and enable a mood glyph; needs robust fallback parsing.
- **B4. Entry-length dynamics (S)** — scale sentence budget with day drama via a small
  salience-driven curve instead of per-template constants. XML-side.
- **B5. Voice-drift guard (S/M)** — re-inject 1–2 verbatim quotes of the pawn's past phrasing so
  style survives model swaps.
- **B6. Salience pacing / digest routing (M)** — route low-salience captures into the existing
  `DayReflection` digest with an optional per-pawn-per-day soft cap (important > ambient). Fewer,
  denser pages; reuses batching + DayReflection machinery.

### C. Reading experience

- **C1. Reader search + filters (M)** — text search + filter chips by cue/group + "important
  only". Metadata already on every entry (`ColorCue`, `GroupLabel`, `Important`); reuse the
  Advanced-tab search-box pattern.
- **C2. Quadrum/season dividers + jump-to-date (S/M)** — slim section headers on quadrum change;
  year dropdown gains quadrum sub-entries. Pure layout insertion in the virtualized list.
- **C3. Full-screen reading-mode Dialog (M/L, deferred)** — optional `Window` reusing the card
  renderer at book width (~850px centered). Fixes the inspect-pane clamp; same data, bigger canvas.
- **C4. Per-cue page tints + header rules (S)** — extend the 3-cue special-casing to all ~15 cues
  (romance warm rose, ritual violet, danger ember, quiet neutral…); XML color specs.
- **C5. Bookmarks/favorites (S/M)** — star toggle in the card footer next to regenerate; a
  "starred" filter; per-entry bool. Feeds keepsake export.
- **C6. Pawn presence on cards (S)** — small `PortraitsCache` portrait in year/card header +
  tiny mood glyph from captured `moodImpact` (stored today, never drawn).
- **C7. New-entry reveal animation (S)** — one-shot ink-fade/alpha sweep for entries finishing
  while visible (extend the existing first-seen fade). Avoid typewriter text-length changes that
  would invalidate the height cache.
- **C8. "Who has new pages" surface (M)** — gizmo right-click float menu listing pawns with
  unread pages (state exists per pawn). A UI affordance, not a Letter/Alert.
- **C9. Paper/book theme option (M/L)** — 1–2 alternative `DiaryUiStyleDef` palettes
  ("Parchment" light, "Ledger" dark) + optional 9-sliced paper texture; selectable in settings.
  Custom serif/TTF font = stretch goal, validate separately.
- **C10. Typography quick-controls (S)** — tiny reader toolbar: text size, line gap,
  collapse/expand all (maps to existing XML knobs).

### D. Settings UX

- **D1. First-run setup card (M)** — guided path when no lane is configured: provider preset →
  prefilled endpoint/auth → paste key → fetch models → test → done. All plumbing exists
  (`ApiConnectionController`, `ModelListClient`, per-row test).
- **D2. "Write a sample entry now" (S/M)** — one real generation for a selected colonist, shown
  inline. Converts config anxiety into instant gratification.
- **D3. Main-tab regrouping (S)** — collapsible sections (Connection / Generation / Reader /
  Data); move rarely-touched caps into Tuning.
- **D4. Cost/pacing estimator (S)** — "≈ N calls/day, ~M tokens/day" from current settings +
  colony size. Pure arithmetic.
- **D5. Style previews in the studios (S)** — render each persona/psychotype micro-example as a
  styled preview card.
- **D6. Per-pawn freeform voice note (S)** — optional one-line note ("writes like a tired
  sergeant") appended to the voice block after psychotype → style → humor. RimTalk-parity,
  layered on the auto system instead of replacing it.

### E. Formatting & typography

- **E1. Drop caps + ornaments (S)** — enlarged first letter above a length threshold; section
  glyph (❦) on long reflections. Display-time only.
- **E2. Model-side paragraphing (S)** — ask for blank-line breaks on 4+ sentence shapes; display
  reflow already handles them.
- **E3. Pull-quote epigraphs (M)** — for landmark entries, render the most striking sentence
  larger above the body (can reuse title-call machinery).
- **E4. Styled date lines (S)** — small-caps/letter-spacing/cue glyph via rich-text tags.

### F. Keepsakes, export & sharing

- **F1. Player-facing export (M)** — promote the dev export: per-pawn or colony; txt / Markdown /
  themed HTML to `SaveData/PawnDiaryExports/`; buttons in reader + settings. Shareable HTML is
  organic marketing.
- **F2. Player per-entry copy (S)** — un-gate the existing dev copy icon.
- **F3. Memoir compilation export (M)** — per pawn: life summary + starred entries + final page.
  The keepsake when a colonist dies or a colony ends. Depends on A2 + C5.
- **F4. Progress Renderer hook (L, deferred)** — inline/linked map captures per day; candidate
  for the external-compat track, not core.

### G. Cost, robustness & hygiene

- **G1. Per-lane usage counters (S)** — requests/tokens today + last error in the lanes editor.
- **G2. Global "pause writing" switch (S)** — halt generation without disabling capture; queue
  and resume.
- **G3. Optional internal daily budget (M, deferred)** — reuse `ExternalApiBudgetPolicy` for
  internal generation, opt-in, importance-first.
- **G4. Build/test CI (S)** — GitHub Actions mirroring `verify.ps1` gates on PRs.

### H. Untapped read-only story sources

- **H1. BattleLog mining (M)** — feed 1–2 concrete combat beats (who hit whom, near-miss,
  weapon) into raid/combat prompts from `Find.BattleLog`.
- **H2. Anniversaries & record milestones (M)** — birthdays, arrival anniversaries, anniversaries
  of a bonded pawn's death, round-number `pawn.records` milestones via cheap delta scans →
  existing reflection templates. Distinctive vs. all competitors.
- **H3. Colony news awareness (S/M)** — one line of "what the colony was told" from
  `Find.Archive` letters in day/quadrum reflections.
- **H4. Mid-save backfill "story so far" (M)** — one-time memoir page per colonist on first
  install into an existing save, from read-only state (backstory, age, relations, scars,
  records, titles). Makes "safe to add mid-save" delightful instead of just safe.
- **H5. "On this day" callbacks (S)** — archive is year-indexed: reader divider for entries from
  N years ago; prompt-side, feed last year's same-quadrum snippet into quadrum reflections.
- **H6. Art-description flavor (S, optional)** — when a pawn's own tale is immortalized in a
  sculpture, they may mention it once. Niche, very RimWorld.

### Priority tiers (rationale behind the wave order)

- **Tier 1 (small, immediate):** A5 · C4 · B1 · C2 · F2 → Wave C1.
- **Tier 2 (flagship arc):** A1+A2 · C1 · F1 · C7 → Waves C2–C4/C6.
- **Tier 3 (distinctive delight):** H2 · H4 · B6 · C6 · H5 → Waves C3–C5.
- **Deprioritized to last (author's call):** D1+D2 wizard/sample-entry and the settings-UX
  cluster (D3–D6, G1, G2) — onboarding value acknowledged, reader/prose quality first → Wave C7.
- **Deferred:** C3 · C9 fonts · F4 · B3 · G3 · A3/A4 (narrative-continuity track).

## Appendix D — Memory system design (Wave C2 architecture, settled 2026-07-17)

Design discussion outcome; supersedes catalog items A1/A2 as originally written. Status: design
reference for future implementation — re-verify named seams before coding.

### Canonical principles (non-negotiable)

1. **No LLM summaries, no full-context dumps.** Memory is data selection in code + weighted
   random gates into the prompt — the same canonical mechanism as weather mention, thought pick,
   humor chance. The model never writes or maintains memory state. Zero new LLM calls.
2. **Additive enhancement.** The existing context system (thoughts, previous-entry bridge, day
   summaries, reflections) is not rewritten. Memory contributes one optional, template-gated
   prompt field in the mold of `DiaryPromptEnchantmentDef` — everything behind it invisible.
3. **Strict pure/impure boundary.** Impure code only captures snapshots (main-thread, no
   decisions); all filtering/scoring/gating/fuzz is pure, golden-testable code in
   `Source/Pipeline`.
4. **Game state is the single source of truth.** Everything inferable stays inferred; only
   non-inferable facts are ever stored.

### The three layers

| Layer | Truth lives in | Decay mechanism | Persistence cost | Usage weight |
|---|---|---|---|---|
| **L1 game state** | the game (thoughts, relations, hediffs/scars, `pawn.records`) | native — thoughts age out, opinions drift, wounds heal | none | highest |
| **L2 hot diary** | `DiaryEventRepository` (≤100 events/pawn) | existing hot→archive eviction (archived rows fall out of queryable memory — natural fading) | already paid | middle |
| **L3 long-term store** | new per-pawn tagged item bag | forget-on-query fuzz + capped eviction | small, new | lowest ("least used, most critical") |

**L1-residue rule for L3:** anything `pawn.records` already tracks (kills, downs, mental
states, operations…) is L1 — never duplicated into L3. L3 holds only what the game doesn't
index: mostly **moments** (who I lost and how, the raid I nearly died in, the wedding) and rare
diary-semantic aggregates.

### The pawn-knowledge facade (`PawnMind` — working name)

One adapter is the single gateway to "what this pawn knows, feels, remembers." All pawn-data
reads (existing and new) become views over one captured snapshot:

```csharp
// IMPURE, thin, main-thread — the only code touching Pawn.
// Section-masked: capture only what the template's field list + memory gate need.
PawnMindGatherer.Capture(pawn, ctx, SectionMask needed) -> PawnMindSnapshot

// PURE immutable value object, sectioned (no god object):
PawnMindSnapshot {
    Identity      // sex, life stage, xenotype, title, faith, traits, backstory
    Mood          // mood bucket + raw thought list
    Health        // conditions, capacities, pain, scars
    Social        // relations, opinions
    Counters      // curated game records (closed enum key set)
    DiaryRecall   // L2: hot entries projected to candidates
    LongMemory    // L3: the item bag
    ExternalLines // API v8 RegisterPawnContextProvider output — absorbed, contract unchanged
}

// PURE adapter over the snapshot:
PawnMind {
    Summary()        // byte-identical to today's BuildPawnSummary — the drop-in
    Thoughts(filter) // LINQ-style queries over snapshot sections
    RelationTo(id)
    Counter(key)
    Memories(query)  // unified L1–L3 candidate query (below)
}
```

**Drop-in contract:** `BuildPawnSummary` reimplemented on `PawnMind.Summary()` must produce
byte-identical prompts, proven by the existing prompt-lab golden suite — zero drift is the
merge gate. Snapshot is transient (never saved); only the L3 bag touches `ExposeData`.
Side benefits: the snapshot is the natural prompt-lab fixture format and feeds a dev-mode
"what does this pawn remember" inspector; immutability opens the door (later) to off-thread
prompt assembly.

### Unified candidate query + selector

All three layers project into one normalized shape; the selector never knows layer internals:

```csharp
MemoryQuery      { PawnId, Tick, ContextTags /*from event cue/group*/, Shape, CharBudget, Seed }
MemoryCandidate  { Layer, Tags, Salience /*0..1, normalized BY the source*/,
                   AgeTicks, Text /*model-visible*/, FadedText?, DedupKey }

score = Salience
      × TagOverlap(query.ContextTags, c.Tags)   // popcount over flags
      × RecencyDecay(c.AgeTicks)
      × LayerWeight(c.Layer)                    // L1 > L2 > L3, XML-tunable
// then: repetition penalty vs recently-used DedupKeys (hard for L2/L3, soft for L1),
// seeded recall roll, canonical weighted gate → 0..2 picks within CharBudget
```

Selected candidates render into one `memory` prompt field. Gate chances per shape (reflections
high, important medium, everyday low), layer weights, decay half-lives: all XML
(`DiaryTuningDef` / small `DiaryMemoryPolicyDef`). Deliberately **not** wired into the
narrative-continuity selector (in-flight DLC track): shared pure primitives (decay curves,
seeded gate, repetition-key memory) go into common utilities both use — one toolbox, two thin
policies; convergence possible post-N4 with zero source changes.

### L3 store — item envelope

```csharp
[Flags] public enum MemoryTag : long   // explicit values, append-only forever,
{ None = 0, Combat = 1<<0, Violence = 1<<1, Loss = 1<<2, Family = 1<<3,
  Social = 1<<4, Romantic = 1<<5, Ritual = 1<<6, Health = 1<<7, /* … ≤64 */ }
// persisted as the numeric value (renames free; unknown future bits load harmlessly)

MemoryItem {
    key           // stable code-constant for accumulators; guid for moments
    value         // number, accumulators only
    text          // self-describing model-visible fragment: "killed {value} raiders"
    fadedText     // optional writer-provided degraded variant
    tags          // MemoryTag flags
    severity      // 0–10, MACHINE-ONLY: eviction/query/fuzz — never rendered to the model
    createdTick, lastReinforcedTick
}
```

- **Two item kinds:** accumulators (stable key, updatable value, idempotent by source event id)
  and moments (append-once). v1 likely ships **moments-only**; accumulators added when one
  proves non-inferable.
- **Severity is trait-relative:** deposit sites pass a base severity; a pure trait/psychotype
  multiplier adjusts per pawn (a Bloodthirsty pawn's 40th kill ≠ a Kind pawn's first).
  Anchored scale: 1–3 flavor / 4–6 notable / 7–8 formative / 9–10 identity-defining.
- **Closed vocabularies:** tags and accumulator keys are compile-time constants (house
  discipline, cf. `DiaryEventType` reserved members); external adapters pick from the published
  taxonomy via the API — no free-form strings.

### Forget-on-query (never modify storage)

Store truth; degrade the view. A deterministic fuzz seeded on (pawn, item, entry) — stable
within one entry, drifting over time — with width `f(severity band, age since reinforcement)`:
sharp text when fresh/reinforced ("thirty-seven — I remember every one"), `fadedText` or a
failed recall roll when stale and minor. Severity 9–10 exempt ("will never forget").
Reinforcement (same-key re-deposit) bumps `lastReinforcedTick` — repetition restores clarity
instead of spamming duplicates. Pure function → fully testable.

### Limits & eviction

- Moments: cap ≈48/pawn (`DiaryTuningDef` knob); evict lowest `severity × recencyDecay`;
  severity 9–10 eviction-exempt but the exempt set itself capped (≈8).
- Accumulators: bounded by the closed key set — nothing to evict.
- Backstop: per-pawn serialized-size ceiling (free-form `text`).

### Writes — centralized deposit policy

No changes to capture specs. One pure `MemoryDepositPolicy: (event snapshot) → deposits`,
observing the finalized event stream on the existing dispatch path (like retention/dedup
policies do), idempotent by source event id. One file answers "what does a pawn remember."
Starter catalog (small on purpose): death of bonded/related pawn (9) · own near-death (8) ·
child born (8) · wedding (7) · arrival at colony (7, once) · betrayal/social fight with close
relation (6) · psylink/title milestone (6) · first kill as a *moment* (trait-scaled; the count
stays L1).

### Migration phases (keeps "no rewrite" honest)

1. **Facade refactor (invisible):** Gatherer + Snapshot + `PawnMind`; pawn-summary read sites
   swap over; golden tests prove byte-identical output. No new features.
2. **Memory enhancement (additive):** `Memories(query)` + selector + gated prompt field + L3
   store & deposit policy.
3. **Opportunistic (never big-bang):** event-specific context extractors migrate to the facade
   only when touched for other reasons.

### Open questions

- Facade name (`PawnMind` / `PawnKnowledge` / `PawnContextSource`) and final section list
  (audit `DiaryContextBuilder` read sites).
- The cue/group → `MemoryTag` mapping table (single-sourced; also produces query
  `ContextTags`) — needs an author pass over ~15 cues + interaction groups.
- Default policy numbers (layer weights, per-shape gate chances, decay half-lives) — the
  defaults are the product.
- Fuzz rendering details: writer-provided `fadedText` vs pure vague-ifying transform (lean:
  writer-provided; no clever string surgery); sharp-number threshold for accumulators.
- Phase-1 scope: pawn-summary reads only (lean) vs. also the hottest event extractors.
