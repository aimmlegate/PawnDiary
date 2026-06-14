# prompt-lab

Tinker with Pawn Diary's prompts outside the game. Each fixture is an editable prompt
that mirrors what the mod actually sends; the runner fires it at your model and prints the
result. Legacy dual fixtures can still be parsed, but current paired POV prompts are
single-entry requests.

No dependencies — just Node.

## Use

```
cd prompt-lab
node run.js                              # list fixtures
node run.js prompts/insult-dual.txt      # run one
node run.js insult-dual.txt --show       # also echo the prompt that was sent
node run.js --all                        # run every fixture
```

Make sure your model server is up (LM Studio / llama.cpp / etc.). Endpoint + model are in
`config.json`, or override per run with env vars:

```
ENDPOINT=http://localhost:1234/v1 MODEL=rocinante-x-12b-v1-i1 node run.js --all
```

## Editing prompts

A fixture is a plain text file:

```
# mode: single          # single | solo | dual  (only changes how output is printed/parsed)
# max_tokens: 320       # optional per-fixture override
# temperature: 0.8      # optional per-fixture override
===SYSTEM===            # optional — omit to use prompts/_system.txt
<system prompt>
===USER===
<the user prompt the model receives>
```

- `prompts/_system.txt` is the shared system prompt (same as the mod's default). Edit it
  once to test a system-prompt change across every fixture, or add a `===SYSTEM===` block to
  a fixture to override it just there.
- The `===USER===` body is the exact text the mod builds. Tweak wording, add/remove context
  lines, reorder — then re-run and compare. Underscore-prefixed files (`_system.txt`) are not
  listed as fixtures.

## Keeping it honest

These fixtures are hand-authored to match the mod's current compact format (see
`../DOCUMENTATION.md` §5/§4). Current pairwise fixtures should represent one request at a
time: initiator first, then recipient with an `initiator diary (hidden context)` line.
Mirror that when you add new ones so what you test matches what ships.
