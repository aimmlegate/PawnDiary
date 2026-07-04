# API v4 — Pawn-Context Providers — Design Brief

Status: **design draft (2026-07-04), one decision still open.** The public surface, sanitation,
failure isolation, and purity boundary are worked out, but the **player-toggle model is not yet
decided** — §7 holds an expanded option set (A–G) that needs a rethink before the v4 code PR. This
is the design-doc-before-code deliverable
that `design/MOD_COMPAT_PLAN.md` §4.2 / PR 4 requires before `RegisterPawnContextProvider` is
implemented. When v4 ships, its stable contract detail moves to `../INTEGRATIONS.md` and its status
flips to *shipped* in the MOD_COMPAT_PLAN ledger (§1); this file then becomes an implemented-plan
record like `BODY_PART_EVENTS_PLAN.md`.

Follow `skills/pawndiary-engineering/SKILL.md` and `AGENTS.md` (architecture barriers, DLC safety,
localization, docs-as-done). Every "verify" note below is a real checkpoint, not boilerplate.

---

## 1. Goal

Let a **personality mod** (RimPsyche, Psychology, …) contribute a compact line to *our* pawn summary
so the LLM sees personality as **who the pawn is**, not as an event. A blunt, curious, slow-to-trust
colonist should write blunt, curious, guarded diary pages — even though Pawn Diary has never heard of
the mod that models those traits.

Concretely, v4 adds one new public member:

```csharp
// PawnDiary.Integration.PawnDiaryApi
public static void RegisterPawnContextProvider(string id, Func<Pawn, string> provider);
public const int ApiVersion = 4;   // bumped 3 -> 4
```

A registered provider returns **one already-formed `key=value` line** for a pawn (e.g.
`personality=blunt, curious, slow to trust`). Pawn Diary sanitizes it and appends it to the pawn
summary next to the existing DLC-identity lines. That is the whole feature.

Non-goals for v4 (kept out on purpose):
- Reading another mod's personality to *drive* Pawn Diary behavior beyond the summary line.
- Structured personality data crossing the boundary — the contract is a **plain string**, nothing
  else, so no adapter type has to exist for us to compile.
- Any inbound-event path. The RimPsyche `InteractionHook → SubmitEvent` idea in MOD_COMPAT_PLAN §4.2
  is a **separate, v1-only** track and needs no new API; it is explicitly out of scope here.

## 2. Where this sits today (recap)

Shipped API surface (see `../INTEGRATIONS.md`): v1 `SubmitEvent` (inbound), v2 `GetRecentEntryTitles`
(read titles), v3 `GetWritingStyle` (publish base writing style). `PawnDiaryApi.ApiVersion == 3`.

The pawn summary those prompts are built from lives in `Source/Generation/DiaryContextBuilder.cs`,
`BuildPawnSummary(Pawn)`. Today it assembles a `; `-joined list of `key=value` parts:

```
sex=…; life_stage=…; xenotype=…; title=…; faith=…; mood=…; health=…; low_capacities=…; thoughts=…
```

The `xenotype=`/`title=`/`faith=` identity lines come from `Source/Generation/DlcContext.cs` — the
one guarded home for DLC-gated pawn reads, each returning `string.Empty` when its DLC is absent so a
no-DLC game simply omits the line. **v4 provider lines join right here, in the same shape** (MOD_COMPAT
§4.2 point 3).

## 3. The public surface (contract to design against)

### 3.1 Signature and semantics

```csharp
/// Registers a personality/context provider that contributes one line to every pawn summary.
/// - id: stable, mod-prefixed, e.g. "rimpsyche.personality". Re-registering the same id REPLACES.
/// - provider: Func<Pawn,string> returning ONE "key=value" line, or null/empty to contribute nothing
///   for that pawn. MUST be pure-ish and cheap: it runs once per prompt build, on the main thread.
/// Never throws into the caller; a null id or null provider is logged once and ignored.
public static void RegisterPawnContextProvider(string id, Func<Pawn, string> provider);
```

- **Additive-only, feature-detectable.** Consumers guard with `if (PawnDiaryApi.ApiVersion >= 4)`
  before calling, exactly like v3. On an older Pawn Diary the member does not exist, so a soft
  (reflection) integration simply skips registration.
- **Return contract:** the provider returns a **full `key=value` line** (like an `extraContext`
  line), not a bare value. This mirrors the inbound `extraContext` convention players and adapter
  authors already know, lets a provider choose its own key (`personality=`, `disposition=`, …), and
  lets multiple providers coexist without Pawn Diary inventing labels. Empty/`null` return = "no
  line for this pawn" (the common case for pawns the mod doesn't model).
- **One provider, one line.** If a mod wants two facets it either packs them into one line
  (`personality=blunt, curious; …` → the `;` is flattened to `,`, see §4) or registers two ids.
