# How Pawn Diary works

Pawn Diary turns selected, verified RimWorld moments into first-person pages without making the game wait for an API response.

```mermaid
flowchart LR
    A["Gameplay event or observed state"] --> B["Eligibility, filters, chance, and duplicate checks"]
    B --> C["Event classification and page shape"]
    C --> D["Frozen pawn and event context"]
    D --> E["Prompt and model lane"]
    E --> F["Cleaned page, title, save, and Diary UI"]
```

## A few useful terms

An **event** is a confirmed gameplay moment submitted to Pawn Diary. A saved **page** is the resulting diary entry. An **observation** is a lasting state found by comparing the game now with the last stored state. A **batch** holds several related moments for one later page, while a **reflection** turns accumulated evidence into a scheduled day, quadrum, belief, or longer-arc page.

**POV** means point of view: whose first-person knowledge and voice the page uses. An **API lane** is one configured endpoint-and-model row available for generation and failover.

## Who can own a diary

The normal owner is a humanlike colonist old enough to use the first-person minimum age in the tuning policy. Generation can also be disabled for an individual pawn. These checks apply again when a moment is dispatched, so noticing an event does not automatically create a page.

Arrival establishes the start of a pawn's valid diary lifetime. Death closes it. Those boundaries prevent ordinary pages from being placed before the pawn joined or after the pawn died; neutral arrival and death descriptions record the boundary itself.

## What happens when a moment is noticed

RimWorld can confirm a moment immediately, a scheduled scanner can discover a state change, several signals can be correlated into one event, or an adapter can submit an external event. In every case, live game objects are read on the game thread and the relevant facts are copied into a bounded payload. The background request therefore works from a frozen account of the moment rather than continuing to inspect a changing pawn or map.

The common dispatcher then checks:

- whether a game and valid diary owner exist;
- the per-pawn and global settings;
- whether the matched event group is enabled;
- route-specific semantic eligibility;
- native source chance and the selected Events frequency profile;
- recent duplicate or source-ownership claims; and
- low-salience pacing limits where applicable.

For routes that use XML classification, the first matching group supplies the settings label and policy: importance, combat treatment, card color cue, instruction, tone, and any batching or page-shape behavior. Some dedicated sources own their result directly and suppress a generic Tale or notification that describes the same occurrence.

## Page shapes

- **Solo:** one pawn writes one first-person page.

- **Paired:** each eligible pawn receives a separate first-person page grounded in that pawn's role and knowledge. There is no shared omniscient entry.

- **Colony or map fan-out:** an incident such as a raid or selected quest phase can offer an independent solo page to each admitted colonist, or to a deterministic witness when that policy uses one.

- **Batched:** related interactions, Tales, or ambient notes wait for a bounded flush and become a summarized page.

- **Reflection:** accumulated evidence can produce a day, quadrum, narrative-arc, or belief reflection.

- **Lifetime description:** arrival and death use neutral description templates rather than pretending the pawn wrote outside their diary lifetime.

## Pacing

Everyday, low-salience pages can be capped per pawn and day. A folded moment does not always vanish: eligible facts can be retained as digest evidence for a later reflection. Important events, combat, colony fan-out, and configured batch routes use their own admission and pacing policies instead of automatically sharing the everyday cap.

Chance still applies where the route or group enables it. The Events frequency profile multiplies that native chance through the classified group. Deterministic events remain limited to the one occurrence that actually happened; shared fan-outs sample once, and delayed batches retain their frozen result. A frequency skip can still preserve allowlisted important knowledge without creating or queueing a page.

## Generation and persistence

After the main thread freezes context and selects a template, the HTTP request runs away from the game thread. Configured lane concurrency, retry, cooldown, and ordered failover rules decide when and where it runs. Completed work returns through a main-thread result drain, where Pawn Diary can safely update game-owned state.

A successful response is parsed and cleaned before it becomes a page. If titles are enabled, a separate short title request may follow the completed main page. The page is stored, shown in the Diary UI, and moved between active and archived collections according to retention settings.

Save/load preserves durable diary pages, boundaries, preferences, and other serializable state. Transient work such as live references and in-flight queues is cancelled, recovered, or rebuilt at the next safe game boundary rather than being serialized as an unsafe background operation.

## What leaves the game

For a normal generated page, Pawn Diary sends the chosen model its assembled system instruction and user prompt. Depending on the template and context preset, that prompt can contain the event facts, pawn description, relationships, current setting, memories, beliefs, and other selected context. The request also carries generation controls and the lane's configured authentication.

[Prompt building and outbound data](Prompt-Building.md) describes each current path—including optional error reports and adapter behavior—without assuming that every noticed event is transmitted.

## Source of truth

- Front door and dispatcher: [DiaryEvents](../../Source/Ingestion/DiaryEvents.cs), [DiaryGameComponent.Dispatch](../../Source/Core/DiaryGameComponent.Dispatch.cs), and [DiaryEventCatalog](../../Source/Capture/Catalog/DiaryEventCatalog.cs).
- Eligibility and lifetime: [DiaryGameComponent.GenerationEligibility](../../Source/Core/DiaryGameComponent.GenerationEligibility.cs) and [DiaryGameComponent.Lookup](../../Source/Core/DiaryGameComponent.Lookup.cs).
- Prompt and transport: [DiaryPromptPlanner](../../Source/Pipeline/DiaryPromptPlanner.cs), [PromptAssembler](../../Source/Generation/PromptAssembler.cs), and [LlmClient](../../Source/Generation/LlmClient.cs).
- Storage and UI: [Source/Core](../../Source/Core/) and [Source/UI](../../Source/UI/).
