# Pawn Diary — Integration Guide for Other Mods

How another mod (an "adapter") records moments in a colonist's Pawn Diary. This is the **public
contract**: everything described here is stable; everything else in the assembly is an internal
implementation detail that may change without notice.

- Inbound (API v1): an adapter pushes events **into** Pawn Diary.
- Read side (API v2): an adapter can ask for recent diary entry title snapshots for a pawn.
- Planned (not yet shipped): richer pawn-context providers and a fuller read-only context snapshot
  (persona + recent entries). See *Roadmap* below.

## Stability promise

- The public surface is the `PawnDiary.Integration` namespace: `PawnDiaryApi`,
  `ExternalEventRequest`, and read-only DTOs. Adapters must not call anything outside it.
- Evolution is **additive only**: members are added, never renamed, removed, or repurposed.
  `PawnDiaryApi.ApiVersion` (currently `2`) increments when members are added, so an adapter can
  feature-detect: `if (PawnDiaryApi.ApiVersion >= 2) { ... }`.
- `SubmitEvent` **never throws** into the caller and is safe to call at any time (menus included —
  it just returns `false` when no game is loaded).
- A shipped `eventKey` is save-data: it is stored on diary events like a defName. Never rename one.

## Quickstart (C# adapter)

> Working template: `integrations/PawnDiary.ExampleAdapter/` in this repo is a complete, buildable
> adapter (About.xml, csproj, one GameComponent, group XML, adapter-owned Keyed strings). Copy it
> and replace its daily timer with your mod's real trigger. `integrations/README.md` covers
> building and deploying adapters during development.

1. **Reference the DLL.** Compile against `1.6/Assemblies/PawnDiary.dll` (a plain assembly
   reference with `<Private>false</Private>` — do not bundle it).
2. **Declare load order.** In your adapter's `About.xml`, add Pawn Diary to `<modDependencies>`
   (or at least `<loadAfter>`). The core packageId is currently `aimmlegate.pawndiary.development`
   — **verify against the release you target**; it will change for the Workshop release.
3. **Submit events** from your mod's own hooks (main thread only):

```csharp
using PawnDiary.Integration;

var accepted = PawnDiaryApi.SubmitEvent(new ExternalEventRequest
{
    sourceId = "yourname.youradapter",          // your packageId, for log attribution
    eventKey = "youradapter_campfire_story",    // stable, prefixed, lowercase
    subject = pawn,                             // whose diary gets the entry
    partner = otherPawn,                        // optional: eligible partner => pair entry
    summaryText = "...".Translate(...),         // short factual line, localized by YOU
    eventLabel = "...".Translate(...),          // optional UI label; falls back to group label
    extraContext = new List<string>             // optional "key=value" prompt evidence
    {
        "story_topic=the old war",
        "mood=warm",
    },
});
```

