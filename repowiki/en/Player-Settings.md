# Player settings

Pawn Diary's settings follow the five tabs shown in the mod settings window. Most players only need Main and Events; the other tabs change voice or advanced prompt policy.

| Tab | What it controls |
|---|---|
| Main | API lanes, routing, reading mode, generation frequency, context detail, storage, integrations, and optional error reporting |
| Prompts | shared system prompts plus event-specific prompt, enhancement, and model overrides |
| Styles | writing-style and psychotype presets |
| Events | automatic-capture group toggles |
| Tuning | experimental low-level overrides, visible only after its opt-in gate is enabled |

## Main

### API lanes and routing

Each lane is an endpoint row that can participate in generation and ordered failover. A lane is active only when:

- the row is enabled;
- its URL is not blank; and
- its model is not blank.

The API key and authentication mode depend on the endpoint. A connection test checks the selected row, but it does not make an incomplete row active. Event prompt policy can prefer or force a particular model; otherwise the normal lane order and failover rules apply. A failed lane can enter cooldown while another eligible lane is tried.

Reading mode is independent of generation routing: it changes how saved diary content is presented, not which gameplay events are captured.

### Context detail

The global preset controls how much optional context competes for prompt space:

| Preset | Practical effect |
|---|---|
| Full | Keeps the widest set of eligible continuity, identity, setting, and live-context fields within the configured budgets. |
| Balanced | Retains core facts and a moderate selection of supporting context. |
| Compact | Prioritizes the event and required identity facts, with a smaller optional-context budget. |

Each lane can inherit the global preset or select its own override. “Use global” therefore follows later global changes; choosing Full, Balanced, or Compact on a lane pins that lane to the selected level.

Required event facts do not become optional merely because Compact is selected. Blank and sentinel-valued fields are still omitted in every preset.

### Generation frequency

Generation frequency is a multiplier on configured event chances. Raising it makes eligible probabilistic routes more likely; lowering it makes them less likely. It is not a guarantee that every noticed event becomes a page: group enablement, semantic checks, duplicates, daily pacing, batching, lane readiness, and response failure still apply.

### Retention

The active-page limit controls how many recent pages remain in the main diary collection. Older pages can move into the archive. The archived-page limit then bounds that collection as well. These are storage policies, not generation quotas, and changing them can prune older retained pages when the limits are applied.

### Integrations and error reporting

The master external-integration switch gates public API submissions and bridge behavior that depends on it. Individual adapters can also expose their own tier or mode settings.

Automatic error reporting is a separate opt-in setting. It only transmits a scrubbed Pawn Diary error report when enabled; it is not required for generation. See [outbound data](Prompt-Building.md#what-can-leave-the-game) for the exact current payload categories.

## Prompts

This tab edits shared prompt text and event-specific overrides. A matching player override wins over the shipped XML prompt policy. A model override pins the event when an active lane has that model; unknown or inactive model text is ignored and the normal active-lane routing policy is used.

The controls are policy editors, not a general template language. Ordered template fields and their source-backed contracts are listed in the [prompt reference](reference/Prompt-Reference.md).

## Styles

Writing-style presets shape prose direction. Psychotypes shape the pawn's broader diary voice, and the effective voice can also be influenced by traits or an enabled compatibility bridge. These settings do not manufacture event facts; they are added to the system-side voice instruction when the selected template allows persona/style context.

## Events

Each row represents one XML classification group, not one individual matcher. Disabling a group prevents its automatic capture policy from admitting new events. Defaults come from XML, but once a player saves an explicit override that saved choice wins over a later XML default change.

Quest acceptance is off by default; completion and failure are on. Optional DLC and mod-aware groups remain harmless when their required content is absent.

## Tuning

Tuning exposes low-level experimental overrides only after the player deliberately opts in. These controls can change pacing, budgets, caps, scans, or other policy values and are easiest to reason about with prompt-test mode and the testing references. Leaving the gate off uses shipped XML tuning.

## Per-pawn Diary controls

From a pawn's Diary UI, the feather/voice control opens that pawn's writing-style and psychotype editor. These are ordinary per-pawn voice controls.

The current per-pawn **generation enabled** switch is exposed in the same area only while RimWorld Dev Mode is active. It is not a normal Main-tab toggle. When disabled, future LLM generation for that pawn is gated off; already recorded moments can remain visible as raw diary entries, and existing generated pages remain.

## Prompt-test mode

With RimWorld Dev Mode active, Main exposes prompt-test mode. It captures the assembled system and user prompts for the global context preset without selecting a real endpoint, sending the generation request, or spending model tokens. Because no real page request is sent, prompt-test mode itself is also a reason that a test produces a captured prompt but no saved page.

## If no page appears

Check, in this order:

1. The pawn is a humanlike colonist, inside the valid diary lifetime and age rules, and has generation enabled.
2. The matching Events group is enabled and any DLC/mod prerequisite is present.
3. Chance, generation frequency, semantic eligibility, duplicate suppression, or daily pacing did not reject the moment.
4. At least one enabled API lane has both a URL and model, and any forced model can be resolved.
5. The selected lane is not unavailable from cooldown, concurrency pressure, authentication failure, timeout, or exhausted failover.
6. The route is not waiting for a batch flush or saving the fact as reflection evidence.
7. Prompt-test mode is off when you expect a real request and saved page.

The [event catalog](reference/Event-Catalog.md) gives the exact group and expected evidence for a reproducible test.

## Source of truth

- Settings surface: [PawnDiaryMod.SettingsWindow](../../Source/Settings/PawnDiaryMod.SettingsWindow.cs) and [PawnDiarySettings](../../Source/Settings/PawnDiarySettings.cs).
- Lane policy: [PawnDiaryMod.ApiLanes](../../Source/Settings/PawnDiaryMod.ApiLanes.cs) and [LlmClient](../../Source/Generation/LlmClient.cs).
- Pawn controls: [Source/UI](../../Source/UI/).
