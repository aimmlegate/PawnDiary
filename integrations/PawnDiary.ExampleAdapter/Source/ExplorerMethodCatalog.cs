// The catalog of PawnDiaryApi methods the explorer can drive. This is the heart of the window's
// left tree: each entry is one API call, with (a) its tree path, (b) a form drawer that renders the
// method's inputs from shared FormState, and (c) an invoke action that fires the API and appends
// the result to ExplorerState.Log.
//
// Design notes:
//   • One shared FormState object holds every form field. Only the fields the selected method uses
//     are drawn, but values persist across method switches so a tester can fill in eventKey once
//     and reuse it across Submit/Preview/Read. Sensible defaults are set in FormState.Reset so
//     every method works with zero typing.
//   • Every invoke is wrapped in try/catch. PawnDiaryApi never throws into the caller by contract,
//     but a developer poking the explorer with weird inputs (or a future PawnDiaryApi bug) must not
//     crash the window — catching here keeps the log usable.
//   • DTO field labels stay English (schema labels, AGENTS.md §12 carve-out). Window chrome
//     (titles, buttons) is localized via ExampleAdapter.xml keyed strings.
//
// New to C#/RimWorld? See AGENTS.md.
using System;
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Persistent form state shared by every method's form drawer. Fields a method does not use are
    /// simply ignored; values persist across method switches so a developer's input is not lost.
    /// </summary>
    internal sealed class FormState
    {
        // shared
        public string sourceId = "aimmlegate.pawndiary.adapter.example";
        public string povRole = string.Empty;          // blank → let API pick
        public string maxCount = "5";

        // ExternalEventRequest (shared by Submit / Preview / PromptEntry)
        public string eventKey = "exampleadapter_quiet_moment";
        public string summaryText = string.Empty;
        public string eventLabel = string.Empty;
        public string extraContext = string.Empty;     // multiline
        public string promptFragment = string.Empty;
        public string enchantmentCandidates = string.Empty; // multiline
        public int enchantmentMode;                    // 0 keep / 1 add / 2 replace
        public string dedupKey = string.Empty;
        public string dedupTicks = string.Empty;

        // ExternalPromptEntryRequest (adds promptInstruction)
        public string promptInstruction = "a brief moment worth noting today";

        // ExternalDirectEntryRequest
        public string directText = "I sat by the window and watched the rain for a while.";
        public string directPartnerText = string.Empty;
        public string directTitle = string.Empty;
        public string directPartnerTitle = string.Empty;
        public bool directGenerateTitle;

        // Style override
        public string styleSourceId = "aimmlegate.pawndiary.adapter.example";
        public string styleRule = "write in terse, clipped sentences";

        // Read entry by id
        public string manualEventId = string.Empty;
        public string manualPovRole = "initiator";

        // DiaryEntryTitleQuery (shared by Read Pawn reads)
        public string qDomain = string.Empty;
        public string qAtmosphereCue = string.Empty;
        public string qPovRole = string.Empty;
        public string qSourceId = string.Empty;
        public string qEventKey = string.Empty;
        public string qDateContains = string.Empty;
        public int qIncludeActive = 1;                 // 0 no / 1 yes
        public int qIncludeArchived = 1;
        public int qImportant;                          // 0 any / 1 not / 2 important
        public int qHasTitle;                           // tri-state UI index
        public int qHasGeneratedText;                  // tri-state UI index

        // Context bundle
        public bool bundleIncludeImportant;
        public bool bundleUseQuery;
        public bool bundleUseImportant;

        // Toggle for SetDiaryGenerationEnabled + GetPromptEnchantments
        public bool setGenEnabled = true;
        public bool enchantIncludeImportant;

        // Read-entry handle source: remembered picker vs manual id+role
        public bool useRememberedHandle = true;
        public int rememberedHandleIndex = -1;

        // Whether the Read Pawn methods should apply the DiaryEntryTitleQuery
        public bool applyPawnQuery;

        /// <summary>
        /// Builds the shared ExternalEventRequest from the current form. Partner is attached only
        /// when a partner pawn is selected and differs from the subject.
        /// </summary>
        public ExternalEventRequest BuildEventRequest(Pawn subject, Pawn partner)
        {
            ExternalEventRequest req = new ExternalEventRequest
            {
                sourceId = sourceId,
                eventKey = eventKey,
                subject = subject,
                partner = (partner != null && partner != subject) ? partner : null,
                summaryText = summaryText,
                eventLabel = eventLabel,
                extraContext = ExplorerParsing.LinesFromMultiline(extraContext),
                promptFragment = promptFragment,
                dedupKey = dedupKey,
                dedupTicks = ExplorerParsing.ParseTick(dedupTicks, 0)
            };

            List<string> candidates = ExplorerParsing.LinesFromMultiline(enchantmentCandidates);
            if (candidates.Count > 0)
            {
                req.promptEnchantmentCandidates = candidates;
                req.replacePromptEnchantments = enchantmentMode == 2;
            }

            return req;
        }

        /// <summary>
        /// Builds a prompt-entry request from the shared event-request fields + promptInstruction.
        /// </summary>
        public ExternalPromptEntryRequest BuildPromptEntryRequest(Pawn subject, Pawn partner)
        {
            ExternalEventRequest baseReq = BuildEventRequest(subject, partner);
            return new ExternalPromptEntryRequest
            {
                sourceId = baseReq.sourceId,
                eventKey = baseReq.eventKey,
                subject = baseReq.subject,
                partner = baseReq.partner,
                summaryText = baseReq.summaryText,
                eventLabel = baseReq.eventLabel,
                extraContext = baseReq.extraContext,
                promptFragment = baseReq.promptFragment,
                promptEnchantmentCandidates = baseReq.promptEnchantmentCandidates,
                replacePromptEnchantments = baseReq.replacePromptEnchantments,
                dedupKey = baseReq.dedupKey,
                dedupTicks = baseReq.dedupTicks,
                promptInstruction = promptInstruction
            };
        }

        public ExternalDirectEntryRequest BuildDirectRequest(Pawn subject, Pawn partner)
        {
            return new ExternalDirectEntryRequest
            {
                sourceId = sourceId,
                eventKey = eventKey,
                subject = subject,
                partner = (partner != null && partner != subject) ? partner : null,
                text = directText,
                partnerText = (partner != null && partner != subject) ? directPartnerText : string.Empty,
                title = directTitle,
                partnerTitle = (partner != null && partner != subject) ? directPartnerTitle : string.Empty,
                summaryText = summaryText,
                eventLabel = eventLabel,
                extraContext = ExplorerParsing.LinesFromMultiline(extraContext),
                dedupKey = dedupKey,
                dedupTicks = ExplorerParsing.ParseTick(dedupTicks, 0),
                generateTitleIfMissing = directGenerateTitle
            };
        }

        public DiaryEntryTitleQuery BuildQuery()
        {
            return new DiaryEntryTitleQuery
            {
                domain = qDomain.Trim(),
                atmosphereCue = qAtmosphereCue.Trim(),
                povRole = qPovRole.Trim(),
                sourceId = qSourceId.Trim(),
                eventKey = qEventKey.Trim(),
                dateContains = qDateContains.Trim(),
                includeActive = qIncludeActive == 1,
                includeArchived = qIncludeArchived == 1,
                important = ExplorerParsing.TriStateFromIndex(qImportant),
                hasTitle = ExplorerParsing.TriStateFromIndex(qHasTitle),
                hasGeneratedText = ExplorerParsing.TriStateFromIndex(qHasGeneratedText)
            };
        }

        public int MaxCount => ExplorerParsing.ParsePositiveInt(maxCount, 5);
    }

    /// <summary>
    /// One node in the explorer's left tree. Leaf nodes carry a form drawer + invoke action and a
    /// short summary line; branch nodes are purely organizational.
    /// </summary>
    internal sealed class ExplorerMethodNode
    {
        public string label;            // shown in the tree
        public string category;         // grouping header (the tree level above the leaf)
        public string summary;          // one-line description under the form title
        public Action<FormState, Rect> drawForm;     // renders the method's inputs from shared state
        public Action<FormState> invoke;             // runs the API call and appends the result
    }

    /// <summary>
    /// Static catalog of every API method the explorer exposes. The window renders this as a tree.
    /// </summary>
    internal static class ExplorerMethodCatalog
    {
        // The flat list, in display order. The window groups by `category` for the tree.
        public static readonly List<ExplorerMethodNode> Nodes = BuildNodes();

        private static List<ExplorerMethodNode> BuildNodes()
        {
            List<ExplorerMethodNode> list = new List<ExplorerMethodNode>();

            // ── READINESS ──────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Readiness", "IsReady", "True when a game is loaded and the diary component is alive.",
                (f, r) => { },
                f =>
                {
                    bool v = PawnDiaryApi.IsReady;
                    ExplorerState.AppendLog("IsReady", SnapshotFormatter.FormatBool("IsReady", v),
                        SnapshotFormatter.FormatBool("IsReady", v));
                }));

            list.Add(Leaf("Readiness", "ApiVersion", "Contract version of the PawnDiaryApi surface.",
                (f, r) => { },
                f =>
                {
                    int v = PawnDiaryApi.ApiVersion;
                    string s = SnapshotFormatter.FormatApiVersion(v);
                    ExplorerState.AppendLog("ApiVersion", s, s);
                }));

            list.Add(Leaf("Readiness", "IsDiaryEligible(subject)", "Base owner eligibility + saved per-pawn toggle.",
                (f, r) => { },
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryApi.IsDiaryEligible(p);
                    string s = SnapshotFormatter.FormatBool("IsDiaryEligible", v);
                    ExplorerState.AppendLog("IsDiaryEligible", s, s + "\n(subject: " + ExplorerPawns.LabelOrEmpty(p) + ")");
                }));

            list.Add(Leaf("Readiness", "IsDiaryGenerationEnabled(subject)", "The per-pawn generation toggle (Diary tab / dev panel).",
                (f, r) => { },
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryApi.IsDiaryGenerationEnabled(p);
                    string s = SnapshotFormatter.FormatBool("IsDiaryGenerationEnabled", v);
                    ExplorerState.AppendLog("IsDiaryGenerationEnabled", s, s);
                }));

            list.Add(Leaf("Readiness", "SetDiaryGenerationEnabled(subject, bool)", "Sets the per-pawn generation toggle. True re-queues pending work.",
                (f, r) => DrawToggleRow(r, "PawnDiaryExampleAdapter.SetEnabledToggle", ref f.setGenEnabled),
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryApi.SetDiaryGenerationEnabled(p, f.setGenEnabled);
                    string s = SnapshotFormatter.FormatBool("SetDiaryGenerationEnabled(→" + f.setGenEnabled + ")", v);
                    ExplorerState.AppendLog("SetDiaryGenerationEnabled", s, s);
                }));

            // ── SUBMIT EVENT ───────────────────────────────────────────────────────────────────
            list.Add(Leaf("Submit", "SubmitEvent(req)",
                "Validate + dispatch. Returns true when recorded; never throws. Outcomes: Recorded/Invalid/OffThread/Ineligible/DroppedBudget/DroppedByPipeline.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    bool recorded = PawnDiaryApi.SubmitEvent(req, out SubmitEventOutcome outcome);
                    string oneLine = SnapshotFormatter.FormatSubmitOutcome(recorded, outcome);
                    ExplorerState.AppendLog("SubmitEvent", oneLine, oneLine + "\n\n" + req.EventRequestSummary(s, p));
                }));

            list.Add(Leaf("Submit", "SubmitEventWithHandle(req)",
                "Same dispatch, returns stable handles when the pipeline actually creates an entry.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryApi.SubmitEventWithHandle(req);
                    string oneLine = "recorded=" + result.recorded + (result.pairwise ? " pairwise" : string.Empty);
                    string detail = SnapshotFormatter.Format(result);
                    ExplorerState.AppendLog("SubmitEventWithHandle", oneLine, detail);
                    if (result.recorded && result.primary != null)
                    {
                        ExplorerState.RememberHandle(result.primary, ExplorerPawns.LabelOrEmpty(s) + " (initiator)");
                    }

                    if (result.pairwise && result.partner != null)
                    {
                        ExplorerState.RememberHandle(result.partner, ExplorerPawns.LabelOrEmpty(p) + " (recipient)");
                    }
                }));

            list.Add(Leaf("Submit", "PreviewPrompt(eventReq, povRole)",
                "Side-effect-free prompt preview. No event saved, no tokens spent, RNG restored.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    DiaryPromptPreviewSnapshot preview = PawnDiaryApi.PreviewPrompt(req, ExplorerParsing.NormalizePovRole(f.povRole));
                    string oneLine = preview == null ? "preview = null" : "povRole=" + preview.povRole + " pairwise=" + preview.pairwise;
                    ExplorerState.AppendLog("PreviewPrompt(event)", oneLine, SnapshotFormatter.Format(preview));
                }));

            // ── SUBMIT PROMPT ENTRY ─────────────────────────────────────────────────────────────
            list.Add(Leaf("Prompt Entry", "SubmitPromptEntry(req)",
                "Pawn Diary writes from your promptInstruction; persona/safety/style/context stay owned by Pawn Diary.",
                DrawPromptEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalPromptEntryRequest req = f.BuildPromptEntryRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryApi.SubmitPromptEntry(req);
                    string oneLine = "recorded=" + result.recorded + (result.pairwise ? " pairwise" : string.Empty);
                    ExplorerState.AppendLog("SubmitPromptEntry", oneLine, SnapshotFormatter.Format(result));
                    if (result.recorded && result.primary != null)
                    {
                        ExplorerState.RememberHandle(result.primary, ExplorerPawns.LabelOrEmpty(s) + " (initiator)");
                    }
                }));

            list.Add(Leaf("Prompt Entry", "PreviewPrompt(promptEntryReq, povRole)",
                "Preview the wrapped prompt-entry assembly before spending tokens.",
                DrawPromptEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalPromptEntryRequest req = f.BuildPromptEntryRequest(s, p);
                    DiaryPromptPreviewSnapshot preview = PawnDiaryApi.PreviewPrompt(req, ExplorerParsing.NormalizePovRole(f.povRole));
                    string oneLine = preview == null ? "preview = null" : "povRole=" + preview.povRole;
                    ExplorerState.AppendLog("PreviewPrompt(promptEntry)", oneLine, SnapshotFormatter.Format(preview));
                }));

            // ── SUBMIT DIRECT ENTRY ─────────────────────────────────────────────────────────────
            list.Add(Leaf("Direct Entry", "SubmitDirectEntry(req)",
                "Caller owns final prose; no main LLM rewrite. Optional title-only generation when requested.",
                DrawDirectEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalDirectEntryRequest req = f.BuildDirectRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryApi.SubmitDirectEntry(req);
                    string oneLine = "recorded=" + result.recorded + (result.pairwise ? " pairwise" : string.Empty);
                    ExplorerState.AppendLog("SubmitDirectEntry", oneLine, SnapshotFormatter.Format(result));
                    if (result.recorded && result.primary != null)
                    {
                        ExplorerState.RememberHandle(result.primary, ExplorerPawns.LabelOrEmpty(s) + " (initiator)");
                    }
                }));

            // ── READ ENTRY ──────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Read Entry", "GetEntryStatus(handle)",
                "Lifecycle status for a remembered handle (from a submit) or a manual eventId+povRole.",
                DrawReadEntryByIdForm,
                f =>
                {
                    DiaryEntryHandle h = f.ResolveHandleForRead();
                    if (h == null)
                    {
                        ExplorerState.AppendLog("GetEntryStatus", "no handle", "Pick a remembered handle or type eventId+povRole.");
                        return;
                    }

                    DiaryEntryStatusSnapshot snap = PawnDiaryApi.GetEntryStatus(h);
                    string oneLine = snap == null ? "null" : "status=" + snap.status + " title=" + (snap.titleComplete ? "yes" : "no");
                    ExplorerState.AppendLog("GetEntryStatus", oneLine, SnapshotFormatter.Format(snap));
                }));

            list.Add(Leaf("Read Entry", "GetEntrySnapshot(handle)",
                "Full read: completed player-visible prose + metadata. Use after status == complete.",
                DrawReadEntryByIdForm,
                f =>
                {
                    DiaryEntryHandle h = f.ResolveHandleForRead();
                    if (h == null)
                    {
                        ExplorerState.AppendLog("GetEntrySnapshot", "no handle", "Pick a remembered handle or type eventId+povRole.");
                        return;
                    }

                    DiaryEntrySnapshot snap = PawnDiaryApi.GetEntrySnapshot(h);
                    string oneLine = snap == null ? "null" : "status=" + snap.status + " textLen=" + (snap.generatedText ?? string.Empty).Length;
                    ExplorerState.AppendLog("GetEntrySnapshot", oneLine, SnapshotFormatter.Format(snap));
                }));

            // ── READ PAWN ───────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Read Pawn", "GetRecentEntryTitles(pawn, n[, query])",
                "Newest completed titles. Toggle the query filter to exercise the v5 overload.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetRecentEntryTitles", "no pawn", "(no eligible pawn)"); return; }
                    List<DiaryEntryTitleSnapshot> titles = f.applyPawnQuery
                        ? PawnDiaryApi.GetRecentEntryTitles(s, f.MaxCount, f.BuildQuery())
                        : PawnDiaryApi.GetRecentEntryTitles(s, f.MaxCount);
                    string oneLine = "count=" + titles.Count;
                    ExplorerState.AppendLog("GetRecentEntryTitles", oneLine, SnapshotFormatter.Format(titles as IReadOnlyList<DiaryEntryTitleSnapshot>));
                }));

            list.Add(Leaf("Read Pawn", "GetContextSnapshot(pawn, n[, query])",
                "Recent completed prose summaries — the memory surface for chat/context adapters.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetContextSnapshot", "no pawn", "(no eligible pawn)"); return; }
                    DiaryContextSnapshot ctx = f.applyPawnQuery
                        ? PawnDiaryApi.GetContextSnapshot(s, f.MaxCount, f.BuildQuery())
                        : PawnDiaryApi.GetContextSnapshot(s, f.MaxCount);
                    string oneLine = ctx == null ? "null" : "entryCount=" + ctx.entryCount;
                    ExplorerState.AppendLog("GetContextSnapshot", oneLine, SnapshotFormatter.Format(ctx));
                }));

            list.Add(Leaf("Read Pawn", "GetEntryStats(pawn[, query])",
                "Aggregate counts without materializing rows.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetEntryStats", "no pawn", "(no eligible pawn)"); return; }
                    DiaryEntryStatsSnapshot stats = f.applyPawnQuery
                        ? PawnDiaryApi.GetEntryStats(s, f.BuildQuery())
                        : PawnDiaryApi.GetEntryStats(s);
                    string oneLine = stats == null ? "null" : "total=" + stats.total;
                    ExplorerState.AppendLog("GetEntryStats", oneLine, SnapshotFormatter.Format(stats));
                }));

            // ── READ MACHINERY ──────────────────────────────────────────────────────────────────
            list.Add(Leaf("Machinery", "GetPawnSummary(pawn)",
                "Structured pawn-summary Pawn Diary would feed its own prompt.",
                (f, r) => { },
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetPawnSummary", "no pawn", "(no eligible pawn)"); return; }
                    DiaryPawnSummarySnapshot sum = PawnDiaryApi.GetPawnSummary(s);
                    string oneLine = sum == null ? "null" : "sex=" + sum.sex + " mood=" + sum.mood;
                    ExplorerState.AppendLog("GetPawnSummary", oneLine, SnapshotFormatter.Format(sum));
                }));

            list.Add(Leaf("Machinery", "GetPromptEnchantments(pawn, includeImportant)",
                "Post-suppression, post-multiplier enchantment candidate set.",
                (f, r) => DrawCheckboxRow(r, "PawnDiaryExampleAdapter.IncludeImportantToggle", ref f.enchantIncludeImportant),
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetPromptEnchantments", "no pawn", "(no eligible pawn)"); return; }
                    List<DiaryPromptEnchantmentCandidateSnapshot> cs = PawnDiaryApi.GetPromptEnchantments(s, f.enchantIncludeImportant);
                    string oneLine = "count=" + cs.Count;
                    ExplorerState.AppendLog("GetPromptEnchantments", oneLine, SnapshotFormatter.Format(cs as IReadOnlyList<DiaryPromptEnchantmentCandidateSnapshot>));
                }));

            list.Add(Leaf("Machinery", "GetContextBundle(pawn, n, query?, includeImportant?)",
                "Style + summary + enchantments + recent memory in one call. Four overloads.",
                DrawContextBundleForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetContextBundle", "no pawn", "(no eligible pawn)"); return; }

                    // Pick the most specific overload the tester enabled. query+important → 4-arg;
                    // query alone → 3-arg(query); important alone → 3-arg(bool); neither → 2-arg.
                    DiaryContextBundleSnapshot b;
                    if (f.bundleUseQuery)
                    {
                        b = PawnDiaryApi.GetContextBundle(s, f.MaxCount, f.BuildQuery(), f.bundleIncludeImportant);
                    }
                    else if (f.bundleUseImportant)
                    {
                        b = PawnDiaryApi.GetContextBundle(s, f.MaxCount, f.bundleIncludeImportant);
                    }
                    else
                    {
                        b = PawnDiaryApi.GetContextBundle(s, f.MaxCount);
                    }

                    string oneLine = b == null ? "null" : "summary=" + (b.pawnSummary != null ? "yes" : "no");
                    ExplorerState.AppendLog("GetContextBundle", oneLine, SnapshotFormatter.Format(b));
                }));

            list.Add(Leaf("Machinery", "GetWritingStyle(pawn)",
                "The pawn's BASE saved writing-style rule. Excludes hediff style overrides.",
                (f, r) => { },
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetWritingStyle", "no pawn", "(no eligible pawn)"); return; }
                    DiaryWritingStyleSnapshot style = PawnDiaryApi.GetWritingStyle(s);
                    string oneLine = style == null ? "null" : "style=" + style.styleDefName;
                    ExplorerState.AppendLog("GetWritingStyle", oneLine, SnapshotFormatter.Format(style));
                }));

            list.Add(Leaf("Machinery", "GetAvailableWritingStyles()",
                "Effective style catalog (XML styles + settings-backed custom rows).",
                (f, r) => { },
                f =>
                {
                    List<DiaryWritingStyleSnapshot> styles = PawnDiaryApi.GetAvailableWritingStyles();
                    string oneLine = "count=" + styles.Count;
                    ExplorerState.AppendLog("GetAvailableWritingStyles", oneLine, SnapshotFormatter.FormatStyles(styles as IReadOnlyList<DiaryWritingStyleSnapshot>));
                }));

            // ── STYLE OVERRIDE ──────────────────────────────────────────────────────────────────
            list.Add(Leaf("Style", "SetWritingStyleOverride(pawn, sourceId, rule)",
                "Source-owned free-form style override; sits above base + hediff styles.",
                DrawStyleOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("SetWritingStyleOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryApi.SetWritingStyleOverride(s, f.styleSourceId, f.styleRule);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("SetWritingStyleOverride", oneLine, oneLine + "\nsourceId=" + f.styleSourceId + "\nrule=" + f.styleRule);
                }));

            list.Add(Leaf("Style", "ResetWritingStyleOverride(pawn, sourceId)",
                "Clears a source-owned override. False when another source owns the active override.",
                DrawStyleOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("ResetWritingStyleOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryApi.ResetWritingStyleOverride(s, f.styleSourceId);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("ResetWritingStyleOverride", oneLine, oneLine);
                }));

            // ── HOOKS ───────────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Hooks", "Activity log",
                "Live ring buffer of RegisterEntryStatusListener firings + RegisterPawnContextProvider invocations.",
                DrawHooksForm,
                f =>
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append("=== entry-status listener events (").Append(ExplorerState.ListenerEvents.Count).Append(") ===\n");
                    foreach (string e in ExplorerState.ListenerEvents)
                    {
                        sb.Append("  ").Append(e).Append('\n');
                    }

                    sb.Append("\nprovider invocations: ").Append(ExplorerState.providerInvocations);
                    string s = sb.ToString();
                    ExplorerState.AppendLog("Hooks/Activity", "listener=" + ExplorerState.ListenerEvents.Count + " provider=" + ExplorerState.providerInvocations, s);
                }));

            return list;
        }

        // ── form drawer helpers (shared widgets) ─────────────────────────────────────────────

        private static ExplorerMethodNode Leaf(string category, string label, string summary,
            Action<FormState, Rect> drawForm, Action<FormState> invoke)
        {
            return new ExplorerMethodNode
            {
                category = category,
                label = label,
                summary = summary,
                drawForm = drawForm,
                invoke = invoke
            };
        }

        private static void DrawEventRequestForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.Summary", ref f.summaryText);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventLabel", ref f.eventLabel);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PromptFragment", ref f.promptFragment);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EnchantmentCandidates", ref f.enchantmentCandidates);
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.EnchantmentMode", ref f.enchantmentMode,
                new[] { "keep", "add", "replace" });
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DedupKey", ref f.dedupKey);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DedupTicks", ref f.dedupTicks);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PovRole", ref f.povRole);
            list.End();
        }

        private static void DrawPromptEntryForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            // Prompt-entry reuses the event-request shape + adds promptInstruction on top.
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PromptInstruction", ref f.promptInstruction);
            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.Summary", ref f.summaryText);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PovRole", ref f.povRole);
            list.End();
        }

        private static void DrawDirectEntryForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectText", ref f.directText);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectPartnerText", ref f.directPartnerText);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectTitle", ref f.directTitle);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectPartnerTitle", ref f.directPartnerTitle);
            list.CheckboxLabeled("PawnDiaryExampleAdapter.Field.DirectGenerateTitle".Translate(), ref f.directGenerateTitle);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext);
            list.End();
        }

        private static void DrawReadEntryByIdForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.Label("PawnDiaryExampleAdapter.ReadEntry.HandleHint".Translate());
            DrawRememberedHandlePicker(list, f);
            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ManualEventId", ref f.manualEventId);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ManualPovRole", ref f.manualPovRole);
            list.End();
        }

        private static void DrawReadPawnForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.MaxCount", ref f.maxCount);
            list.CheckboxLabeled("PawnDiaryExampleAdapter.Field.ApplyQuery".Translate(), ref f.applyPawnQuery);
            if (!f.applyPawnQuery)
            {
                list.End();
                return;
            }

            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QDomain", ref f.qDomain);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QAtmosphereCue", ref f.qAtmosphereCue);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QPovRole", ref f.qPovRole);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QSourceId", ref f.qSourceId);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QEventKey", ref f.qEventKey);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QDateContains", ref f.qDateContains);
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QImportant", ref f.qImportant, new[] { "any", "no", "yes" });
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QHasTitle", ref f.qHasTitle, new[] { "any", "no", "yes" });
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QHasGeneratedText", ref f.qHasGeneratedText, new[] { "any", "no", "yes" });
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QIncludeActive", ref f.qIncludeActive, new[] { "no", "yes" });
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QIncludeArchived", ref f.qIncludeArchived, new[] { "no", "yes" });
            list.End();
        }

        private static void DrawContextBundleForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.MaxCount", ref f.maxCount);
            list.CheckboxLabeled("PawnDiaryExampleAdapter.Field.BundleUseQuery".Translate(), ref f.bundleUseQuery);
            list.CheckboxLabeled("PawnDiaryExampleAdapter.Field.BundleUseImportant".Translate(), ref f.bundleUseImportant);
            if (f.bundleUseImportant)
            {
                list.CheckboxLabeled("PawnDiaryExampleAdapter.IncludeImportantToggle".Translate(), ref f.bundleIncludeImportant);
            }

            list.End();
        }

        private static void DrawStyleOverrideForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.StyleSourceId", ref f.styleSourceId);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.StyleRule", ref f.styleRule);
            list.End();
        }

        private static void DrawHooksForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.Label("PawnDiaryExampleAdapter.Hooks.Hint".Translate());
            list.Label(ExplorerState.ActivitySummary());
            list.End();
        }

        // ── small widget helpers (the ones Listing_Standard doesn't have built in) ──────────

        private static void DrawToggleRow(Rect r, string labelKey, ref bool value)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.CheckboxLabeled(labelKey.Translate(), ref value);
            list.End();
        }

        private static void DrawCheckboxRow(Rect r, string labelKey, ref bool value)
        {
            DrawToggleRow(r, labelKey, ref value);
        }

        private static void DrawEnumRow(Listing_Standard list, string labelKey, ref int value, string[] options)
        {
            Rect row = list.GetRect(30f);
            Widgets.Label(new Rect(row.x, row.y, row.width * 0.45f, row.height), labelKey.Translate());
            Rect seg = new Rect(row.x + row.width * 0.45f, row.y, row.width * 0.55f, row.height);
            for (int i = 0; i < options.Length; i++)
            {
                Rect btn = new Rect(seg.x + (seg.width / options.Length) * i + 2f, seg.y, seg.width / options.Length - 4f, seg.height);
                bool on = value == i;
                if (Widgets.ButtonText(btn, (on ? "● " : "  ") + options[i]))
                {
                    value = i;
                }
            }
        }

        private static void DrawRememberedHandlePicker(Listing_Standard list, FormState f)
        {
            if (ExplorerState.RememberedHandles.Count == 0)
            {
                list.Label("PawnDiaryExampleAdapter.ReadEntry.NoHandles".Translate());
                return;
            }

            Rect row = list.GetRect(30f);
            string pickHint = "PawnDiaryExampleAdapter.ReadEntry.PickHandle".Translate();
            string current = (f.rememberedHandleIndex >= 0 && f.rememberedHandleIndex < ExplorerState.RememberedHandles.Count)
                ? ExplorerState.RememberedHandles[f.rememberedHandleIndex].label
                : pickHint;
            if (Widgets.ButtonText(row, current))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                for (int i = 0; i < ExplorerState.RememberedHandles.Count; i++)
                {
                    int idx = i;
                    RememberedHandle h = ExplorerState.RememberedHandles[i];
                    opts.Add(new FloatMenuOption(h.label + "  [" + h.handle.eventId + "]", () =>
                    {
                        f.rememberedHandleIndex = idx;
                        f.manualEventId = h.handle.eventId;
                        f.manualPovRole = h.handle.povRole;
                        f.useRememberedHandle = true;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }
    }

    /// <summary>
    /// Extension methods that live next to FormState because they reference RimWorld types the
    /// FormState field bag itself avoids. Kept in this file so the form state bag stays plain.
    /// </summary>
    internal static class FormStateExtensions
    {
        /// <summary>
        /// One labeled single-line text field row, since Listing_Standard has no built-in labeled
        /// text field. Label takes the left ~40%, field the right ~60%. The field keeps keyboard
        /// focus only while the rect is hot, matching how the rest of RimWorld's IMGUI behaves.
        /// </summary>
        public static void TextFieldEntryLabeled(this Listing_Standard list, string labelKey, ref string value)
        {
            Rect row = list.GetRect(30f);
            float labelWidth = row.width * 0.4f;
            Widgets.Label(new Rect(row.x, row.y + 3f, labelWidth, row.height), labelKey.Translate());
            Rect field = new Rect(row.x + labelWidth + 6f, row.y, row.width - labelWidth - 6f, row.height);
            string current = value ?? string.Empty;
            string next = Widgets.TextField(field, current);
            if (next != current)
            {
                value = next;
            }
        }

        /// <summary>
        /// Resolves the handle a Read-Entry method should target: the remembered selection when set,
        /// otherwise a synthetic handle built from the manual eventId+povRole fields.
        /// </summary>
        public static DiaryEntryHandle ResolveHandleForRead(this FormState f)
        {
            if (f.useRememberedHandle
                && f.rememberedHandleIndex >= 0
                && f.rememberedHandleIndex < ExplorerState.RememberedHandles.Count)
            {
                return ExplorerState.RememberedHandles[f.rememberedHandleIndex].handle;
            }

            if (string.IsNullOrWhiteSpace(f.manualEventId))
            {
                return null;
            }

            return new DiaryEntryHandle
            {
                eventId = f.manualEventId.Trim(),
                povRole = (f.manualPovRole ?? string.Empty).Trim()
            };
        }

        /// <summary>
        /// Short one-line summary of an event request for the result log, so a tester sees what was
        /// sent without re-reading the form fields.
        /// </summary>
        public static string EventRequestSummary(this ExternalEventRequest req, Pawn subject, Pawn partner)
        {
            if (req == null)
            {
                return "(null request)";
            }

            return "sourceId=" + req.sourceId
                + "\neventKey=" + req.eventKey
                + "\nsubject=" + ExplorerPawns.LabelOrEmpty(subject)
                + "\npartner=" + (partner == null || partner == subject ? "(none)" : ExplorerPawns.LabelOrEmpty(partner))
                + "\nsummaryText=" + (req.summaryText ?? string.Empty)
                + "\nextraContext=" + (req.extraContext == null ? 0 : req.extraContext.Count) + " line(s)"
                + "\nenchantmentCandidates=" + (req.promptEnchantmentCandidates == null ? 0 : req.promptEnchantmentCandidates.Count)
                + "  replace=" + req.replacePromptEnchantments;
        }
    }
}
