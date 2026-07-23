# Pawn Diary External API

This root file is a compatibility pointer. The readable adapter guide is now in the GitHub wiki:

- [External API Quickstart](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/External%20API%20Quickstart.md) — setup, minimal C# call, XML claim, write paths, request schema, snapshots, and safety rules.
- [Adapter Contract](repowiki/en/content/Integration%20Framework/Public%20API%20Reference/Adapter%20Contract.md) — versioning, lifecycle, budgets, ownership, and no-DLC compatibility.
- [Buildable example adapter](integrations/PawnDiary.ExampleAdapter/) — use `Source/PawnDiaryExampleApi.cs` as the starting point.

The supported public namespace is `PawnDiary.Integration`; current `PawnDiaryApi.ApiVersion` is 8.
Implementation guidance lives in the linked Adapter Contract and buildable example.
