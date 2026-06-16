# PawnDiary

LLM-written diaries for RimWorld colonists.

## What it does

Watches in-game events — social interactions, fights, mental breaks, injuries, deaths, raids, crafts — and rewrites each into a short **first-person diary entry**. Entries appear in a new inspector tab on each colonist.

- **Diary entries, not chat.** Reflective prose written after the fact, not real-time dialog.
- **In-game data only.** Uses what RimWorld already tracks: relationships, moods, traits, events. No external memory, no extended context, no game-mechanic changes.
- **Minimal prompts, small models.** Designed for 6–31B local LLMs (LM Studio, llama.cpp, Ollama). Works with any OpenAI-compatible `/chat/completions` endpoint.
- **Per-pawn personas.** Optional writing-style presets shape each colonist's voice.
- **Paired POV.** Two-pawn events generate two entries — one from each perspective.

## Setup

Point the mod at your LLM endpoint in settings. Defaults work out of the box with a local server on `http://localhost:1234`.

For prompt experimentation outside the game, run the `prompt-lab` harness:

```bash
cd prompt-lab
npm run from-defs
```

See `prompt-lab/README.md` for multi-version fixtures and per-model markdown output.

⚠️ **Experimental / heavy WIP.** Expect rough edges. See `DOCUMENTATION.md` for internals.
