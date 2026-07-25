# Prompt building and outbound data

Pawn Diary builds a bounded system/user request from verified game facts. It does not hand an endpoint live game objects, and it does not let the model decide what event occurred.

## From an event to a request

1. **Choose a template shape.** Current XML defines 15: pair default, important, combat, and batched; solo default, important, internal-state, and batched; day, quadrum, arc, and belief reflections; neutral death and arrival descriptions; and the separate title template.

2. **Resolve event policy.** The interaction group and event-prompt policy provide classification, guidance, enhancement, and any model preference. A matching saved player override takes precedence where supported.

3. **Freeze one POV and event payload.** Main-thread capture copies the selected pawn role, other participants, and verified event facts into plain values. Sequential paired pages can wait for the initiator's completed text before building the recipient's request.

4. **Collect possible context.** Depending on the event and template, candidates include:

   - what happened and who was involved;
   - the pawn summary and current setting;
   - relationship and identity snapshots;
   - mood and top thought where allowed;
   - weapon and combat facts;
   - narrative continuity, recent memory, belief, and culture;
   - active event windows, observed conditions, and live-context prompt cues; and
   - the pawn's previous opener/ending and, for a sequential pair, the initiator's completed entry.

5. **Apply context detail.** Full, Balanced, or Compact decides how much optional material fits inside XML-tuned character budgets. Required event and identity facts remain eligible; lower-priority optional fields compete for the remaining space.

6. **Build the system prompt.** The selected template contributes its base instruction. When allowed, Pawn Diary adds the pawn's psychotype, writing style, and humor voice direction, followed by the active output-language instruction.

7. **Build the user prompt.** Enabled template fields render in XML order as `label: value` lines. Blank values and the sentinel values `none`, `n/a`, and `unknown` are omitted. The final writing instruction is appended last.

8. **Choose and run a lane.** Forced-model policy, enabled lane order, lane-specific context override, concurrency, retries, cooldown, and ordered failover determine the request attempt.

9. **Parse and clean the response.** Pawn Diary supports OpenAI-compatible Chat Completions and Responses shapes. It removes provider reasoning, code fences, instruction echoes, unsafe markup, malformed speech markers, stray placeholders, and trailing text beyond the local limit.

10. **Apply the result on the main thread.** A successful cleaned page enters storage and the Diary UI. If titles are enabled, Pawn Diary issues a separate bounded title request only after the main page succeeds.

The [prompt reference](reference/Prompt-Reference.md) lists every current template field in order, all event-prompt selectors, and all live-context enchantments.

## Sanitized example

This illustrates the real separation and line shape; it is not a byte-for-byte dump of one live template.

```text
System:
Write a first-person diary entry grounded only in supplied facts.

Write with restrained, observant prose.
Write the diary entry in English.

User:
event: social fight
pov: Latch
other pawn: Mira
what happened: Mira insulted Latch and the argument became a fight.
tone: angry and humiliated
relationship: colony acquaintance with worsening trust

Write one concise first-person diary entry. Do not invent outcomes or private knowledge.
```

There is no general-purpose template programming language. Pawn Diary does not promise wiki-defined loops, arbitrary conditions, nested object traversal, or custom template functions. Template shapes and ordered fields are typed XML policy consumed by the current planner and renderer.

## What can leave the game

### LLM prompt requests

When a real generation or title request reaches an active lane, Pawn Diary sends an HTTP request to that lane's configured URL. The request contains:

- the configured model name;
- the assembled system and user prompt text;
- temperature and the applicable output-token cap; and
- explicit reasoning effort when the selected API mode and setting require it.

Chat Completions mode sends system/user messages to `/chat/completions`. Responses mode sends user input, optional system instructions, and its output cap to `/responses`. Authentication follows the lane setting: no auth, Bearer header, a named custom header, or a `key` query parameter. The configured API key therefore leaves the game by the selected authentication mechanism, not as a diary prompt field.

The prompt can contain every selected context item described above. It is sent only after capture and admission produce a request; prompt-test mode records the planned request locally without transmitting it.

### Optional error reporting

Automatic error reporting transmits only when the player enables it and Pawn Diary records a reportable Pawn Diary-family error. The current report contains:

- schema, Pawn Diary, and RimWorld versions;
- a coarse operating-system string;
- install source and a random per-install ID;
- an error fingerprint and UTC timestamp;
- active DLC names; and
- a scrubbed error message and stack.

The report contract has no prompt, API key, configured endpoint URL, user/machine name, file path, save, colony, or pawn-name field. This describes the current source contract; it is not a broader privacy or security guarantee about the configured LLM provider or other mods.

### External integrations

The public Pawn Diary API is an in-process mod contract. Other mods submit bounded event/context data **into** Pawn Diary; that call alone is not an Internet transmission. If the submitted event needs generated prose, it follows the same configured LLM lane path as a core event.

Shipped bridges can also exchange data in-process with their target mod. For example, the RimTalk bridge can provide diary context or persona direction to RimTalk. What the receiving mod later does is governed by that mod and its configuration.

Several bridge modes deliberately request an LLM transformation or semantic assessment. When the player selects such a mode, the bridge sends its bounded source summary plus transform/assessment instruction through a selected Pawn Diary lane. Conversation and gathering adapters that submit normal External events send the resulting assembled diary prompt through the normal lane only when that event is admitted.

See [Compatibility](reference/Compatibility.md) for each shipped adapter's exact mode and transmitted summary, and [EXTERNAL_API.md](../../EXTERNAL_API.md) for the public integration contract.

## What comes back

The transport extracts visible answer text from the configured response shape. Local parsing removes reasoning and wrapper artifacts; the postprocessor enforces diary-safe formatting and length. A malformed, empty, timed-out, or exhausted response does not become a saved page. Successful text returns to the main thread before any game state or UI collection is changed.

## Source of truth

- Planning and context selection: [DiaryPromptPlanner](../../Source/Pipeline/DiaryPromptPlanner.cs) and [PromptContextDetail](../../Source/Pipeline/PromptContextDetail.cs).
- Rendering: [PromptAssembler](../../Source/Generation/PromptAssembler.cs).
- Request shape and transport: [LlmRequestJsonBuilder](../../Source/Pipeline/LlmRequestJsonBuilder.cs) and [LlmClient](../../Source/Generation/LlmClient.cs).
- Cleanup: [LlmResponseParser](../../Source/Generation/LlmResponseParser.cs) and [DiaryResponsePostprocessor](../../Source/Pipeline/DiaryResponsePostprocessor.cs).
- Error reporting: [DiaryErrorReporter](../../Source/Diagnostics/DiaryErrorReporter.cs) and [ErrorReportPayload](../../Source/Diagnostics/Pure/ErrorReportPayload.cs).