4. **Ship the group XML.** Submissions do nothing until an **External-domain
   `DiaryInteractionGroupDef` claims the eventKey** (required-match by design: prompt policy lives
   in XML, and a stray submission stays harmless). Minimal group:

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>youradapterCampfireStory</defName>
  <label>campfire story</label>
  <domain>External</domain>
  <order>1000</order>                <!-- unique-ish within External; lower wins -->
  <important>false</important>
  <instruction>a campfire story shared tonight; write what the story stirred up, not a transcript</instruction>
  <tone>warm and unhurried</tone>
  <matchDefNames>
    <li>youradapter_campfire_story</li>
  </matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```

   `label`, `instruction`, `tone`(`s`) are DefInjected-localizable in your mod. You can also ship a
   narrower `DiaryEventPromptDef` (keyed by your eventKey or group defName) to override the broad
   `DiaryEventPrompt_External` prompt/enhancement.

5. **Test in-game.** Dev mode → Debug Actions → *Pawn Diary* → "Submit test external event..."
   proves the pipeline itself; then trigger your own hook and watch the pawn's Diary tab. If your
   key is unclaimed, Pawn Diary logs one warning naming your `sourceId` and the key.

## API reference (v2)

`PawnDiary.Integration.PawnDiaryApi`:

| Member | Meaning |
|---|---|
| `const int ApiVersion` | Contract version; bumps only when members are added. |
| `bool IsReady` | A game is loaded and the diary component is alive. |
| `bool SubmitEvent(ExternalEventRequest)` | `true` = validated and handed to the pipeline. The pipeline may still decline afterwards exactly like a native event: group disabled in XML, ineligible pawn, or dedup window. `false` = null/incomplete request, no game loaded, off-main-thread call, or unclaimed eventKey (all logged once, attributed to `sourceId`). |
| `List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn, int maxCount)` | Newest completed diary pages for one pawn, newest first. Returns at most 20 snapshots, never prompts or raw responses. Empty list = no game, invalid pawn/count, no completed pages, off-main-thread call, or failure. |

`ExternalEventRequest` fields: `sourceId`*, `eventKey`*, `subject`* (required); `partner`,
`summaryText`, `eventLabel`, `extraContext`, `dedupKey`, `dedupTicks` (optional). Semantics:

- **subject / partner** — subject must be diary-eligible (humanlike colonist) or the event drops.
  A distinct, eligible partner upgrades the event to pairwise (both POVs, like a native social
  event); an ineligible partner quietly downgrades to solo.
- **summaryText** — one factual line; it becomes the entry's raw text and the LLM's "what
  happened" evidence. Sanitized to a single line and length-capped. Localize it yourself — Pawn
  Diary cannot translate your content.
- **extraContext** — short `key=value` lines appended to the prompt's game-context (capped count,
  one line each, `;` becomes `,`). Facts only; the model is instructed to stay inside them.
- **dedup** — default window is `externalEventDedupTicks` (XML-tunable, ~1 in-game hour) keyed by
  `eventKey` + pawn (solo) or the order-independent pawn pair. Pass `dedupKey`/`dedupTicks` to
  collapse related submissions differently; `dedupTicks <= 0` keeps the default window.

Threading: **main thread only** (the pipeline reads DefDatabase/settings and translates text).
From a worker thread, marshal first — e.g. `LongEventHandler.ExecuteWhenFinished(() =>
PawnDiaryApi.SubmitEvent(...))`.

`DiaryEntryTitleSnapshot` fields: `tick`, `date`, `eventId`, `povRole`, `title`, `groupLabel`,
`archived`. `title` is the stored LLM title when one exists; adapters should fall back to
`groupLabel` or `date` for debug/UI display. The snapshot intentionally excludes generated prose,
prompts, raw responses, and live RimWorld objects.

## eventKey conventions

- Lowercase, `snake_case`, **prefixed with your mod's short name**: `rimtalk_conversation`,
  `youradapter_campfire_story`. The prefix is the collision guard between adapters.
- One key per *kind* of moment (per prompt policy you want), not per occurrence.
- Keys are matched case-insensitively by the standard group matchers (`matchDefNames`,
  `matchPrefixes`, `matchSuffixes`, `matchSegments`, `matchTokens`), so a family of keys can share
  one group via a prefix matcher.

## XML-only compatibility (no C# at all)

If the target mod's content already flows through vanilla systems Pawn Diary hooks —
InteractionDefs (social log), ThoughtDefs, HediffDefs, MentalStateDefs, TaleDefs — no adapter code
is needed: a plain `DiaryInteractionGroupDef` in the matching domain with `matchPackageIds` (or
defName matchers) claims that content and owns its prompt policy. Two package gates keep such
groups safe in any mod list:

- `enableWhenPackageIdsLoaded` — the group is active **only** while a listed mod is loaded
  (use this on every compatibility group so it sits inert without its target mod);
- `disableWhenPackageIdsLoaded` — the inverse, for silencing a built-in group when a richer
  replacement mod is present.

`captureRenderedGameText` can be set `false` on Interaction-domain compatibility groups for
conversation-framework mods that schedule follow-up dialogue during grammar rendering.

## Roadmap (planned, not yet available)

> Target selection, per-mod patch plans, and sequencing for everything below live in
> `MOD_COMPAT_PLAN.md`.

- **v3 — pawn-context providers**: `RegisterPawnContextProvider(id, Func<Pawn, string>)`, letting
  personality mods (Psychology, RimPsyche, 1-2-3 Personalities, ...) add lines to the pawn summary
  of every prompt.
- **v4 — outbound context**: a richer read-only snapshot (persona, recent generated entries) so
  chat mods (RimTalk, ...) can use the diary as memory.

Check `ApiVersion` before using newer members.
