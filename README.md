# Pawn Diary

> **Beta / work in progress.** Pawn Diary is being published to the Steam Workshop as a **hidden beta**
> for testing. Expect rough edges, changing defaults, and ongoing prompt tuning — output quality
> depends heavily on your model and settings. Feedback and bug reports are very welcome.

Pawn Diary gives RimWorld colonists a private journal. It watches meaningful colony moments and asks
a local or OpenAI-compatible language model to rewrite them as short diary pages in each pawn's own
voice.

## What It Adds

- A **Diary** tab on each colonist (placed next to Needs), including corpses.
- First-person pages for social interactions, fights, mental breaks, raids, medicine, crafting,
  work, mood events, thoughts, inspirations, hediff/health turns, and end-of-day reflections.
- Neutral arrival and death pages so a pawn's story has a beginning and an end.
- Paired points of view for two-colonist events: each pawn can remember the same moment differently.
- Writing **personas** that shape a colonist's voice, with the first persona weighted toward the
  pawn's traits and backstory. Tunable or fully custom in the settings.
- Optional LLM-generated entry **titles**, display-only atmospheric formatting for extreme moments,
  and one live health/capacity cue woven into eligible prompts.
- Deep tuning: API lanes, prompt text, personas, and event groups are all editable in settings or XML.

Pawn Diary is storytelling only. It does not add memories, mechanics, needs, hediffs, or jobs, and it
writes no external save data beyond the diary entries stored in the RimWorld save.

## LLM Support

The mod talks to any OpenAI-compatible `/chat/completions` endpoint, and also supports the OpenAI
Responses API and native Ollama chat (with model fetch/pick, per-lane connection tests, reasoning
effort, and Ollama thinking output). It is built for local models, designed around compact prompts
for small to mid-size models (roughly 4–32B) through tools such as LM Studio, llama.cpp, and Ollama.

Default settings expect a local server at:

```text
http://localhost:1234
```

Configure endpoints, models, API keys, concurrency, temperature, prompt text, personas, and event
groups in the mod settings.

## Status

Pawn Diary is an early **beta** under active development, currently shipping as a **hidden Steam
Workshop** release for testing. Expect rough edges, especially around model behavior and prompt
tuning. The mod targets base RimWorld 1.6 and keeps DLC content optional.

## Prompt Lab

For prompt experimentation outside the game, run the prompt-lab harness:

```bash
cd prompt-lab
npm run from-defs
```

See `prompt-lab/README.md` for fixtures and per-model markdown output.

## Documentation

- `DOCUMENTATION.md` explains the architecture, event flow, settings, prompts, and localization.
- `CHANGELOG.md` records dated changes.
