# Pawn Diary External API

This root file is the stable entry point for the public in-process adapter contract:

- [Buildable example adapter](integrations/PawnDiary.ExampleAdapter/) — use `Source/PawnDiaryExampleApi.cs` as the starting point.
- [Shipped compatibility reference](repowiki/en/reference/Compatibility.md) — exact first-party adapters, XML policies, setup, and expected evidence.
- [Prompt and outbound-data guide](repowiki/en/Prompt-Building.md#external-integrations) — what an admitted external event can send through a configured LLM lane.

The supported public namespace is `PawnDiary.Integration`; current `PawnDiaryApi.ApiVersion` is 8.
The public request, result, snapshot, and listener types are defined under
[`Source/Integration`](Source/Integration/). Compile only against the public
`PawnDiary.Integration` namespace, treat status/result objects as snapshots, respect API readiness
and source budgets, and keep optional-mod behavior inert when its dependency is absent.
