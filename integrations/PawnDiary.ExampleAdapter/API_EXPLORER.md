# Pawn Diary API Explorer

Open in-game with Dev mode -> Debug Actions -> Pawn Diary Example Adapter -> Open API explorer.
The launcher menu closes after opening the explorer. Use the thin top strip to drag the window; it
is a resizeable debug overlay, and clicking outside it keeps it open while normal game UI/camera
interactions continue.

Use the header pickers to choose the subject pawn and optional partner. Pick an API method on the
left; each method row shows its plain-language purpose under the API signature. Fill the request
form, then press Invoke. The right pane keeps the call history; selecting a row shows the full
returned snapshot, and Copy copies the selected detail.

The header badge shows the core API version plus readiness. `ready` means the game component is live
and the player has enabled the external API master switch in Pawn Diary's main mod settings. When
that switch is off, the explorer still opens so you can inspect Readiness calls, but it skips other
invocations instead of hammering the disabled API.

Every field starts with a concrete sample value so submit and preview calls work with minimal
typing. The three write families intentionally start on different event keys:
`exampleadapter_quiet_moment`, `exampleadapter_prompt_idea`, and
`exampleadapter_direct_note`. That makes each External group shipped in
`1.6/Defs/DiaryExternalGroups_Example.xml` reachable from its matching form. The write forms default
`forceRecord` on so repeated smoke-test submissions record instead of disappearing into dedup or
budget guardrails; turn it off when you want to test normal pipeline drops. Query and overload
toggles stay off by default; turn them on when you want to test the prefilled filters.

Hover a method title or field label to see a short popover explaining the public API meaning. Reset
restores the shared form defaults when a test request gets messy.

For adapter development, start with `SubmitEvent(req)`, then test the handle-returning submit and
read methods. Use the query toggle under Read Pawn to verify filtered context reads before wiring a
real integration. The Psychotype category covers the snapshot, built-in rule lookup, editable
base/custom setters, source-owned override pair, and external Regenerate callback. The LLM
Completion category covers request, poll, and cancellation; requesting invokes a real configured
provider and can spend the player's tokens, so the form calls that out explicitly.

The copyable adapter code is `Source/PawnDiaryExampleApi.cs`. It is the only example-adapter source
file that calls `PawnDiaryApi` directly, and each wrapper documents what it does, which args are
required, and what safe return value to expect.