- **Idempotent by id.** Re-registering an id replaces the delegate; this makes hot-reload and
  double-`RegisterAll` harmless. No `Unregister` in v4 (additive-only lets us add one later if a real
  need appears; a provider that wants to go quiet returns empty).

### 3.2 Registration lifecycle

- **When:** once at startup, from the consumer's own init (a `Mod` ctor, a `StaticConstructorOn­Startup`,
  or a `GameComponent`). Registration is process-global, not per-save — providers describe *how to
  read a pawn*, not save data.
- **Thread:** main thread only, same rule as every other API entry point (it mutates a shared
  registry and the provider later runs where `.Translate()`/DefDatabase are touched). Off-thread
  registration is rejected-and-logged, never raced.
- **Safe before a game loads:** registering from a menu/loading screen is fine — it only records the
  delegate; providers are *invoked* only during prompt building, which only happens in-game.

## 4. Safety — sanitation (mirror `extraContext` exactly)

A provider string is untrusted adapter output flowing into a prompt, identical in risk to an
`extraContext` line. Reuse the **same** cleaning `ExternalEventSignal.JoinExtraContext` /
`CleanSummary` already apply (`Source/Ingestion/Sources/ExternalEventSignal.cs`):

1. `PromptTextSanitizer.OneLine(value)` — collapse to a single line (no newlines can break prompt
   framing or the diary card).
2. `.Replace(';', ',')` — `;` is the game-context field separator, so a `;` inside a provider value
   would forge extra fields. Same rule as `extraContext` and the arrival-context builder.
3. **Per-provider length cap** — cap each provider's contribution (proposed `MaxProviderLineChars`,
   start at the `extraContext`-family scale, e.g. 200; **decide the exact value in review**, it is a
   defensive cap, not tunable policy, so a C# `const` is correct per AGENTS.md rule 3).
4. **Skip empty** — a blank/whitespace result contributes no line (no dangling `key=`).
5. **Count cap** — total provider lines per summary capped (proposed `MaxProviderLines`, e.g. 8) so a
   pathological modlist can't balloon the prompt. Registered providers beyond the cap are skipped in
   registration order, logged once.

Because the shape is byte-for-byte the `extraContext` shape, the **cleaning logic should be extracted
into a pure helper** both call sites share (see §8 — this is the testable core).

## 5. Safety — failure isolation

A provider is third-party code that runs inside our prompt build; it must never crash the build.

- **Wrap every invocation.** A throwing provider is **caught, disabled for the rest of the session,
  and logged exactly once**, attributed to its `id` (mirrors the `Log.ErrorOnce`-per-source pattern
  in `PawnDiaryApi.SubmitEvent`). A disabled provider contributes nothing thereafter; the summary and
  the game keep going.
- **Null-tolerant.** A provider handed a pawn in a weird state returns empty, not an exception; but
  the wrap is the real guarantee.
- **No live objects escape.** The provider receives the live `Pawn` (it must, to read state) and
  returns a **string**. Nothing mutable crosses back. See §6.

## 6. Purity boundary (AGENTS.md barrier holds unchanged)

`BuildPawnSummary` is already **impure** — it reads `pawn.genes`, `pawn.royalty`, mood, health,
thoughts. Providers run *there*, in the impure snapshot phase:

```
[impure snapshot phase]  BuildPawnSummary(pawn):
    parts += DlcContext.Xenotype(pawn) ...      // existing impure reads
    parts += RunPawnContextProviders(pawn)      // NEW: providers read live pawn, return strings
        -> for each provider: string line = Sanitize(provider(pawn))   // cleaned to plain text
[pure pipeline]           summary string -> prompt planning/formatting  // only plain text crosses
```

The provider's live-`Pawn` read stays on the impure side; only its cleaned **string** enters the
plain prompt payload. No live `Pawn`/`Def`/settings object reaches pure code — the barrier is intact
(MOD_COMPAT §4.2 point 5). The pure, testable part is the sanitation + count-cap assembly, which
takes `IEnumerable<string>` raw lines and returns the cleaned joined block (§8).

### Placement in the summary
Insert the provider block **after `faith=` and before `mood=`** — i.e. grouped with identity, not
with transient state. Rationale: personality is *who the pawn is* (like xenotype/title/faith), and
keeping it adjacent to those lines reads coherently to the model. Order among multiple providers is
**registration order**, which is stable within a session.

## 7. Player control — OPEN (needs rethink)

