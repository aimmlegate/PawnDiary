# prompt-lab

Lightweight Node harness for testing PawnDiary prompt payloads outside RimWorld.

## Run generated prompts from XML defs

```bash
cd prompt-lab
npm start
```

This reads:

- `1.6/Defs/DiaryPromptDef.xml`
- `1.6/Defs/DiaryPersonaDefs.xml`
- `1.6/Defs/DiaryInteractionGroupDefs.xml`

and generates multiple versions for:

- paired interaction prompts (initiator and recipient),
- solo prompts,
- arrival descriptions,
- death descriptions,
- title follow-ups.

Use:

```bash
npm run from-defs -- --from-defs --all
```

### Save per-model markdown results

```bash
node run.js --from-defs --all --save --model local-model
```

Saved files:

- `prompt-lab/results/<model-name>/<timestamp>.md`

Where `<model-name>` is a filesystem-safe model label.

## Useful overrides

- `--endpoint http://127.0.0.1:1234/v1`
- `--model <name>`
- `--api-key <key>`
- `--temperature <float>`
- `--max-tokens <int>`
- `--timeout <seconds>`
- `--include-groups <n>`
- `--include-personas <n>`
- `--case <fixture-id>` (single case filter)
- `--dry-run` (build prompt only)
- `--verbose` (print payload)

## Manual fixtures

Drop JSON fixtures into `prompt-lab/prompts/fixtures/` and run with:

```bash
node run.js --case arrival-v2
```
