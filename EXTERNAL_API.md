# Pawn Diary — External API (short guide)

A one-page overview for mod authors who want another mod to read or write a colonist's diary.
For the full contract, see [`INTEGRATIONS.md`](INTEGRATIONS.md). For a buildable template, copy
[`integrations/PawnDiary.ExampleAdapter/`](integrations/PawnDiary.ExampleAdapter/).

> **Public surface:** the `PawnDiary.Integration` namespace — `PawnDiaryApi` (static facade) plus
> plain request/snapshot DTOs. Anything outside that namespace is internal and may change.

---

## What it does

Pawn Diary normally writes diary pages by hooking vanilla RimWorld events. The **integration API**
lets *your* mod do the same thing: push a moment into a pawn's diary, read diary state back, or feed
your own pawn context into Pawn Diary's prompts. Your adapter stays a normal mod — Pawn Diary owns
the LLM call, prompt framing, safety text, parsing, persistence, and the Diary tab.

Current contract version: `PawnDiaryApi.ApiVersion == 23`. Feature-detect before using newer members:

```csharp
if (PawnDiaryApi.ApiVersion >= 23) { /* use a newer member */ }
```

---

## 30-second quickstart

1. **Reference** `1.6/Assemblies/PawnDiary.dll` (`<Private>false</Private>` — don't bundle it).
2. **Load after** Pawn Diary (add it to your `About.xml` `<modDependencies>` or `<loadAfter>`).
3. **Submit** an event from a main-thread hook:

```csharp
using PawnDiary.Integration;

PawnDiaryApi.SubmitEvent(new ExternalEventRequest
{
    sourceId   = "yourname.youradapter",          // your packageId, for log attribution
    eventKey   = "youradapter_campfire_story",    // stable, lowercase, prefixed
    subject    = pawn,                            // whose diary
    partner    = otherPawn,                       // optional -> paired POV entry
    summaryText= "A campfire story turned into a confession.".Translate(),
});
```

4. **Ship the group XML** so Pawn Diary claims the key (required-match by design — an unclaimed key
   stays harmless and logs one warning):

```xml
<PawnDiary.DiaryInteractionGroupDef>
  <defName>youradapterCampfireStory</defName>
  <label>campfire story</label>
  <domain>External</domain>
  <order>1000</order>
  <instruction>a campfire story shared tonight; write what it stirred up, not a transcript</instruction>
  <matchDefNames><li>youradapter_campfire_story</li></matchDefNames>
</PawnDiary.DiaryInteractionGroupDef>
```

That's it. Open the pawn's Diary tab and trigger your hook.

---

## What can the API do?

| You want to… | Call | Since |
|---|---|---|
| Push an event for Pawn Diary to write about | `SubmitEvent` / `SubmitEventWithHandle` | v1 / v8 |
| Push your **own final prose**, no LLM rewrite | `SubmitDirectEntry` | v9 |
| Push an entry **idea** (instruction), Pawn Diary writes it | `SubmitPromptEntry` | v22 |
| Know *why* a submit didn't record | `SubmitEvent(req, out SubmitEventOutcome)` | v23 |
| Inspect the prompt an event would produce (no tokens spent) | `PreviewPrompt` | v15 / v22 |
| Be told when an entry finishes generating | `RegisterEntryStatusListener` | v10 |
| Poll one entry's status / read its prose | `GetEntryStatus` / `GetEntrySnapshot` | v8 / v17 |
| Read recent titles, memory summaries, or counts | `GetRecentEntryTitles` / `GetContextSnapshot` / `GetEntryStats` | v2 / v7 / v20 |
| Read the structured pawn summary Pawn Diary would prompt with | `GetPawnSummary` | v6 |
| Read the live condition/enchantment candidates | `GetPromptEnchantments` | v6 |
| Read or set the pawn's writing style / generation toggle | `GetWritingStyle`, `SetWritingStyleOverride`, `IsDiaryGenerationEnabled`… | v3 / v12–14 |
| Contribute one `key=value` context line to every pawn summary | `RegisterPawnContextProvider` | v4 |
| Get style + summary + enchantments + recent memory in one call | `GetContextBundle` | v21 |

Full signatures and field semantics: [`INTEGRATIONS.md`](INTEGRATIONS.md) § API reference.

---

## Hard rules (these will bite you if ignored)

- **Main thread only.** Every call reads DefDatabase/settings and translates text. From a worker
  thread, queue the request in your mod and drain it from `GameComponentUpdate` or `OnGUI`. Off-thread
  calls return the safe empty value (false / null / empty list) and log a diagnostic.
- **`SubmitEvent` never throws** into the caller. It returns `false` (or `recorded=false`, or an
  empty list) on any failure — no game loaded, master toggle off, off-thread, invalid request,
  unclaimed key, exhausted budget, or pipeline drop. One log line per cause, attributed to your
  `sourceId`.
- **Master toggle.** Players can disable all integrations in Pawn Diary's settings
  (*Allow external mod integrations*). When off, submissions and reads return safe empty values and
  registered providers/listeners are not invoked — but registration is still accepted, so things
  work again if the player re-enables.
- **`eventKey` is save-data.** It's stored on diary events like a defName. **Never rename one** you
  have shipped. Lowercase, `snake_case`, prefixed with your mod's short name (`youradapter_*`).
- **Additive only.** The public surface only ever grows — members are added, never renamed, removed,
  or repurposed. `ApiVersion` bumps only on additions, so feature-detection is safe across versions.
- **Prompt ownership stays with Pawn Diary.** Adapter inputs (`summaryText`, `extraContext`,
  `promptFragment`, `promptInstruction`) are cleaned, capped, and placed *inside* the normal prompt
  wrapper. Adapter-controlled strings are flattened so they can only ever be a field *value* — they
  cannot forge internal `gameContext` keys. Reserved keys in `extraContext` are dropped.
- **Token budget.** Token-spending submissions (`SubmitEvent*`, `SubmitPromptEntry`, and
  `SubmitDirectEntry` only when it may queue title generation) reserve against an XML-tuned rolling
  budget with per-source and global caps. State is transient (not saved). If you burst-submit and
  start getting `DroppedBudget`, back off — the reservation is refunded when the pipeline drops a
  valid event, so drops don't permanently consume your window.

---

## Three submission paths at a glance

| Path | Who writes the prose? | LLM tokens spent? | External group required? |
|---|---|---|---|
| `SubmitEvent` / `SubmitEventWithHandle` | Pawn Diary (normal generation) | Yes | **Yes** (claims `eventKey`) |
| `SubmitPromptEntry` (v22) | Pawn Diary, from your instruction | Yes | Optional |
| `SubmitDirectEntry` (v9) | **You** (final text passed in) | No (unless title generation) | Optional |

---

## Read snapshots never leak internals

Every read returns plain DTOs (`DiaryEntryTitleSnapshot`, `DiaryEntrySnapshot`,
`DiaryContextSnapshot`, `DiaryPawnSummarySnapshot`, …) — sealed field bags, no live RimWorld objects.
They never include prompts, raw provider responses, fallback facts, or in-flight pages. List fields
are independent copies: mutating game state after the call does not change a snapshot you already hold.

---

## Where to go next

- **Full contract & field-by-field semantics** — [`INTEGRATIONS.md`](INTEGRATIONS.md)
- **Buildable reference adapter** — [`integrations/PawnDiary.ExampleAdapter/`](integrations/PawnDiary.ExampleAdapter/)
  (copy it and swap the timer for your trigger); see also [`integrations/README.md`](integrations/README.md)
- **Architecture / data flow** — [`DOCUMENTATION.md`](DOCUMENTATION.md) §3.7
- **Planned (not-yet-shipped) capabilities** — [`design/EXTERNAL_API_CAPABILITIES.md`](design/EXTERNAL_API_CAPABILITIES.md)
- **Test the pipeline in-game** — Dev mode → Debug Actions → *Pawn Diary* → "Submit test external event…"
