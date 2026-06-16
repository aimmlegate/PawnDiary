# Pawn Diary

Pawn Diary gives RimWorld colonists a private journal. It watches important colony moments and asks
a local or OpenAI-compatible language model to turn them into short diary pages in each pawn's own
voice.

## What It Adds

- A **Diary** tab on colonist pawns, placed beside the vanilla Social tab.
- First-person pages for social interactions, fights, mental breaks, raids, medicine, crafting,
  work, mood events, thoughts, and end-of-day reflections.
- Neutral arrival and death pages so a pawn's story has a beginning and an end.
- Paired points of view for two-colonist events: each pawn can remember the same moment differently.
- Optional persona presets that shape a colonist's writing style without changing gameplay.
- Prompt and event-group settings for players who want to tune what gets written and how.

Pawn Diary is storytelling only. It does not add memories, mechanics, needs, hediffs, jobs, or any
external save data beyond the diary entries it stores in the RimWorld save.

## LLM Support

The mod talks to any OpenAI-compatible `/chat/completions` endpoint. It is built for local models and
has been designed around compact prompts for 6-31B models through tools such as LM Studio, llama.cpp,
and Ollama.

Default settings expect a local server at:

```text
http://localhost:1234
```

Configure the endpoint, model, API key, concurrency, prompt text, and event groups in the mod
settings.

## Status

Pawn Diary is experimental and under active development. Expect rough edges, especially with model
behavior and prompt tuning. The mod targets base RimWorld 1.6 and keeps DLC content optional.

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
