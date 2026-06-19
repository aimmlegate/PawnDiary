# Changelog

Newest first. `DOCUMENTATION.md` describes the current design; this file records how it got there.

## 2026-06-19

- **Generated speech Social-log injection:** added an opt-in setting that creates one fresh native
  social-log interaction row when an initiator diary result contains a valid parsed direct-speech
  block, sharing the same parser used by the Diary tab and skipping the synthetic row in Pawn
  Diary's own PlayLog listener to avoid feedback loops.
- **Initiator speech prompt nudge:** strengthened initiator and single-POV direct-speech prompt
  instructions so eligible entries ask for exactly one short standalone `[[speech]]` block when the
  supplied notes contain or strongly imply words spoken by the POV pawn.
- **Speech-injection hardening:** the saved `LogID`->text map now drops entries whose PlayLog row
  RimWorld has already pruned (on save), so it can't grow without bound; the display patch skips its
  per-row lookup entirely in games that never created a generated row; and the reflection-fallback
  constructor now stamps a fresh unique `LogID` instead of risking a colliding default.
- **Trailing speech no longer trimmed away:** response cleanup treated a `[[speech]]...[[/speech]]`
  block on the final line as an incomplete sentence (it ends in `]]`, not `.`/`!`/`?`) and deleted
  it, so entries that ended on a spoken line lost their speech in both the diary tab and the
  Social-log injection. Cleanup now treats a closed speech block as a complete boundary while still
  trimming genuinely truncated tails.

## 2026-06-18

- **Publish metadata cleanup:** `scripts/publish.ps1` now derives its default payload folder,
  upload instructions, tag message, and published metadata from `About.xml`; the copied payload
  strips a trailing `(developement)` / `(development)` dev marker from both mod name and packageId.
- **Small-model persona retune:** rewrote every `DiaryPersonaDef` rule to lead with one dominant,
  imitable voice signature (shorter, positive phrasing, mechanically distinct) and appended a short
  in-voice example (`For example: "..."`) to each, so small local models (Gemma, Mistral Nemo, small
  Qwen) keep the personas distinct instead of collapsing into one generic atmospheric voice. Synced
  the hardcoded `DiaryPersonas` fallback rule; the prompt-lab harness reads the same XML, so no lab
  code change was needed.
- **Prompt lab indoor coverage:** the default `--from-defs` set now includes indoor pair/solo cases
  (no weather token in `setting:`) so a standard run also exercises the outdoors-gated setting path
  and shows whether entry openings still diverge by persona without the weather anchor.
- **Weighted weather mentions:** outdoor entries now note the weather only on a severity-weighted
  roll instead of always — clear is never mentioned, mild weather rarely, dramatic weather almost
  always — to stop weather from dominating diary openings. The per-weather chances are XML-tunable in
  `DiaryTuningDef.xml` (`weatherMentionChances`), with favorability-keyed fallbacks for unlisted
  DLC/modded weather.
- **Beta release prep:** new `About/Preview.png` banner; refreshed `About.xml` description and
  `README.md` to the current feature set and marked the mod as a beta/WIP hidden Steam Workshop
  release (name shows "Pawn Diary (Beta)"; `packageId` unchanged).
- **Voice-spanning few-shot:** the first-person `systemPrompt` examples now span three registers
  (clipped, warm, lyrical) instead of all lyrical, with a "match the colonist's voice, not these
  examples" framing, so small models stop defaulting every persona to elegiac prose. Each example
  still teaches its point (concrete anchor vs flat summary, health pressing without a medical scene,
  the speech mechanic).
- **All event groups enabled by default:** every shipped `DiaryInteractionGroupDef` now defaults on,
  including former low-stakes, catch-all, insult-spree, work-achievement, and quiet-tale groups.
- **Combat tale batching:** non-death combat tales now batch per pawn into delayed solo entries;
  death descriptions stay immediate neutral pages, and solo combat batches keep the richer combat
  prompt shape.
- **Settings prompt studio simplification:** Prompt Studio now edits only the four shared system
  prompts; event group matching/instructions/defaults are XML-only, and old saved group toggles are
  ignored.
- **Diary tab view cache:** entry views are cached behind a render token, and social-log click lookup
  now avoids repeated diary-bound recalculation.
- **Pipeline cleanup:** removed the dead pre-pipeline renderer, routed prompt-enchantment gating
  through `DiaryPromptPlanner.TemplateKeyFor`, removed unused response-rule fields, cached text
  decoration fallbacks, and added parser tests for no-closing-tag reasoning blocks.
- **Temperature slider:** exposed the saved LLM sampling temperature in Generation settings.
- **Prompt lab exhaustive runs:** added `--all-variants`, `--passes`, compact markdown saving,
  prompt-enchantment matrices, all-groups/all-personas options, and fixed assembler rendering and
  absolute result folders.
- **Local verification hooks:** added tracked pre-commit/pre-push hooks and documented hook setup,
  committed-DLL freshness checks, and `PAWNDIARY_SKIP_VERIFY_HOOKS=1`.
