# prompt-lab

Tinker with Pawn Diary's prompts outside the game. Each fixture is an editable prompt
that mirrors what the mod actually sends; the runner fires it at your model and prints the
result. Model output is treated as ready diary text and is not parsed into initiator or
recipient sections.

No dependencies — just Node.

## Use

```
cd prompt-lab
node run.js                              # list fixtures
node run.js prompts/insult-dual.txt      # run one
node run.js insult-dual.txt --show       # also echo the prompt that was sent
node run.js --all                        # run every fixture
```

Each run saves a formatted Markdown file under `results/<fixture>/`. The diary result is
shown first for easy reading; the system prompt, user prompt, raw response, and raw JSON
metadata are included below in fenced code blocks.

Make sure your model server is up (LM Studio / llama.cpp / etc.). Endpoint + model are in
`config.json`, or override per run with env vars:

```
ENDPOINT=http://localhost:1234/v1 MODEL=rocinante-x-12b-v1-i1 node run.js --all
```

## Editing prompts

A fixture is a plain text file:

```
# mode: paired          # paired | single | solo  (saved as run metadata only)
# max_tokens: 320       # optional per-fixture override
# temperature: 0.8      # optional per-fixture override
===SYSTEM===            # optional — omit to use prompts/_system.txt
<system prompt>
===USER===
<the user prompt the model receives>
```

Pairwise fixtures use two request bodies:

```
===INITIATOR===
<initiator prompt>
===RECIPIENT===
<recipient prompt with {{initiator_result}}>
```

The runner sends the initiator prompt first, replaces `{{initiator_result}}` with that
plain text response, sends the recipient prompt, and saves both POVs in one Markdown file.

- `prompts/_system.txt` is the shared system prompt (same as the mod's default). Edit it
  once to test a system-prompt change across every fixture, or add a `===SYSTEM===` block to
  a fixture to override it just there.
- The `===USER===` body is the exact text the mod builds. Tweak wording, add/remove context
  lines, reorder — then re-run and compare. Underscore-prefixed files (`_system.txt`) are not
  listed as fixtures.

## Keeping it honest

These fixtures are hand-authored to match the mod's current compact format (see
`../DOCUMENTATION.md` §5/§4). Current pairwise fixtures should include both request bodies:
initiator first, then recipient with an `initiator diary (hidden context)` line containing
`{{initiator_result}}`. Mirror that when you add new ones so what you test matches what
ships.