MOD_COMPAT §4.2 point 4 says "per-provider toggle in settings (mirrors per-group toggles)." That
sentence predates the current architecture: **per-group enablement is now XML-only** —
`DiaryInteractionGroupDef.defaultEnabled` plus the `enableWhenPackageIdsLoaded` /
`disableWhenPackageIdsLoaded` package gates (`PawnDiarySettings.IsGroupEnabled`). The old
`groupEnabled` settings dictionary is legacy and ignored except for clean save-scribe of older files.

Providers are **C#-registered and have no Def**, so they cannot ride the XML `defaultEnabled` path
that groups now use. Three ways to give the player control:

| Option | What it is | Cost | Trade-off |
|---|---|---|---|
| **A. Master toggle** | One settings bool, e.g. `allowExternalPawnContextProviders`, read in the snapshot phase; off ⇒ `RunPawnContextProviders` returns nothing | smallest | Real control today, contract stays clean — but all-or-nothing; can't silence one noisy provider while keeping another |
| **B. Per-provider settings dict** | `Dictionary<string,bool>` keyed by provider id, surfaced in the Advanced tab as providers register (like the legacy `groupEnabled` shape) | medium | True per-provider control — but a settings-owned toggle for runtime-registered ids (no Def to hang `defaultEnabled` on), and the Advanced tab only shows a provider *after* it has registered |
| **C. Provider-descriptor Def** | Consumer ships a small XML Def (id + `defaultEnabled` + label) that its `RegisterPawnContextProvider` call references, mirroring groups exactly | highest | Most consistent with the XML-owned-policy rule and reuses the whole group toggle/label/localization machinery — but couples registration to a Def load and adds ceremony to every adapter |
| **D. No toggle — install *is* consent** | No Pawn Diary setting at all; the player enables/disables by adding or removing the provider mod | none | Zero surface, matches "a provider is just another installed mod" — but no in-game off switch, no way to keep the mod yet mute the diary line, and no per-provider control |
| **E. Fold into an existing setting** | Gate providers under a broader existing control (e.g. an integration / extra-prompt-context master switch, if one exists or is added) rather than a dedicated bool | small | Fewer settings-menu knobs and one coherent "external prompt context" switch — but couples providers to whatever else that switch governs, and the semantics get muddier |
| **F. Consumer-owned toggle (honored, not owned, by us)** | The registering mod exposes its own on/off in *its* settings and simply returns empty when off; Pawn Diary honors that with no setting of its own | small (ours) | Control lives with the mod that understands the data; no ceremony on our side — but consistency depends on every adapter implementing it, and there is no single Pawn Diary place to see/kill providers |
| **G. Hybrid: master + per-provider** | Ship A now and layer B on top (master switch plus a per-id dict that defaults on) | medium+ | Best UX (global kill switch *and* fine control) — but the most surface, and B's "no Def for runtime ids" awkwardness still applies |

**Status: not decided — needs a rethink.** The earlier lean toward a bare master toggle (A) is
withdrawn; the option set above is deliberately wider so the choice can be reconsidered from scratch.
Things to weigh when we revisit:

- **Granularity vs. surface.** Is all-or-nothing (A/D/E) actually enough, or is silencing one
  chatty/low-quality provider while keeping others (B/C/F/G) a real player need? That hinges on how
  many providers a typical modlist will register — likely 1–2 near-term (RimPsyche), which argues
  *against* per-provider machinery for now, but the answer changes if the ecosystem grows.
- **Where the toggle should *live*** — Pawn Diary settings (A/B/E/G), XML Def like groups (C), or the
  consumer mod (D/F). The XML-owned-policy rule (AGENTS.md rule 3) pulls toward C, but C is the most
  ceremony; D/F push control out of our codebase entirely.
- **Contract impact.** Whatever we pick must stay additive: none of these change the
  `RegisterPawnContextProvider(id, Func<Pawn,string>)` signature, so the toggle model can be chosen
  (or upgraded A→G) without breaking a shipped consumer — which means we *can* defer the richer
  options without painting ourselves into a corner.
- **Default state.** Regardless of model: providers should almost certainly default **on** when a
  provider mod is present (installing it is intent), with the toggle there to *mute*, not to opt in.

Pick a direction here before the v4 code PR; this is the one genuinely open design decision left.

## 8. What ships with v4 (implementation checklist — for the later code PR)

1. **Registry + runner.** `RegisterPawnContextProvider` stores `id → Func<Pawn,string>` (idempotent
   by id). A `RunPawnContextProviders(Pawn)` helper, called from `BuildPawnSummary`, invokes each
   provider inside a try/catch (disable-once on throw) and passes the raw lines to the pure cleaner.
