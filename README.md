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

## Building & Releasing

`scripts/publish.ps1` builds the DLL, copies **only** the files the mod needs to run
(`About/`, both assemblies, `1.6/Defs/`, `Languages/`) into a clean `dist/PawnDiary`
folder, and snapshots that payload onto a `release/<version>` branch plus a `v<version>`
tag. Your current branch and the committed Debug DLL are left untouched.

```powershell
# Versioned release: builds dist/PawnDiary + branch release/1.0.0-beta.1 + tag v1.0.0-beta.1
powershell -File scripts/publish.ps1 -Version 1.0.0-beta.1

# Just refresh the uploadable dist/ folder, no git snapshot:
powershell -File scripts/publish.ps1 -SkipBranch

# Re-run an existing version (overwrite branch/tag): add -Force
# Ship the Debug build instead of Release:           -Configuration Debug
```

**Uploading to the Steam Workshop:** the mod is published through RimWorld's in-game
uploader (Dev mode → Mods → *Upload to Steam Workshop*), which uploads an entire mod
folder verbatim — so upload the clean `dist/PawnDiary`, not this dev repo. Copy it into
`…/RimWorld/Mods/PawnDiary` and temporarily move this dev folder out of `Mods` so RimWorld
does not see two mods with `packageId aimml.pawndiary`. `About/PublishedFileId.txt` ships
with the payload, so the upload updates the existing item. RimWorld publishes items as
public, so set **Visibility → Friends Only** on the item's Steam page afterwards.

## Documentation

- `DOCUMENTATION.md` explains the architecture, event flow, settings, prompts, and localization.
- `CHANGELOG.md` records dated changes.
