---
name: pawndiary-engineering
description: PawnDiary repository workflow for Claude Code, Codex, and OpenCode. Use when implementing or changing behavior in this repo.
---

# PawnDiary Engineering Skill

## When to use
- Any feature, fix, refactor, or settings/behavior change in this repository.
- Any change that touches RimWorld runtime code, prompts, defs, or agent instructions.

## Core process
1. Understand scope first
   - Read relevant files before editing.
   - Keep changes surgical and tied to the requested outcome.

2. Respect runtime constraints
   - Target RimWorld Unity Mono runtime.
   - Do not add unsupported JSON/runtime dependencies; use existing `MiniJson.cs`.

3. Implement with minimum surface area
   - Prefer existing patterns/files over new abstractions.
   - Avoid unrelated cleanup.

4. Validate
   - Build command:
     - `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`
   - If `MSBuild` is unavailable in the environment, run the closest available build command and report the environment limitation.

5. Document as part of done
   - Update `DOCUMENTATION.md` whenever behavior/structure changes.
   - Add a dated changelog line in `DOCUMENTATION.md`.

## Red flags
- Skipping documentation updates after structural/behavior changes.
- Adding dependencies incompatible with RimWorld Mono runtime.
- Making broad edits unrelated to the task.

## Verification
- Build attempted and result reported.
- `DOCUMENTATION.md` updated when required, including a dated changelog entry.
- No unrelated files modified.