- **Agent architecture guardrails:** updated repo workflow rules for layer boundaries, typed DTO
  contracts, XML-owned policy, and focused pure tests.
- **XML text decoration rules:** added XML-owned display text decorations, pure selection/transform
  tests, saved per-POV hediff/trait facts, and deterministic direct-speech-only stagger/Zalgo rules.
- **XML diary UI style:** moved Diary tab visual defaults, timings, speech markers, accent palettes,
  and cue-color mappings into `DiaryUiStyleDef.xml`.
- **Typed generation pipeline contracts:** added DTO contracts, impure adapters, pure prompt planner
  and response postprocessor, queued `DiaryPromptPlan`s, and `DiaryPipelineTests`.
- **Pure LLM response parser:** moved provider response extraction, provider error surfacing,
  reasoning/thinking stripping, and local cleanup into pure parser code with tests.
- **Atmosphere-led prompts:** moved persona voice into system prompts, consolidated guardrails,
  added compact examples, and reduced repetitive final instructions.
- **Reasoning leak fix:** stripped structured and transcript-style reasoning/thinking output before
  debug or save persistence.
- **XML prompt and signal architecture:** moved prompt templates/system prompts/signal policies into
  XML Defs while keeping code fallbacks.
- **Direct speech blocks:** added `[[speech]]...[[/speech]]` prompt/UI handling for allowed POVs.
- **API connection test:** added per-lane generation-path connection tests with safe debug logging.
- **LLM client review fixes:** hardened lane selection, failover metadata, debug logging, and
  request/session handling.

## 2026-06-17

- **Reasoning block scrub:** stripped common reasoning/thinking wrappers and final-answer prefixes.
- **Compatible API modes:** added OpenAI-compatible Chat Completions, OpenAI Responses, and native
  Ollama Chat lane modes.
- **Diary UI debug raw response:** exposed raw/final response diagnostics in dev UI.
- **Role-slot extraction TODO:** documented the future `DiaryEvent` role-slot migration route.
- **Diary tab partial split:** split the large tab class into focused partial files.
- **Review hardening and helper split:** extracted helpers and tightened edge handling after review.
- **Active map context in settings:** surfaced current active map context in settings/debug flow.
- **Prompt guardrails:** added low-creative-reach, always-on setting context, and event-subject
  guardrails to reduce invented scenes or wrong subjects.
- **Prompt lab sync/examples:** synced prompt-lab policy with runtime prompts and added concrete
  prompt examples.
- **Pregnancy diary events:** added pregnancy/labor hediff entries and pregnancy-family thoughts.
- **Direct-speech POV rules:** made quoted speech POV-specific and recipient-safe.
- **Hediff mod support:** added XML Hediff groups, immediate/day-reflection modes, progression
  policy, and fixes for modded health signals.
- **Inspiration entries:** added inspiration capture and prompt context.
- **Persona catalog/settings:** added verse, fragmented, low-consciousness-related, and selector UI
  work; later moved low Consciousness to prompt enchantments instead of persona mutation.
- **Atmosphere formatting and color cues:** added rare display effects, stable semantic color cues,
  and linked UI treatment.
- **Route cleanup/discovery removal:** removed discovery-event routing and simplified event paths.
- **Important health cues:** compacted health prompt context, added hediff weighting, consciousness
  capacity enchantments, starter enchantment catalog, severity tiers, and a generation skip below
  11% Consciousness.
- **Insult-spree batching:** batched repeated insult/social conflict rows.
- **Context policy streamlining:** reduced numeric prompt clutter and tuned relationship/health
  context.
- **Documentation compaction:** compacted docs/changelog once before the later full compact pass.
- **Caches, personas, UI paging, discovery quarantine:** added caching, persona improvements, year
  paging, and quarantine of discovery-style event behavior.

## 2026-06-16

- **Prompts, generation weights, UI readability, robustness:** improved prompt policy, added social
  and work generation multipliers, tightened UI layout, and hardened generation/status handling.
- **Work entries, prompt lab, titles, UI polish, cleanup:** added sampled work pages, prompt-lab
  support, title generation, card/UI refinements, and cleanup.
- **Event architecture, batching, reflections, reliability:** expanded event capture, batching,
  day reflections, pending recovery, LLM retries/failover, and save/load behavior.

## 2026-06-15

- **Arrivals, deaths, personas, DLC safety, event coverage, routing:** added arrival/death
  chronicles, persona selection, DLC-safe context patterns, broader event coverage, and routing.
- **Diary UI, context, localization, agent docs:** added the Diary tab experience, context builders,
  localization coverage, and repo agent guidance.

## 2026-06-14

- **Background generation, eligibility, personas, XML refactor:** moved generation into background
  request flow, tightened pawn eligibility, added personas, and began XML policy extraction.

## 2026-06-13

- **Initial diary system:** introduced the base diary event model, generation path, UI surface, and
  RimWorld integration.
