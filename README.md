# Pawn Diary

Pawn Diary is a RimWorld 1.6 mod that gives colonists a private journal.

The mod watches meaningful colony events and sends compact prompts to a configured language model.
Generated entries appear in a per-colonist **Diary** tab, written as short first-person pages from
that pawn's point of view.

Pawn Diary is for storytelling only. It does not add needs, jobs, hediffs, memories, weapons,
buildings, or gameplay pressure.

## What It Adds

- A **Diary** tab for eligible colonists, including colonist corpses.
- First-person pages for social interactions, fights, mental breaks, raids, quests, rituals,
  abilities, inspirations, thoughts, mood events, medicine, health changes, work, crafting,
  arrivals, deaths, and end-of-day reflections.
- Paired points of view for two-pawn events, so both pawns can write about the same moment
  differently.
- Neutral arrival and death pages, giving a pawn's diary a beginning and an end.
- Optional generated entry titles.
- Display-only atmospheric formatting for some intense or strange entries.
- Writing-style presets that can be tuned in the mod settings.
- Per-pawn **psychotypes** — an independent "outlook" layer (what a pawn notices, values, and fears)
  that colors their diary alongside the writing style, rolled from their skill passions. Children keep
  a diary in a naive voice, and both layers re-roll when a pawn grows up. Edited from the same per-pawn
  editor as the writing style; toggle it off in settings if you prefer.
- Prompt Studio for editing shared and event-specific prompt text.

## Model And API Support

Pawn Diary can use local models, hosted APIs, or several configured models at the same time.

Supported request styles:

- OpenAI-compatible Chat Completions
- OpenAI Responses API
- Local OpenAI-compatible servers, including LM Studio, llama.cpp, text-generation-webui, and
  Ollama's OpenAI-compatible endpoint

The settings menu supports multiple **API lanes**. Each lane can have its own endpoint URL, model
name, API key behavior, compatibility mode, and reasoning setting. Lanes can be enabled, disabled,
reordered, tested, and used for failover.

Routing options let you spread requests across models, prefer the top configured rows, or keep extra
models as backups. Event prompts can also prefer a specific configured model when you want certain
diary types handled separately.

Reasoning-model support is experimental. If you use a reasoning model, start with reasoning effort
set to **low**. Higher reasoning settings can be slower, use more tokens, and produce less consistent
short diary entries.

By default Pawn Diary expects a local server at:

```text
http://localhost:1234
```

Output quality depends heavily on the model and settings. Models that follow instructions well and
stay close to supplied facts usually produce better diary pages.

## For Other Mods (Integration API)

Other mods can read or write a colonist's diary through a stable public C# surface in the
`PawnDiary.Integration` namespace — push events, inject your own prose, read diary state back, or
contribute pawn context to Pawn Diary's prompts.

- **Short guide** — [`EXTERNAL_API.md`](EXTERNAL_API.md) (overview + 30-second quickstart)
- **Full contract** — [`INTEGRATIONS.md`](INTEGRATIONS.md) (every member, field, and rule)
- **Buildable template** — [`integrations/PawnDiary.ExampleAdapter/`](integrations/PawnDiary.ExampleAdapter/)

## Save Safety

Pawn Diary is safe to add to an existing save. It starts recording future events after the mod is
loaded.

Removing the mod stops diary capture and hides the Diary UI/history. It does not leave custom
gameplay defs, needs, hediffs, jobs, pawn components, or map components behind.

## Current Status

This is the first public release, so bugs are expected. Please report issues with the RimWorld log,
your model/API setup, and a short description of what happened.

The mod targets base RimWorld 1.6. Paid DLC is not required. Optional DLC events can be recognized
when that content is present.

## Requirements

- RimWorld 1.6
- Harmony
- A local or hosted OpenAI-compatible model endpoint

## Repository Documentation

The detailed, GitHub-readable repository wiki is maintained under
[`repowiki/`](repowiki/README.md). It preserves the generated page structure while remaining a
manual, reviewable documentation snapshot. `DOCUMENTATION.md` remains the authoritative compact
architecture and maintainer guide.
