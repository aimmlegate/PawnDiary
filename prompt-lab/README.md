# prompt-lab

Lightweight Node harness for testing PawnDiary prompt payloads outside RimWorld.

## Run generated prompts from XML defs

```bash
cd prompt-lab
npm start
```

This reads:

- `1.6/Defs/DiaryPromptDef.xml`
- `1.6/Defs/DiaryPromptTemplateDefs.xml`
- `1.6/Defs/DiaryPersonaDefs.xml`
- `1.6/Defs/DiaryInteractionGroupDefs.xml`

and generates multiple versions for:

- paired interaction prompts (initiator and recipient),
- solo prompts,
- arrival descriptions,
- death descriptions,
- title follow-ups.

Generated fixtures are derived from the current XML catalog and honor the harness's configured
group exclusions.

Generated fixtures select the same template keys as the in-game `DiaryPromptBuilder`
(`PairDefault`, `PairImportant`, `SoloInternalState`, `DeathDescription`, `Title`, and so on), then
render field order and inclusion from `DiaryPromptTemplateDefs.xml`. Interaction fixtures also
append the current Keyed direct-speech cue for the active POV pawn when the template allows it.

First-person fixtures append the pawn's persona voice to the **system prompt** (wrapped by the
`PawnDiary.Prompt.PersonaVoice` Keyed string), mirroring `DiaryPromptBuilder.ComposeSystemPrompt` —
persona is no longer a user-message field. Templates with `includePersona=false` (the neutral
death/arrival chronicles and the title follow-up) stay persona-free.

Use:

```bash
npm run from-defs
```

## Run every event group with fixed prompt-enchantment variants

Use exhaustive mode when comparing model stability across repeated passes or across different
models:

```bash
node run.js --all-variants --passes 2 --save --no-title --model local-model
```

`--all-variants` reads every eligible XML interaction/event group after configured exclusions and
builds deterministic cases for:

- paired groups: initiator and recipient POV,
- solo groups: one POV,
- neutral arrival/death fixtures,
- title fixtures.

First-person cases are crossed with the fixed prompt-enchantment matrix:

- no prompt enchantment,
- moderate pain,
- major blood loss,
- critical consciousness,
- feverish sickness,
- intoxication,
- sensory loss.

The same case ids and prompt contexts are reused on every run. `--passes <n>` repeats each case with
the same prompt and a `pass-XX-` id prefix, making same-model stability comparisons straightforward.

Saved `--all-variants` runs default to compact markdown. Each case stores:

- `Prompt` - the full chat prompt (`system` and `user`),
- `Parsed result` - the generated text extracted from the compatible chat response.

Override the prompt-enchantment matrix in `prompt-lab.config.json` with:

```json
{
  "generated": {
    "promptEnchantmentVariants": [
      { "key": "none", "label": "no prompt enchantment", "promptEnchantment": "" },
      { "key": "pain", "label": "moderate pain", "promptEnchantment": "high priority; moderate bruise in left arm; pain" }
    ]
  }
}
```

### Save per-model markdown results

```bash
node run.js --from-defs --save --model local-model
```

Saved files:

- `prompt-lab/results/<model-name>/<timestamp>.md`

Where `<model-name>` is a filesystem-safe model label.

Results now include both:

- Main response
- LLM-generated title (follow-up request), matching the in-game title flow

The title is generated with:

- same endpoint and model as the main call
- temperature copied from the main request
- title fields rendered from the XML `Title` template
- title user instruction read from the template, falling back to `DiaryPromptDef.titleUserInstruction`
- title tokens capped to `40` (same as in-game)

Disable title follow-up locally with:

```bash
node run.js --from-defs --no-title
```

## Useful overrides

- `--endpoint http://127.0.0.1:1234/v1`
- `--model <name>`
- `--api-key <key>`
- `--temperature <float>`
- `--max-tokens <int>`
- `--timeout <seconds>`
- `--include-groups <n|all>`
- `--include-personas <n|all>`
- `--all-variants` (all event groups crossed with fixed prompt-enchantment variants)
- `--passes <n>` (repeat the same prompt set for stability checks)
- `--compact` / `--compact-md` (save prompt + parsed result only)
- `--full-md` (save the older verbose markdown format)
- `--case <fixture-id>` (single case filter)
- `--no-title` (skip title follow-up generation)
- `--dry-run` (build prompt only)
- `--verbose` (print payload)

## Manual fixtures

Drop JSON fixtures into `prompt-lab/prompts/fixtures/` and run with:

```bash
node run.js --case arrival-v2
```
