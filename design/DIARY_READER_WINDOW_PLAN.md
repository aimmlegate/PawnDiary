# Diary Reader Window — three-pane reading mode + default/alternative toggle

**Status: APPROVED 2026-07-22 (owner-reviewed plan; implement from this doc, don't re-research).**
**Amended 2026-07-22 (owner): fixed window size — no resize; must look right on both HD
(1366×768 / 1280×720) and Full HD (1920×1080). Supersedes the earlier resizable+remembered
decision; see Step 4.**
Wave-C1 item C3, extended with the owner's three-pane alternative-mode idea.
Visual reference for the right pane: `Mockups/Wave-C1/14-year-tags-favorites-sidebar.png`.

## Context

The diary currently renders only inside the inspect-pane ITab, whose size is clamped by the
inspect chrome (journal column ≈ 696px with the XML defaults `tabWidth=992`,
`filterPanelWidth=260`). Wave-C1 item C3 calls for an optional reading-mode `Window` reusing
the card renderer at book width (~850px). The owner extends this with an **alternative UI
mode** (settings toggle): the diary for **all pawns** moves to one standalone three-pane
window — left: pawn list with in-game portraits (dead pawns behind a toggle); middle: the
diary card renderer; right: the filter panel. Same data, bigger canvas.

**Owner decisions (2026-07-22):**
- In alternative mode the Diary **gizmo AND the inspect tab disappear entirely**; all
  programmatic open paths (social-log links, linked-entry jumps) redirect to the window.
- Entry point is a **bottom main-bar button** (`MainButtonDef`), visible only in alternative
  mode; clicking toggles the reader window.
- The window is **non-pausing, draggable, and fixed-size (no resize)** — amended 2026-07-22.
  Size is computed from the screen at open and must look right on HD (1366×768 / 1280×720)
  and Full HD (1920×1080) alike.
- Pawn list shows living colonists by default with **dead/departed behind a toggle**.
- The filters pane reuses the shipped FilterPanel behavior (instant apply — no Apply/Clear
  buttons from the mockup).

## Verified RimWorld 1.6 API facts (ilspycmd vs real Assembly-CSharp, 2026-07-22)

- `MainButtonDef`: fields `workerClass` (default `MainButtonWorker_ToggleTab`),
  `tabWindowClass` (optional — may be null), `buttonVisible`, `order`, `defaultHotKey`,
  `validWithoutMap`, `minimized`, `iconPath`.
- `MainButtonWorker`: `virtual bool Visible` (returns `def.buttonVisible` + classic-ideo
  check — override to gate on the setting), `abstract void Activate()`,
  `virtual void InterfaceTryActivate()` (tutor checks then `Activate()`), `virtual bool
  Disabled` (disables when `Find.CurrentMap == null` unless `def.validWithoutMap`). A custom
  worker may open a regular `Window` on the WindowStack instead of a `MainTabWindow`.
- `PortraitsCache.Get(Pawn pawn, Vector2 size, Rot4 rotation, Vector3 cameraOffset = default,
  float cameraZoom = 1f, ...)` → `RenderTexture`; also has `PawnHealthState?
  healthStateOverride` if dead-pose portraits look wrong.
- Vanilla button orders (`Data/Core/Defs/Misc/MainButtonDefs/MainButtons.xml`): History=80,
  Factions=90 — both `minimized` icon-buttons. The Diary button slots at **order 85,
  minimized, icon `UI/Commands/PawnDiaryOpen`** (existing mod texture; gizmo fallback pattern
  is `ContentFinder<Texture2D>.Get(path, false) ?? TexButton.IconBook`).

## Architecture decision

**Promote the entire journal UI (renderer + virtualized layout + filters + favorites +
highlights + year paging) into one host-neutral instance class `DiaryJournalView` by
mechanically renaming the `ITab_Pawn_Diary` partial family.** No fine-grained list-view
extraction, no provider interfaces, no duplicated simpler layout.

Rationale: the filter panel, year paging, layout dirty-checks, and favorites mirror are deeply
interwoven instance state (`ITab_Pawn_Diary.FilterPanel.cs:37-65`, `YearPaging.cs:152-162`;
dirty-check subtleties documented at `ITab_Pawn_Diary.cs:856-931` record two past hitching
bugs); a whole-class rename keeps method bodies byte-identical so behavior preservation is
compiler-verified. A year can hold thousands of cards (dev mock seeds 2000/year) — the
frame-sliced virtualization must be reused, not duplicated. Both hosts (tab, window) own a
private `DiaryJournalView` instance, which gives the window independent scroll/filter/
expansion state and per-pawn reset for free (`ResetFilterStateOnPawnChange`,
`EnsureSelectedYear`, `EnsureFavoritesSynced`).

## Steps

Each step builds cleanly and is verifiable on its own; land them in order.

### Step 1 — Mechanical extraction: `DiaryJournalView` (behavior-preserving)

Rename the partial family in `Source\UI\` (csproj globs `**\*.cs`, no project edit):

| From | To |
|---|---|
| `ITab_Pawn_Diary.cs` (journal portion) | `DiaryJournalView.cs` |
| `ITab_Pawn_Diary.EntryCards.cs` | `DiaryJournalView.EntryCards.cs` |
| `ITab_Pawn_Diary.Expansion.cs` | `DiaryJournalView.Expansion.cs` |
| `ITab_Pawn_Diary.FilterPanel.cs` | `DiaryJournalView.FilterPanel.cs` |
| `ITab_Pawn_Diary.Controls.cs` | `DiaryJournalView.Controls.cs` |
| `ITab_Pawn_Diary.YearPaging.cs` | `DiaryJournalView.YearPaging.cs` |
| `ITab_Pawn_Diary.NameHighlights.cs` | `DiaryJournalView.NameHighlights.cs` |
| `ITab_Pawn_Diary.RoleplayText.cs` | `DiaryJournalView.RoleplayText.cs` |
| `ITab_Pawn_Diary.DevPreview.cs` | `DiaryJournalView.DevPreview.cs` |
| `DiaryTabVisibleEntriesCache.cs` | `DiaryJournalView.VisibleEntriesCache.cs` |

Class decl: `internal sealed partial class DiaryJournalView`. Only three non-rename edits:

1. `FillTab` body (`ITab_Pawn_Diary.cs:446-793`) → `internal void Draw(Rect outerRect, Pawn
   pawn, DiaryGameComponent component)`; the two `new Rect(0,0,size.x,size.y)` uses (seasonal
   wash `:461`, content `:464`) become `outerRect` / `outerRect.ContractedBy(12f)`;
   `PawnToShow()`/component resolution move to the caller.
2. `DiaryEntryCardRenderRequest.Tab` (`EntryCards.cs:67`, used only for favorites at
   `:119-127`) → `Owner` of type `DiaryJournalView`.
3. Pending-scroll statics (`pendingScrollPawnId`/`pendingScrollEventId`/`RequestScrollToEntry`
   `ITab_Pawn_Diary.cs:353`/`ClearPendingScrollRequest` `:362`) move to `DiaryJournalView` as
   `internal static`; `ITab_Pawn_Diary` keeps thin forwarders so patch call sites compile
   (repointed in Step 5).

Slimmed `ITab_Pawn_Diary.cs` keeps: ctor, `UpdateSize`/`ApplyResponsiveTabSize`/
`ResponsiveTabWidth`/`ResponsiveTabHeight` + constants, `IsVisible`, `Hidden`, `CloseTab`,
`commandOpenRequested`, `OpenDiaryTab` (`:372`), `CanShowDiaryFor` (`:416`), `PawnToShow`
(`:425`), plus `private readonly DiaryJournalView journalView` and a 4-line `FillTab` that
delegates. `FilterPanelSettingEnabled`/`UiStyle` become `internal static` on the view (pure
reads used by tab sizing).

**Verify: build + pixel-identical inspect tab in game.**

### Step 2 — Subject threading + pawnId-based component accessors

New `Source\UI\DiaryReaderSubject.cs`: struct `{ Pawn Pawn; string PawnId; string DisplayName;
bool Alive; static FromPawn(Pawn) }` (`Pawn` may be null for departed pawns).
`DiaryJournalView.Draw` takes `DiaryReaderSubject`; internally replace
`pawn.GetUniqueLoadID()` → `subject.PawnId`, header name (`pawn.LabelShortCap`, old
`ITab_Pawn_Diary.cs:546`) → `subject.DisplayName`; live-`Pawn` uses (dev preview,
writing-style icon, regenerate) already null-guard.

Id-based overloads added beside their Pawn siblings:
- `Source\Core\DiaryGameComponent.PublicApi.cs`: `RenderTokenForId(string)` (mirror of `:773`,
  via `LookupDiaryByPawnId` + `archive.CountForPawn`); refactor `DiaryTabYearIndexBuild` ctor
  (`:248-273`) to `(owner, pawnId, pawnAliveForBounds, flags)` with the `Pawn` overload
  wrapping it (`pawnAliveForBounds = PawnAliveForDiaryBounds(pawn)`); pawnId `Matches`
  overload (`:310`); `AcknowledgeGeneratedEntriesForId` (+4-arg variant, mirrors `:663-699`).
- `Source\Core\DiaryGameComponent.Lookup.cs`: `FavoriteEntryKeysForId(string)`,
  `SetEntryFavoriteById(string, string, bool)` (mirrors of `:924`/`:936`).

Cache: thread `subject` through `DiaryJournalView.VisibleEntriesCache.cs`.
**CRITICAL TRAP: the "index ready" sentinel `cachedIndexPawn != null` (old
`DiaryTabVisibleEntriesCache.cs:180`) must become a string `cachedIndexPawnId` sentinel, or a
null-Pawn subject re-triggers the index build every frame forever.** Favorites sync
(`EntryCards.cs:362-426`) switches to PawnId + the ById component calls.
`AddDevPreviewEntryIfNeeded` keeps taking `subject.Pawn` (live-pawn-only; no-op for null).

**Verify: build; tab identical (subject always has a live pawn in the tab path).**

### Step 3 — Reader data: directory, dead-pawn resolver, ordering policy (+ tests)

1. New partial `Source\Core\DiaryGameComponent.ReaderDirectory.cs`:
   `internal struct DiaryReaderPawnInfo { pawnId, cachedName, hotEntryCount,
   archivedEntryCount }`; `internal void CollectDiaryReaderPawns(List<...>)` — iterate the
   private `diaries` list (append-only; dead pawns retained; name from `record.pawnName`,
   count from `record.eventIds.Count` + `archive.CountForPawn`) then archive-only pawnIds from
   `archive.AllEntries` not covered by a record — exact pattern of the dev export
   (`Source\Core\DiaryGameComponent.Export.cs:72-239`). Archive-only rows have no cached name
   (leave empty; UI falls back to the "unknown" key).
2. New pure policy `Source\Pipeline\DiaryReaderListPolicy.cs` (System-only, no Verse): rows
   `{ pawnId, name, alive, isColonist, entryCount }` → ordered list + divider index. Living
   colonists first (always included, even 0 pages — any colonist can open a diary today),
   name-sorted (OrdinalIgnoreCase, pawnId tiebreak); then dead/departed (dead, unresolvable,
   or alive-non-colonist) only when `entryCount > 0`; empty names → caller-supplied "unknown".
3. New impure adapter `Source\UI\DiaryReaderPawnDirectory.cs`: owns the cached row list +
   pawn-resolution snapshot `Dictionary<string, Pawn>` built from: each map's
   `mapPawns.AllPawns`, `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive`,
   `Find.WorldPawns.AllPawnsAlive`, spawned corpses (`ThingRequestGroup.Corpse` →
   `Corpse.InnerPawn`), `Find.WorldPawns.AllPawnsDead`, casket/grave corpses
   (`Building_Casket` holding a `Corpse`). Unresolved → `Pawn = null` (placeholder portrait,
   cached name). NOTE: the existing resolvers
   (`DiaryGameComponent.GenerationEligibility.cs:316` and old `EntryCards.cs:1351`) find
   ALIVE pawns only — do not reuse them for the dead legs.
   **Refresh throttle:** rebuild on data-count change OR ≥250 game ticks since last build
   (cadence matches `NameHighlightCacheTicks`, `NameHighlights.cs:21`) with ~0.5s realtime
   floor; force-refresh on window open. Never per-frame.
4. New test project `tests/DiaryReaderPolicyTests/` (console-exe pattern of
   `tests/DiaryCapturePolicyTests/`; `<Compile Include>` of `DiaryReaderListPolicy.cs` only):
   partition/ordering, divider index, 0-page inclusion, unknown-name fallback,
   alive-non-colonist = departed, stable tiebreaks.

### Step 4 — The window: `Source\UI\Dialog_DiaryReader.cs`

`internal sealed class Dialog_DiaryReader : Window` (regular Window, NOT `MainTabWindow` —
must survive other tabs opening, drag, and not pause; windowing conventions per
`Source\UI\Dialog_PawnWritingStyle.cs:68-99`). Ctor:
`forcePause = false; draggable = true; resizeable = false; doCloseX = true;
closeOnClickedOutside = false; absorbInputAroundWindow = false; preventCameraMotion = false;
onlyOneOfTypeAllowed = true;`.

- `InitialSize` (fixed, responsive — no resize grip, no remembered rect; opens centered via
  the vanilla default): `min(readerMaxWidth, UI.screenWidth − readerScreenMargin) ×
  min(readerMaxHeight, UI.screenHeight − readerScreenMargin)`, floors
  `readerMinWidth/Height`. Resulting geometry (window margin 18px each side):
  - **Full HD 1920×1080** → 1460×940; book column at the full 850px.
  - **HD 1366×768** → ~1318×720; book column ≈750px.
  - **1280×720** → ~1232×672; with the compact pawn list (below), book column ≈720px —
    still wider than the 696px inspect-tab journal column.
  The journal renderer is width-parametric (measures at `width − 20f`) and the filter panel
  already self-hides below its minimum journal width, so the reflow needs no new rendering
  code; `readerBookWidth=850` is a preferred cap, not a promise.
- **Compact pawn list on narrow screens**: when the window's inner width <
  `readerCompactThreshold` (default 1360), the left pane uses `readerPawnListWidthCompact`
  (default 170) instead of `readerPawnListWidth` (220) — name labels truncate with tooltip.
- `internal static void Open(DiaryReaderSubject subject)` / `Open(Pawn)`: existing window →
  update selection + bring to front; else `Find.WindowStack.Add(...)` (dedupe pattern per
  old `Controls.cs:314-326`). `internal static void Toggle()` for the main button.
- State: `selectedSubject`, `pawnListScroll`, own `DiaryJournalView`, own
  `DiaryReaderPawnDirectory`, `showDeadPawns` (transient per window). Per-pawn reset comes
  free from the view.
- `DoWindowContents`: left pane `readerPawnListWidth`; header row with the dead-pawns toggle
  (`Widgets.CheckboxLabeled`, key `PawnDiary.Reader.ShowDeadPawns`); "Colonists" section, then
  (when toggled) "Dead and departed" section; row = portrait square (`readerPortraitSize`) +
  name + page count, dead rows tinted (`readerDeadPawnTint`), selected row
  `Widgets.DrawHighlightSelected`. Portrait: `PortraitsCache.Get(pawn, new Vector2(s,s),
  Rot4.South)` in try/catch → placeholder (`ContentFinder<Texture2D>.Get(
  "UI/Commands/PawnDiaryOpen", false) ?? TexButton.IconBook` at reduced alpha) when
  `Pawn == null` or the call throws. Middle+right: `readerWidth = min(remaining,
  readerBookWidth + filterPanelWidth + filterPanelGap + 24f)`, centered leftover;
  `journalView.Draw(readerRect, selectedSubject, component)` — yields the ~850px book column
  AND the shipped filter panel (year pager FloatMenu, favorites-only, tag chips, funnel
  toggle) with no new filter code. Archive-only subjects work via the Step-2 Id-based index
  (build tolerates `diary == null`); regenerate self-hides (archived + null pawn); favorites
  no-op without a record (same semantics as today, `Lookup.cs:943-947`); empty directory →
  `PawnDiary.Reader.NoDiaries` label; no selection → `PawnDiary.Reader.SelectPawnHint`.
- New `DiaryUiStyleDef` fields (code defaults in `Source\Defs\DiaryUiStyleDef.cs` + XML in
  `1.6\Defs\DiaryUiStyleDef.xml`, per the tunables-in-XML rule): `readerPawnListWidth=220`,
  `readerPawnListWidthCompact=170`, `readerCompactThreshold=1360`, `readerPaneGap=12`,
  `readerBookWidth=850`, `readerPawnRowHeight=48`, `readerPortraitSize=40`,
  `readerMaxWidth=1460`, `readerMaxHeight=940`, `readerMinWidth=760`, `readerMinHeight=520`,
  `readerScreenMargin=48`, `readerDeadPawnTint` (≈0.72/0.66/0.62/1).
- Dev-mode debug action "Open diary reader window" in `Source\Dev\PawnDiaryDebugActions.cs`
  so Step 4 is verifiable before Step 5 wires the setting.

### Step 5 — Mode toggle wiring (gizmo+tab disappear; main-bar button entry)

1. Setting: `public bool useDiaryReaderWindow = false;` in
   `Source\Settings\PawnDiarySettings.cs` (near `:124`) + `Scribe_Values.Look(ref
   useDiaryReaderWindow, "useDiaryReaderWindow", false)` in `ExposeData` (`:269` area). UI row
   in `DrawMainTab` under the ShowDiaryInspectTab row
   (`Source\Settings\PawnDiaryMod.SettingsWindow.cs:188-191`); **bump
   `EstimateSettingsContentHeight()` (`:435`)** 790f → 824f.
2. New `Source\UI\DiaryUiRouter.cs`: `ReaderWindowMode` (settings null-safe);
   `OpenDiaryFor(Pawn)` and `OpenDiaryAt(Pawn, string eventId)` — reader mode → set pending
   scroll + `Dialog_DiaryReader.Open`; default mode → `ITab_Pawn_Diary.OpenDiaryTab()` (with
   `Find.Selector` reselection where the current call sites do it).
3. **Gizmo hidden in alternative mode**: `ShouldShowDiaryCommand`
   (`Source\Patches\DiaryInspectCommandPatch.cs:110-118`) gains `&& (Settings == null ||
   !Settings.useDiaryReaderWindow)`. (Today the gizmo shows only when the tab button is
   disabled; in alternative mode neither shows.) `ToggleDiaryTab` (`:138`) routes through the
   router anyway (defense in depth).
4. **Tab hidden in alternative mode**: `ITab_Pawn_Diary.Hidden` (`:398-405`) →
   `!commandOpenRequested && (Settings == null || !Settings.showDiaryInspectTab ||
   Settings.useDiaryReaderWindow)`; `commandOpenRequested` is never set in reader mode
   because all open paths route to the window.
5. **Main-bar button**: new XML `1.6\Defs\MainButtonDefs\PawnDiary_MainButtons.xml`:
   `<MainButtonDef>` defName `PawnDiary_DiaryReader`, label/description (EN in def),
   `workerClass = PawnDiary.MainButtonWorker_DiaryReader`, `order = 85`, `minimized = True`,
   `iconPath = UI/Commands/PawnDiaryOpen`, `validWithoutMap = true` (the GameComponent exists
   without a map). New `Source\UI\MainButtonWorker_DiaryReader.cs`:
   `override bool Visible => base.Visible && DiaryUiRouter.ReaderWindowMode;`
   `override void Activate() => Dialog_DiaryReader.Toggle();`.
6. Reroute the 4 open call sites through the router: gizmo
   (`DiaryInspectCommandPatch.cs:150`), social log
   (`Source\Patches\DiarySocialLogPatches.cs:186-187`), linked-entry jump (old
   `EntryCards.cs:1339-1340` — in reader mode switch the window subject, skip `Find.Selector`
   reselection), dev preview (old `DevPreview.cs:61`).
7. `PawnDiaryMod.WriteSettings` (`Source\Settings\PawnDiaryMod.cs:119-128`): toggled OFF →
   close any open `Dialog_DiaryReader`; toggled ON → close the diary inspect tab if it is the
   open inspect tab (`MainTabWindow_Inspect.CloseOpenTab`, pattern at
   `DiaryInspectCommandPatch.cs:140-148`). Both null-guarded for main menu.

### Step 6 — Localization (EN + natively-authored RU, same change)

Keyed — `Languages\English\Keyed\PawnDiary.xml` + `Languages\Russian (Русский)\Keyed\PawnDiary.xml`
(RU authored natively, never calqued):

| Key | EN |
|---|---|
| `PawnDiary.Settings.UseDiaryReaderWindow` | "Open diaries in a separate reading window" |
| `PawnDiary.Settings.UseDiaryReaderWindowTip` | "Alternative reading mode: diaries open in a large three-pane window (pawn list, journal, filters) opened from a button on the bottom bar. The inspect tab and the pawn's Diary button are hidden while this is on." |
| `PawnDiary.Reader.LivingPawnsHeader` | "Colonists" |
| `PawnDiary.Reader.DepartedPawnsHeader` | "Dead and departed" |
| `PawnDiary.Reader.ShowDeadPawns` (+`...Tip`) | "Show dead and departed" |
| `PawnDiary.Reader.NoDiaries` | "No diaries yet." |
| `PawnDiary.Reader.UnknownPawn` | "Unknown pawn" |
| `PawnDiary.Reader.PawnRowPages` | "{0} pages" |
| `PawnDiary.Reader.SelectPawnHint` | "Select a pawn on the left to read their diary." |

Journal-side strings reuse existing `PawnDiary.Tab.*` keys unchanged. MainButtonDef
label/description: EN in the def XML; RU via
`Languages\Russian (Русский)\DefInjected\MainButtonDef\PawnDiary_MainButtons.xml` (follow the
repo's existing DefInjected conventions; add the EN DefInjected sibling if the repo mirrors EN).

### Step 7 — Tests

- `tests/DiaryReaderPolicyTests/` (Step 3): `DiaryReaderListPolicy` coverage.
- Extract the three-pane width math into a static System-only helper (testable in the same
  project) if it stays pure; otherwise skip (UnityEngine Rect can't link into pure tests).
- Existing suites untouched; pre-commit hook (`.githooks/verify.ps1`) runs pure tests +
  Debug build.

### Step 8 — Verification

Build (VS MSBuild, NOT `dotnet msbuild` — the committed-DLL freshness hook requires the VS
Roslyn): `MSBuild Source\PawnDiary.csproj /t:Build /p:Configuration=Debug`; rebuild + stage
`1.6/Assemblies/PawnDiary.dll`. In-game smoke:

1. Default mode: inspect tab pixel-identical after Steps 1-2 (filters, favorites, dividers,
   year pager, dev tools, social-log link jump, corpse selection); no main-bar button.
2. Toggle alternative mode: inspect tab button AND gizmo disappear; minimized Diary button
   appears in the bottom bar next to History; click toggles the window.
3. Window: game does NOT pause on open; draggable; fixed size (no resize grip); opens
   centered.
3a. Resolution sweep: at 1920×1080 the book column hits the full 850px; at 1366×768 and
   1280×720 the window fits with margin, the pawn list switches to compact width below the
   threshold, the filter panel still fits (or self-hides via its existing min-width guard),
   and nothing clips off-screen. Test via RimWorld's resolution setting or windowed mode.
4. Left pane: colonists listed (0-page colonists included); dead-pawns toggle reveals dead/
   departed (with pages only); portraits for live + corpse pawns; placeholder + cached name
   for departed/archive-only; selection switches diary and resets scroll/filters.
5. Middle: ~850px cards, dividers, expand/collapse, copy; regenerate hidden for dead/archived.
6. Right: year pager, favorites-only, tag chips (instant apply), funnel toggle.
7. Favorites: star in window → close/reopen → save/load → persists; works for a dead pawn
   with a record.
8. Social-log click in alternative mode opens the window scrolled to the entry.
9. Toggle mode off while window open → window closes; tab returns.
10. Large history (dev mock, 2000 entries/year): sliced loading, smooth scrolling.

### Step 9 — Docs

`repowiki/README.md` §7 "Settings And UI" (~line 2797): reader window, `useDiaryReaderWindow`,
interaction with `showDiaryInspectTab`, the `DiaryJournalView` extraction, `reader*` style
knobs, main-bar button. `CHANGELOG.md`: dated entry. Both in the same change as the code.

## Risks

- **PortraitsCache with dead/world pawns**: corpse `InnerPawn` renders (possibly rotted);
  non-map dead world pawns may lack initialized graphics → mandatory try/catch → placeholder;
  never call with null. `healthStateOverride` exists if dead-pose portraits look wrong.
- **Extraction regression**: mitigated by byte-identical bodies + compile verification +
  smoke after Step 1 before any functional change. Biggest trap: the visible-entries cache
  `cachedIndexPawn != null` sentinel (Step 2) — missing it = infinite rebuild for null-Pawn
  subjects.
- **World scans throttled** (250 ticks / 0.5s / data-change) — never per frame.
- **Shared statics across hosts** (pending scroll, `EntryFirstSeenSeconds`): keys are
  globally unique event ids; mid-toggle double-draw is a one-frame cosmetic edge, accepted.
- **Buried/exotic corpses** inside unscanned containers resolve as "unknown" (placeholder) —
  acceptable v1; resolver legs are isolated in `DiaryReaderPawnDirectory` for later additions.

## File inventory

Modify: `Source\UI\ITab_Pawn_Diary*.cs` (rename → `DiaryJournalView.*`),
`Source\UI\DiaryTabVisibleEntriesCache.cs`, `Source\Core\DiaryGameComponent.PublicApi.cs`,
`Source\Core\DiaryGameComponent.Lookup.cs`, `Source\Patches\DiaryInspectCommandPatch.cs`,
`Source\Patches\DiarySocialLogPatches.cs`, `Source\Settings\PawnDiarySettings.cs`,
`Source\Settings\PawnDiaryMod.SettingsWindow.cs`, `Source\Settings\PawnDiaryMod.cs`,
`Source\Defs\DiaryUiStyleDef.cs`, `1.6\Defs\DiaryUiStyleDef.xml`, both Keyed XMLs,
`repowiki/README.md`, `CHANGELOG.md`.

New: `Source\UI\DiaryReaderSubject.cs`, `Source\UI\Dialog_DiaryReader.cs`,
`Source\UI\DiaryReaderPawnDirectory.cs`, `Source\UI\DiaryUiRouter.cs`,
`Source\UI\MainButtonWorker_DiaryReader.cs`,
`Source\Core\DiaryGameComponent.ReaderDirectory.cs`,
`Source\Pipeline\DiaryReaderListPolicy.cs`,
`1.6\Defs\MainButtonDefs\PawnDiary_MainButtons.xml`,
`Languages\Russian (Русский)\DefInjected\MainButtonDef\PawnDiary_MainButtons.xml`,
`tests\DiaryReaderPolicyTests\`.
