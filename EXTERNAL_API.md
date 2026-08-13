# Pawn Diary External API

This root file is the stable entry point for the public in-process adapter contract:

- [Buildable example adapter](integrations/PawnDiary.ExampleAdapter/) — use `Source/PawnDiaryExampleApi.cs` as the starting point.
- [Shipped compatibility reference](repowiki/en/reference/Compatibility.md) — exact first-party adapters, XML policies, setup, and expected evidence.
- [Prompt and outbound-data guide](repowiki/en/Prompt-Building.md#external-integrations) — what an admitted external event can send through a configured LLM lane.

The supported public namespace is `PawnDiary.Integration`; current `PawnDiaryApi.ApiVersion` is 9.
The public request, result, snapshot, and listener types are defined under
[`Source/Integration`](Source/Integration/). Compile only against the public
`PawnDiary.Integration` namespace, treat status/result objects as snapshots, respect API readiness
and source budgets, and keep optional-mod behavior inert when its dependency is absent.

API v9 additively exposes the selected event-frequency preset, each event row's tier and
preset/effective multiplier, an independent frequency-override flag, and validated preset/set/reset
writes. The existing Boolean event-filter methods and `DiaryEventFilterSnapshot.hasOverride` keep
their v8 enable-state meaning. New calls fail safely for unknown tokens, unavailable settings,
disabled integration access, or an off-main-thread caller.

Adapters that also support older Pawn Diary DLLs must probe `ApiVersion` through reflection. It is a
public `const` for binary metadata compatibility, so a direct C# read is substituted at adapter build
time and cannot report the version of the DLL RimWorld actually loaded. Keep v9-only DTOs and method
calls behind the same reflection boundary; the example adapter's `LoadedApiVersionProbe.cs` and
`FrequencyApiV9Shim.cs` are the reference implementation.

For `SubmitEventWithHandle`, `SubmitPromptEntry`, and `SubmitDirectEntry`, `recorded=true` means the
canonical page entered persistence. In the rare case that later post-commit work fails before the
signal can expose that page, `primary` and `partner` remain null because Pawn Diary will not fabricate
an event id; the accepted request still consumes its budget reservation.