2. **Pure cleaner extracted + shared.** Pull the `extraContext` cleaning (`OneLine` + `;`→`,` +
   length cap + empty-skip + count cap) into a pure helper (plain C#, no Verse) that **both**
   `ExternalEventSignal.JoinExtraContext` and the provider runner call. This is the unit under test
   and removes the current duplication.
3. **Player toggle — model TBD (§7).** Whatever model is chosen, wire it so it gates *invocation*
   inside `RunPawnContextProviders` (not registration), defaults to on when a provider is present,
   and — if it adds a settings row — carries a Keyed label/tooltip (localization-friendly per
   AGENTS.md rule 4). The exact shape (master bool / per-provider dict / Def / consumer-owned / …)
   is the open decision to close before this item is codeable.
4. **`ApiVersion = 4`** and the `SubmitEvent`-style never-throw wrapping on the new entry point;
   update the stale `PawnDiaryApi` class summary ("v2 surface" → current) while there.
5. **Example provider** in `integrations/PawnDiary.ExampleAdapter` — a trivial deterministic provider
   (e.g. `personality=` derived from a vanilla trait) so the contract has a buildable reference, the
   way the ExampleAdapter already demonstrates `SubmitEvent`.
6. **Docs as done.** `../INTEGRATIONS.md` gains a v4 section (surface, sanitation, threading, toggle);
   MOD_COMPAT §1 ledger flips v4 → shipped and moves the §5 PR-4 boxes; `DOCUMENTATION.md` notes the
   provider line in the pawn-summary description; dated `CHANGELOG.md` entry.
7. **Tests.** Extend a pure test project (`DiaryCapturePolicyTests` or a focused new harness) for the
   shared cleaner: `;`→`,`, one-line collapse, length cap, empty-skip, count cap, and the
   registration/replacement + disable-on-throw isolation (the isolation test needs the registry to be
   pure enough to exercise without a game — keep the delegate map and cap logic Verse-free).

## 9. Consumers — validate the contract against real surfaces

- **RimPsyche (primary v4 design partner).** Its modder surface is documented: `PsycheDataUtil.
  GetPsycheData(Pawn)` returns 15 personality facets (−50..50) + sexuality + memories. The provider
  we'd hand their maintainer turns the strongest facets into one line:

  ```csharp
  // in RimPsyche (or a standalone bridge), guarded by ApiVersion >= 4:
  PawnDiaryApi.RegisterPawnContextProvider("rimpsyche.personality", pawn =>
  {
      var data = PsycheDataUtil.GetPsycheData(pawn);
      if (data == null) return null;                     // pawn not modeled -> no line
      var top = data.StrongestFacets(3);                 // e.g. blunt / curious / guarded
      return top.Count == 0 ? null : "personality=" + string.Join(", ", top);
  });
  ```

  **Verify before shipping:** the exact `PsycheDataUtil` member names and facet accessors against the
  RimPsyche build players actually run (source-read names are strong priors, not proof — same lesson
  as the dead `HarbingerTree` matcher). Open a design issue / contact Maux36 with this snippet.
- **Psychology (unofficial).** No documented modder API found; personality lives in a per-pawn comp.
  A bridge would read that comp directly, which belongs on **their** side or in a standalone bridge —
  **not** in Pawn Diary core (core references no personality-mod types). Decide standalone-bridge vs.
  their-side provider during the code PR; we ship neither in core either way.

## 10. Open questions to close before coding

**Blocking (needs a real decision):**
1. **Toggle model — NOT decided, needs a rethink (§7).** Choose among the expanded A–G set (master
   bool / per-provider dict / provider Def / no-toggle / fold-in / consumer-owned / hybrid). The
   granularity-vs-surface and where-the-toggle-lives trade-offs in §7 are the crux. Whatever is
   picked stays additive, so it does not block the *rest* of the design — but it does block the
   toggle slice of the code PR.

**Small (pick in the code PR — none block the design):**
2. **`MaxProviderLineChars` / `MaxProviderLines`** exact values (§4) — pick in review; defensive caps,
   proposed 200 / 8.
3. **Summary placement** — confirm "after `faith=`, before `mood=`" (§6) vs. end-of-summary
   (recommendation stands: with the identity block).
4. **`Unregister` member** — omit in v4 (return-empty covers going quiet) unless a partner needs it;
   additive, can be added any time.
5. **RimPsyche member names** — confirm against the shipped build before writing the outreach snippet.

## 11. Verification checklist (for the eventual code PR, per SKILL/MOD_COMPAT §6)

- Build: `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`; rebuild + stage the DLL.
- Pure tests for the shared cleaner + registry isolation run and reported.
- No-DLC / no-provider game: zero behavior change, zero errors (providers absent ⇒ summary
  unchanged; a throwing provider disables-once and the page still writes).
- `../INTEGRATIONS.md`, `DOCUMENTATION.md`, `CHANGELOG.md`, and this file's status updated together.
- No live `Pawn`/`Def`/settings object leaks into pure code; barrier audit clean.
