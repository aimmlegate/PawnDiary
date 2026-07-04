# Pawn Diary — Integration Guide for Other Mods

How another mod (an "adapter") records moments in a colonist's Pawn Diary. This is the **public
contract**: everything described here is stable; everything else in the assembly is an internal
implementation detail that may change without notice.

- Inbound (API v1): an adapter pushes events **into** Pawn Diary.
- Read side (API v2): an adapter can ask for recent diary entry title snapshots for a pawn.
- Read side (API v3): an adapter can also ask for a pawn's base diary **writing style** as context.
- Context feed (API v4): an adapter can add compact pawn-context lines to Pawn Diary prompts.
- Read side (API v5): an adapter can filter recent title snapshots by domain, atmosphere, date/tick,
  POV, and archived state.
- Planned (not yet shipped): a fuller read-only context snapshot (recent generated entry prose). See
  *Roadmap* below.

## Stability promise

- The public surface is the `PawnDiary.Integration` namespace: `PawnDiaryApi`,
  `ExternalEventRequest`, provider registration, and read-only DTOs. Adapters must not call anything
  outside it.
- Evolution is **additive only**: members are added, never renamed, removed, or repurposed.
  `PawnDiaryApi.ApiVersion` (currently `5`) increments when members are added, so an adapter can
  feature-detect: `if (PawnDiaryApi.ApiVersion >= 5) { ... }`.
- `SubmitEvent` **never throws** into the caller and is safe to call at any time (menus included —
  it just returns `false` when no game is loaded).
- The Pawn Diary settings window has a master **Allow external mod integrations** switch. When it is
  off, submissions and read calls return their safe empty value and registered context providers are
  not invoked. Registration itself is still accepted so providers work again if the player re-enables
  the switch.
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

## API reference (v5)

`PawnDiary.Integration.PawnDiaryApi`:

| Member | Meaning |
|---|---|
| `const int ApiVersion` | Contract version; bumps only when members are added. |
| `bool IsReady` | A game is loaded and the diary component is alive. |
| `bool SubmitEvent(ExternalEventRequest)` | `true` = validated and handed to the pipeline. The pipeline may still decline afterwards exactly like a native event: group disabled in XML, ineligible pawn, or dedup window. `false` = null/incomplete request, no game loaded, off-main-thread call, or unclaimed eventKey (all logged once, attributed to `sourceId`). |
| `List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn, int maxCount)` | Newest completed diary pages for one pawn, newest first. Returns at most 20 snapshots, never prompts or raw responses. Empty list = no game, invalid pawn/count, no completed pages, off-main-thread call, or failure. |
| `List<DiaryEntryTitleSnapshot> GetRecentEntryTitles(Pawn, int maxCount, DiaryEntryTitleQuery query)` (v5) | Same title snapshot shape, filtered by optional query fields. Null or empty query preserves the v2 behavior. |
| `DiaryWritingStyleSnapshot GetWritingStyle(Pawn)` (v3) | The pawn's **base** saved diary writing style. Publishes the diary's own voice instruction (`rule`) so a chat/context mod can — if its player chooses — align its voice with how the pawn writes; Pawn Diary only exposes the style, it never reads or drives another mod's persona. `null` = null/ineligible pawn, no game, or off-main-thread call. This is a side-effect-free read: it never creates a diary record (a pawn with no record yet resolves to the default style), and it excludes temporary hediff style overrides. |
| `void RegisterPawnContextProvider(string id, Func<Pawn, string> provider)` (v4) | Registers a process-global provider that contributes one compact `key=value` line to each pawn summary. Re-registering the same id replaces the provider. Invalid/off-thread registration is logged once and ignored; a throwing provider is disabled for the rest of the session and logged once. |

`ExternalEventRequest` fields: `eventKey`*, `subject`* (required); `sourceId` (recommended, for log
attribution — defaults to `unknown-source` when blank), `partner`, `summaryText`, `eventLabel`,
`extraContext`, `dedupKey`, `dedupTicks` (optional). Semantics:

- **subject / partner** — subject must be diary-eligible (humanlike colonist) or the event drops.
  A distinct, eligible partner upgrades the event to pairwise (both POVs, like a native social
  event); an ineligible partner quietly downgrades to solo.
- **summaryText** — one factual line; it becomes the entry's raw text and the LLM's "what
  happened" evidence. Sanitized to a single line and length-capped. Localize it yourself — Pawn
  Diary cannot translate your content.
- **extraContext** — short `key=value` lines appended to the prompt's game-context (capped count
  and line length, one line each, `;` becomes `,`). Facts only; the model is instructed to stay
  inside them.
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

`DiaryEntryTitleQuery` fields are optional unless noted:
`domain` (semantic event domain such as `External`, `Thought`, or `Interaction`), `atmosphereCue`,
`povRole`, `dateContains`, `minTick`, `maxTick`, `includeActive` (default `true`), and
`includeArchived` (default `true`). String filters are case-insensitive exact matches except
`dateContains`, which is a case-insensitive substring match. Tick bounds are inclusive; negative
bounds mean no bound.

`DiaryWritingStyleSnapshot` fields: `styleDefName`, `label`, `rule`. `rule` is the plain-language
voice instruction the diary feeds its own prompts (e.g. *"two short concrete sentences: visible
action first, feeling only implied by the final detail"*) and is the useful field for adapters that
want the pawn's voice as context; `label` is a short handle and `styleDefName` is opaque save data.
The snapshot is the **base** saved style: it does not reflect temporary hediff-driven style
overrides (those are prompt-time only) and carries no internal theme tags or live RimWorld objects.

`RegisterPawnContextProvider` providers run on the main thread during prompt context collection.
They receive the live `Pawn` so the adapter can read its own data, then return a plain line such as
`personality=blunt, curious, slow to trust`; return `null` or empty for pawns the adapter does not
model. Pawn Diary sanitizes the line exactly like `extraContext`: rich text/control characters are
flattened, line breaks collapse, `;` becomes `,`, blank lines are skipped, and provider output is
count/length capped. Provider lines are inserted beside identity fields (`xenotype=`, `title=`,
`faith=`), before transient state such as `mood=`.

Example:

```csharp
if (PawnDiaryApi.ApiVersion >= 4)
{
    PawnDiaryApi.RegisterPawnContextProvider("youradapter.personality", pawn =>
    {
        var data = YourPersonaStore.For(pawn);
        return data == null ? null : "personality=" + data.ShortSummary;
    });
}
```

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

> **This file is the shipped contract only.** All integration *ideas, roadmap, version ledger, and
> per-mod target plans* are reconciled in one place: [`design/MOD_COMPAT_PLAN.md`](design/MOD_COMPAT_PLAN.md).
> Consult that document for the next planned members and their design; the short list below is just
> a pointer so adapter authors know what is coming.

- **Future richer outbound context**: recent generated entry prose (not just titles) so chat
  mods (RimTalk, ...) can use the diary as fuller memory. The writing-style half of this idea
  already shipped in v3 (`GetWritingStyle`), and title filtering shipped in v5; prose-level entry
  text is what remains.

Check `ApiVersion` before using newer members.
