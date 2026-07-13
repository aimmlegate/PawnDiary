// The catalog of PawnDiaryApi methods the explorer can drive. This is the heart of the window's
// left tree: each entry is one API call, with (a) its tree path, (b) a form drawer that renders the
// method's inputs from shared FormState, and (c) an invoke action that fires the API and appends
// the result to ExplorerState.Log.
//
// Design notes:
//   • One shared FormState object holds every form field. Only the fields the selected method uses
//     are drawn, but values persist across method switches so a tester can fill in eventKey once
//     and reuse it across Submit/Preview/Read. Sensible field initializers keep the forms useful
//     for quick smoke tests with minimal typing.
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
        public string sourceId = PawnDiaryExampleApi.SourceId;
        public string povRole = "initiator";
        public string maxCount = "5";

        // ExternalEventRequest (shared factual fields; each submission family owns its event key)
        public string eventKey = PawnDiaryExampleApi.ExampleEventKey;
        public string promptEntryEventKey = PawnDiaryExampleApi.PromptIdeaEventKey;
        public string directEntryEventKey = PawnDiaryExampleApi.DirectNoteEventKey;
        public string summaryText = "A quiet off-duty moment gave the pawn time to think before returning to colony work.";
        public string eventLabel = "quiet moment";
        public string extraContext = "origin=api_explorer\nmood_hint=calm\nweather=soft rain";
        public string promptFragment = "Keep the scene grounded in ordinary colony life and one private sensory detail.";
        public string enchantmentCandidates = "rain tapping at a window\nwarm lamplight after work\nbrief quiet before the next alarm";
        public int enchantmentMode = 1;                // 0 keep / 1 add / 2 replace
        public bool forceRecord = true;
        public string dedupKey = "api_explorer_quiet_moment";
        public string dedupTicks = "2500";

        // ExternalPromptEntryRequest (adds promptInstruction)
        public string promptInstruction = "Write a short diary entry about noticing a rare quiet pause during a hard colony day.";

        // ExternalDirectEntryRequest
        public string directText = "I sat by the window and watched the rain for a while.";
        public string directPartnerText = "They sat nearby without saying much, and somehow that made the room feel safer.";
        public string directTitle = "A Quiet Window";
        public string directPartnerTitle = "Sitting Nearby";
        public bool directGenerateTitle;

        // Style override
        public string styleSourceId = PawnDiaryExampleApi.SourceId;
        public string styleRule = "write in terse, clipped sentences";

        // Psychotype reads, editable layers, and external-generator demo
        public string psychotypeSourceId = PawnDiaryExampleApi.SourceId;
        public string psychotypeDefName = "DiaryPsychotype_Content";
        public string psychotypeRule = "PawnDiaryExampleAdapter.Default.PsychotypeRule".Translate().Resolve();
        public bool psychotypePin = true;

        // One-shot LLM completion request + the most recently returned poll handle
        public string llmLaneIndex = "-1";
        public string llmSystemPrompt = "PawnDiaryExampleAdapter.Default.LlmSystemPrompt".Translate().Resolve();
        public string llmUserText = "PawnDiaryExampleAdapter.Default.LlmUserText".Translate().Resolve();
        public string llmMaxTokens = "120";
        public string llmHandle = "0";

        // Read entry by id
        public string manualEventId = "paste_event_id_from_log";
        public string manualPovRole = "initiator";

        // DiaryEntryTitleQuery (shared by Read Pawn reads)
        public string qDomain = "External";
        public string qAtmosphereCue = "quiet";
        public string qPovRole = "initiator";
        public string qSourceId = PawnDiaryExampleApi.SourceId;
        public string qEventKey = PawnDiaryExampleApi.ExampleEventKey;
        public string qDateContains = "5500";
        public int qIncludeActive = 1;                 // 0 no / 1 yes
        public int qIncludeArchived = 1;
        public int qImportant;                          // 0 any / 1 not / 2 important
        public int qHasTitle;                           // tri-state UI index
        public int qHasGeneratedText;                  // tri-state UI index

        // Context bundle
        public bool bundleIncludeImportant = true;
        public bool bundleUseQuery;
        public bool bundleUseImportant;

        // Toggle for SetDiaryGenerationEnabled + GetPromptEnchantments
        public bool setGenEnabled = true;
        public bool enchantIncludeImportant = true;

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
                forceRecord = forceRecord,
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
                eventKey = promptEntryEventKey,
                subject = baseReq.subject,
                partner = baseReq.partner,
                summaryText = baseReq.summaryText,
                eventLabel = baseReq.eventLabel,
                extraContext = baseReq.extraContext,
                promptFragment = baseReq.promptFragment,
                promptEnchantmentCandidates = baseReq.promptEnchantmentCandidates,
                replacePromptEnchantments = baseReq.replacePromptEnchantments,
                forceRecord = baseReq.forceRecord,
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
                eventKey = directEntryEventKey,
                subject = subject,
                partner = (partner != null && partner != subject) ? partner : null,
                text = directText,
                partnerText = (partner != null && partner != subject) ? directPartnerText : string.Empty,
                title = directTitle,
                partnerTitle = (partner != null && partner != subject) ? directPartnerTitle : string.Empty,
                summaryText = summaryText,
                eventLabel = eventLabel,
                extraContext = ExplorerParsing.LinesFromMultiline(extraContext),
                forceRecord = forceRecord,
                dedupKey = dedupKey,
                dedupTicks = ExplorerParsing.ParseTick(dedupTicks, 0),
                generateTitleIfMissing = directGenerateTitle
            };
        }

        /// <summary>Builds the explorer's one-shot completion request from its string-backed fields.</summary>
        public ExternalLlmCompletionRequest BuildLlmCompletionRequest()
        {
            int laneIndex;
            if (!int.TryParse(llmLaneIndex, out laneIndex))
            {
                laneIndex = -1;
            }

            return new ExternalLlmCompletionRequest
            {
                sourceId = PawnDiaryExampleApi.SourceId,
                laneIndex = laneIndex,
                systemPrompt = llmSystemPrompt,
                userText = llmUserText,
                maxTokens = ExplorerParsing.ParsePositiveInt(llmMaxTokens, 120)
            };
        }

        /// <summary>Returns the positive completion handle currently typed in the shared form.</summary>
        public int LlmHandle => ExplorerParsing.ParsePositiveInt(llmHandle, 0);

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
            list.Add(Leaf("Readiness", "IsReady", "Checks whether Pawn Diary is loaded and ready before an adapter calls anything else.",
                (f, r) => { },
                f =>
                {
                    bool v = PawnDiaryExampleApi.IsReady;
                    ExplorerState.AppendLog("IsReady", SnapshotFormatter.FormatBool("IsReady", v),
                        SnapshotFormatter.FormatBool("IsReady", v));
                }));

            list.Add(Leaf("Readiness", "IsExternalApiEnabled", "Checks the player-controlled master switch for public integration calls.",
                (f, r) => { },
                f =>
                {
                    bool v = PawnDiaryExampleApi.IsExternalApiEnabled;
                    ExplorerState.AppendLog("IsExternalApiEnabled", SnapshotFormatter.FormatBool("IsExternalApiEnabled", v),
                        SnapshotFormatter.FormatBool("IsExternalApiEnabled", v));
                }));

            list.Add(Leaf("Readiness", "ApiVersion", "Shows the API version your adapter is talking to.",
                (f, r) => { },
                f =>
                {
                    int v = PawnDiaryExampleApi.ApiVersion;
                    string s = SnapshotFormatter.FormatApiVersion(v);
                    ExplorerState.AppendLog("ApiVersion", s, s);
                }));

            list.Add(Leaf("Readiness", "IsDiaryEligible(subject)", "Checks whether the selected pawn can receive diary entries.",
                (f, r) => { },
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryExampleApi.IsDiaryEligible(p);
                    string s = SnapshotFormatter.FormatBool("IsDiaryEligible", v);
                    ExplorerState.AppendLog("IsDiaryEligible", s, s + "\n(subject: " + ExplorerPawns.LabelOrEmpty(p) + ")");
                }));

            list.Add(Leaf("Readiness", "IsDiaryGenerationEnabled(subject)", "Reads the selected pawn's on/off switch for automatic diary generation.",
                (f, r) => { },
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryExampleApi.IsDiaryGenerationEnabled(p);
                    string s = SnapshotFormatter.FormatBool("IsDiaryGenerationEnabled", v);
                    ExplorerState.AppendLog("IsDiaryGenerationEnabled", s, s);
                }));

            list.Add(Leaf("Readiness", "SetDiaryGenerationEnabled(subject, bool)", "Turns generation on or off for the selected pawn; turning it on requeues pending work.",
                (f, r) => DrawToggleRow(r, "PawnDiaryExampleAdapter.SetEnabledToggle", ref f.setGenEnabled),
                f =>
                {
                    Pawn p = ExplorerState.ResolveSubjectPawn();
                    bool v = p != null && PawnDiaryExampleApi.SetDiaryGenerationEnabled(p, f.setGenEnabled);
                    string s = SnapshotFormatter.FormatBool("SetDiaryGenerationEnabled(→" + f.setGenEnabled + ")", v);
                    ExplorerState.AppendLog("SetDiaryGenerationEnabled", s, s);
                }));

            // ── CONNECTION (LLM API setup) ─────────────────────────────────────────────────────
            list.Add(Leaf("Connection", "GetApiSetup()",
                "Reads the player's current LLM API setup: routing mode, request knobs, and every configured lane.",
                (f, r) => { },
                f =>
                {
                    DiaryApiSetupSnapshot setup = PawnDiaryExampleApi.GetApiSetup();
                    string oneLine = setup == null
                        ? "null"
                        : "lanes=" + setup.laneCount + " active=" + setup.activeLaneCount + " routing=" + setup.routingMode;
                    ExplorerState.AppendLog("GetApiSetup", oneLine, SnapshotFormatter.Format(setup));
                }));

            list.Add(Leaf("Connection", "AddApiLane(demo)",
                "Adds a new ACTIVE demo lane (local, keyless) to Pawn Diary's settings. This edits real player settings — remove it in Pawn Diary → Connection when done. Run twice to see the duplicate guard.",
                (f, r) => { },
                f =>
                {
                    ExternalApiLaneRequest req = PawnDiaryExampleApi.BuildDemoApiLaneRequest();
                    AddApiLaneResult res = PawnDiaryExampleApi.AddApiLane(req);
                    string oneLine = "reason=" + res.reason + " index=" + res.index + " active=" + res.active;
                    ExplorerState.AppendLog("AddApiLane", oneLine, SnapshotFormatter.Format(res));
                }));

            // ── ONE-SHOT LLM COMPLETION ────────────────────────────────────────────────────────
            string completionCategory = "PawnDiaryExampleAdapter.Category.LlmCompletion".Translate();
            list.Add(Leaf(completionCategory, "RequestLlmCompletion(req)",
                "PawnDiaryExampleAdapter.Summary.RequestLlmCompletion".Translate(),
                DrawLlmCompletionRequestForm,
                f =>
                {
                    ExternalLlmCompletionRequest req = f.BuildLlmCompletionRequest();
                    int handle = PawnDiaryExampleApi.RequestLlmCompletion(req);
                    f.llmHandle = handle.ToString();
                    string oneLine = "handle=" + handle;
                    string detail = oneLine
                        + "\nlaneIndex=" + req.laneIndex
                        + "\nmaxTokens=" + req.maxTokens
                        + "\nsystemPrompt=" + req.systemPrompt
                        + "\nuserText=" + req.userText;
                    ExplorerState.AppendLog("RequestLlmCompletion", oneLine, detail);
                }));

            list.Add(Leaf(completionCategory, "GetLlmCompletionResult(handle)",
                "PawnDiaryExampleAdapter.Summary.GetLlmCompletionResult".Translate(),
                DrawLlmCompletionHandleForm,
                f =>
                {
                    int handle = f.LlmHandle;
                    LlmCompletionResult result = PawnDiaryExampleApi.GetLlmCompletionResult(handle);
                    string oneLine = "handle=" + handle + " status=" + result.status;
                    string detail = oneLine
                        + "\ntext=" + (result.text ?? string.Empty)
                        + "\nerror=" + (result.error ?? string.Empty);
                    ExplorerState.AppendLog("GetLlmCompletionResult", oneLine, detail);
                }));

            list.Add(Leaf(completionCategory, "CancelLlmCompletion(handle)",
                "PawnDiaryExampleAdapter.Summary.CancelLlmCompletion".Translate(),
                DrawLlmCompletionHandleForm,
                f =>
                {
                    int handle = f.LlmHandle;
                    bool cancelled = PawnDiaryExampleApi.CancelLlmCompletion(handle);
                    string oneLine = "handle=" + handle + " cancelled=" + cancelled;
                    ExplorerState.AppendLog("CancelLlmCompletion", oneLine, oneLine);
                }));

            // ── EVENTS (automatic-capture filters) ─────────────────────────────────────────────
            list.Add(Leaf("Events", "GetEventFilters()",
                "Lists the automatic-capture event filters (the settings Events tab), each with its current on/off state.",
                (f, r) => { },
                f =>
                {
                    List<DiaryEventFilterSnapshot> filters = PawnDiaryExampleApi.GetEventFilters();
                    string oneLine = "count=" + filters.Count;
                    ExplorerState.AppendLog("GetEventFilters", oneLine,
                        SnapshotFormatter.Format(filters as IReadOnlyList<DiaryEventFilterSnapshot>));
                }));

            list.Add(Leaf("Events", "SetEventFilterEnabled(toggle first)",
                "Flips the FIRST event filter's on/off state (demo of SetEventFilterEnabled — uses the same saved flag as the Events tab). Run twice to toggle it back.",
                (f, r) => { },
                f =>
                {
                    List<DiaryEventFilterSnapshot> filters = PawnDiaryExampleApi.GetEventFilters();
                    if (filters.Count == 0)
                    {
                        ExplorerState.AppendLog("SetEventFilterEnabled", "no filters",
                            "(no event filters available — master switch off, off-thread, or no group data)");
                        return;
                    }

                    DiaryEventFilterSnapshot first = filters[0];
                    bool target = !first.enabled;
                    bool ok = PawnDiaryExampleApi.SetEventFilterEnabled(first.key, target);
                    bool now = PawnDiaryExampleApi.IsEventFilterEnabled(first.key);
                    string oneLine = "key=" + first.key + " set→" + target + " ok=" + ok + " now=" + now;
                    ExplorerState.AppendLog("SetEventFilterEnabled", oneLine,
                        oneLine + "\nlabel=" + first.label + "  domain=" + first.domain);
                }));

            // ── SUBMIT EVENT ───────────────────────────────────────────────────────────────────
            list.Add(Leaf("Submit", "SubmitEvent(req)",
                "Submits a factual event and lets Pawn Diary decide whether to record and write it.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    bool recorded = PawnDiaryExampleApi.SubmitEvent(req, out SubmitEventOutcome outcome);
                    string oneLine = SnapshotFormatter.FormatSubmitOutcome(recorded, outcome);
                    ExplorerState.AppendLog("SubmitEvent", oneLine, oneLine + "\n\n" + req.EventRequestSummary(s, p));
                }));

            list.Add(Leaf("Submit", "SubmitEventWithHandle(req)",
                "Submits a factual event and returns handles you can poll or read later.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryExampleApi.SubmitEventWithHandle(req);
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
                "Previews the prompt Pawn Diary would send for this event without saving or spending tokens.",
                DrawEventRequestForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalEventRequest req = f.BuildEventRequest(s, p);
                    DiaryPromptPreviewSnapshot preview = PawnDiaryExampleApi.PreviewPrompt(req, ExplorerParsing.NormalizePovRole(f.povRole));
                    string oneLine = preview == null ? "preview = null" : "povRole=" + preview.povRole + " pairwise=" + preview.pairwise;
                    ExplorerState.AppendLog("PreviewPrompt(event)", oneLine, SnapshotFormatter.Format(preview));
                }));

            // ── SUBMIT PROMPT ENTRY ─────────────────────────────────────────────────────────────
            list.Add(Leaf("Prompt Entry", "SubmitPromptEntry(req)",
                "Gives Pawn Diary an instruction and lets it write the final entry with normal pawn context.",
                DrawPromptEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalPromptEntryRequest req = f.BuildPromptEntryRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryExampleApi.SubmitPromptEntry(req);
                    string oneLine = "recorded=" + result.recorded + (result.pairwise ? " pairwise" : string.Empty);
                    ExplorerState.AppendLog("SubmitPromptEntry", oneLine, SnapshotFormatter.Format(result));
                    if (result.recorded && result.primary != null)
                    {
                        ExplorerState.RememberHandle(result.primary, ExplorerPawns.LabelOrEmpty(s) + " (initiator)");
                    }
                }));

            list.Add(Leaf("Prompt Entry", "PreviewPrompt(promptEntryReq, povRole)",
                "Previews a prompt-entry request before spending tokens or saving an entry.",
                DrawPromptEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalPromptEntryRequest req = f.BuildPromptEntryRequest(s, p);
                    DiaryPromptPreviewSnapshot preview = PawnDiaryExampleApi.PreviewPrompt(req, ExplorerParsing.NormalizePovRole(f.povRole));
                    string oneLine = preview == null ? "preview = null" : "povRole=" + preview.povRole;
                    ExplorerState.AppendLog("PreviewPrompt(promptEntry)", oneLine, SnapshotFormatter.Format(preview));
                }));

            // ── SUBMIT DIRECT ENTRY ─────────────────────────────────────────────────────────────
            list.Add(Leaf("Direct Entry", "SubmitDirectEntry(req)",
                "Saves prose your adapter already wrote; Pawn Diary stores it without rewriting the body.",
                DrawDirectEntryForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    Pawn p = ExplorerState.ResolvePartnerPawn(s);
                    ExternalDirectEntryRequest req = f.BuildDirectRequest(s, p);
                    DiaryEventSubmissionResult result = PawnDiaryExampleApi.SubmitDirectEntry(req);
                    string oneLine = "recorded=" + result.recorded + (result.pairwise ? " pairwise" : string.Empty);
                    ExplorerState.AppendLog("SubmitDirectEntry", oneLine, SnapshotFormatter.Format(result));
                    if (result.recorded && result.primary != null)
                    {
                        ExplorerState.RememberHandle(result.primary, ExplorerPawns.LabelOrEmpty(s) + " (initiator)");
                    }
                }));

            // ── READ ENTRY ──────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Read Entry", "GetEntryStatus(handle)",
                "Checks whether a submitted handle is queued, writing, complete, failed, or missing.",
                DrawReadEntryByIdForm,
                f =>
                {
                    DiaryEntryHandle h = f.ResolveHandleForRead();
                    if (h == null)
                    {
                        ExplorerState.AppendLog("GetEntryStatus", "no handle", "Pick a remembered handle or type eventId+povRole.");
                        return;
                    }

                    DiaryEntryStatusSnapshot snap = PawnDiaryExampleApi.GetEntryStatus(h);
                    string oneLine = snap == null ? "null" : "status=" + snap.status + " title=" + (snap.titleComplete ? "yes" : "no");
                    ExplorerState.AppendLog("GetEntryStatus", oneLine, SnapshotFormatter.Format(snap));
                }));

            list.Add(Leaf("Read Entry", "GetEntrySnapshot(handle)",
                "Reads the completed entry text and metadata for a known handle.",
                DrawReadEntryByIdForm,
                f =>
                {
                    DiaryEntryHandle h = f.ResolveHandleForRead();
                    if (h == null)
                    {
                        ExplorerState.AppendLog("GetEntrySnapshot", "no handle", "Pick a remembered handle or type eventId+povRole.");
                        return;
                    }

                    DiaryEntrySnapshot snap = PawnDiaryExampleApi.GetEntrySnapshot(h);
                    string oneLine = snap == null ? "null" : "status=" + snap.status + " textLen=" + (snap.generatedText ?? string.Empty).Length;
                    ExplorerState.AppendLog("GetEntrySnapshot", oneLine, SnapshotFormatter.Format(snap));
                }));

            // ── READ PAWN ───────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Read Pawn", "GetRecentEntryTitles(pawn, n[, query])",
                "Lists recent completed diary titles for the selected pawn, optionally filtered.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetRecentEntryTitles", "no pawn", "(no eligible pawn)"); return; }
                    List<DiaryEntryTitleSnapshot> titles = f.applyPawnQuery
                        ? PawnDiaryExampleApi.GetRecentEntryTitles(s, f.MaxCount, f.BuildQuery())
                        : PawnDiaryExampleApi.GetRecentEntryTitles(s, f.MaxCount);
                    string oneLine = "count=" + titles.Count;
                    ExplorerState.AppendLog("GetRecentEntryTitles", oneLine, SnapshotFormatter.Format(titles as IReadOnlyList<DiaryEntryTitleSnapshot>));
                }));

            list.Add(Leaf("Read Pawn", "GetContextSnapshot(pawn, n[, query])",
                "Reads recent diary memories as short context items for another mod.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetContextSnapshot", "no pawn", "(no eligible pawn)"); return; }
                    DiaryContextSnapshot ctx = f.applyPawnQuery
                        ? PawnDiaryExampleApi.GetContextSnapshot(s, f.MaxCount, f.BuildQuery())
                        : PawnDiaryExampleApi.GetContextSnapshot(s, f.MaxCount);
                    string oneLine = ctx == null ? "null" : "entryCount=" + ctx.entryCount;
                    ExplorerState.AppendLog("GetContextSnapshot", oneLine, SnapshotFormatter.Format(ctx));
                }));

            list.Add(Leaf("Read Pawn", "GetEntryStats(pawn[, query])",
                "Counts diary entries for the selected pawn without loading full rows.",
                DrawReadPawnForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetEntryStats", "no pawn", "(no eligible pawn)"); return; }
                    DiaryEntryStatsSnapshot stats = f.applyPawnQuery
                        ? PawnDiaryExampleApi.GetEntryStats(s, f.BuildQuery())
                        : PawnDiaryExampleApi.GetEntryStats(s);
                    string oneLine = stats == null ? "null" : "total=" + stats.total;
                    ExplorerState.AppendLog("GetEntryStats", oneLine, SnapshotFormatter.Format(stats));
                }));

            // ── READ MACHINERY ──────────────────────────────────────────────────────────────────
            list.Add(Leaf("Machinery", "GetPawnSummary(pawn)",
                "Shows the pawn summary Pawn Diary would feed into a prompt.",
                (f, r) => { },
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetPawnSummary", "no pawn", "(no eligible pawn)"); return; }
                    DiaryPawnSummarySnapshot sum = PawnDiaryExampleApi.GetPawnSummary(s);
                    string oneLine = sum == null ? "null" : "sex=" + sum.sex + " mood=" + sum.mood;
                    ExplorerState.AppendLog("GetPawnSummary", oneLine, SnapshotFormatter.Format(sum));
                }));

            list.Add(Leaf("Machinery", "GetPromptEnchantments(pawn, includeImportant)",
                "Shows extra tone and context hints Pawn Diary may add to a prompt.",
                (f, r) => DrawCheckboxRow(r, "PawnDiaryExampleAdapter.IncludeImportantToggle", ref f.enchantIncludeImportant),
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetPromptEnchantments", "no pawn", "(no eligible pawn)"); return; }
                    List<DiaryPromptEnchantmentCandidateSnapshot> cs = PawnDiaryExampleApi.GetPromptEnchantments(s, f.enchantIncludeImportant);
                    string oneLine = "count=" + cs.Count;
                    ExplorerState.AppendLog("GetPromptEnchantments", oneLine, SnapshotFormatter.Format(cs as IReadOnlyList<DiaryPromptEnchantmentCandidateSnapshot>));
                }));

            list.Add(Leaf("Machinery", "GetContextBundle(pawn, n, query?, includeImportant?)",
                "Fetches the all-in-one adapter bundle: summary, style, hints, and recent memory.",
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
                        b = PawnDiaryExampleApi.GetContextBundle(s, f.MaxCount, f.BuildQuery(), f.bundleIncludeImportant);
                    }
                    else if (f.bundleUseImportant)
                    {
                        b = PawnDiaryExampleApi.GetContextBundle(s, f.MaxCount, f.bundleIncludeImportant);
                    }
                    else
                    {
                        b = PawnDiaryExampleApi.GetContextBundle(s, f.MaxCount);
                    }

                    string oneLine = b == null ? "null" : "summary=" + (b.pawnSummary != null ? "yes" : "no");
                    ExplorerState.AppendLog("GetContextBundle", oneLine, SnapshotFormatter.Format(b));
                }));

            list.Add(Leaf("Machinery", "GetWritingStyle(pawn)",
                "Reads the selected pawn's saved base writing style.",
                (f, r) => { },
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetWritingStyle", "no pawn", "(no eligible pawn)"); return; }
                    DiaryWritingStyleSnapshot style = PawnDiaryExampleApi.GetWritingStyle(s);
                    string oneLine = style == null ? "null" : "style=" + style.styleDefName;
                    ExplorerState.AppendLog("GetWritingStyle", oneLine, SnapshotFormatter.Format(style));
                }));

            list.Add(Leaf("Machinery", "GetAvailableWritingStyles()",
                "Lists writing styles available from XML and mod settings.",
                (f, r) => { },
                f =>
                {
                    List<DiaryWritingStyleSnapshot> styles = PawnDiaryExampleApi.GetAvailableWritingStyles();
                    string oneLine = "count=" + styles.Count;
                    ExplorerState.AppendLog("GetAvailableWritingStyles", oneLine, SnapshotFormatter.FormatStyles(styles as IReadOnlyList<DiaryWritingStyleSnapshot>));
                }));

            // ── STYLE OVERRIDE ──────────────────────────────────────────────────────────────────
            list.Add(Leaf("Style", "SetWritingStyleOverride(pawn, sourceId, rule)",
                "Temporarily forces a writing-style rule for this pawn from this adapter source.",
                DrawStyleOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("SetWritingStyleOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.SetWritingStyleOverride(s, f.styleSourceId, f.styleRule);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("SetWritingStyleOverride", oneLine, oneLine + "\nsourceId=" + f.styleSourceId + "\nrule=" + f.styleRule);
                }));

            list.Add(Leaf("Style", "ResetWritingStyleOverride(pawn, sourceId)",
                "Removes this adapter's writing-style override for the selected pawn.",
                DrawStyleOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("ResetWritingStyleOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.ResetWritingStyleOverride(s, f.styleSourceId);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("ResetWritingStyleOverride", oneLine, oneLine);
                }));

            // ── PSYCHOTYPE ──────────────────────────────────────────────────────────────────────
            string psychotypeCategory = "PawnDiaryExampleAdapter.Category.Psychotype".Translate();
            list.Add(Leaf(psychotypeCategory, "GetPsychotype(pawn)",
                "PawnDiaryExampleAdapter.Summary.GetPsychotype".Translate(),
                (f, r) => { },
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("GetPsychotype", "no pawn", "(no eligible pawn)"); return; }
                    DiaryPsychotypeSnapshot snap = PawnDiaryExampleApi.GetPsychotype(s);
                    string oneLine = snap == null ? "null" : "psychotype=" + snap.psychotypeDefName;
                    string detail = snap == null
                        ? "(null psychotype snapshot)"
                        : "DiaryPsychotypeSnapshot"
                            + "\n  psychotypeDefName = " + (snap.psychotypeDefName ?? string.Empty)
                            + "\n  label              = " + (snap.label ?? string.Empty)
                            + "\n  rule               = " + (snap.rule ?? string.Empty)
                            + "\n  savedCustomRule    = " + (snap.savedCustomRule ?? string.Empty);
                    ExplorerState.AppendLog("GetPsychotype", oneLine, detail);
                }));

            list.Add(Leaf(psychotypeCategory, "GetPsychotypeRule(defName)",
                "PawnDiaryExampleAdapter.Summary.GetPsychotypeRule".Translate(),
                DrawPsychotypeDefForm,
                f =>
                {
                    string rule = PawnDiaryExampleApi.GetPsychotypeRule(f.psychotypeDefName);
                    string oneLine = "defName=" + f.psychotypeDefName + " ruleLen=" + rule.Length;
                    ExplorerState.AppendLog("GetPsychotypeRule", oneLine, oneLine + "\nrule=" + rule);
                }));

            list.Add(Leaf(psychotypeCategory, "SetPsychotypeOverride(pawn, sourceId, rule)",
                "PawnDiaryExampleAdapter.Summary.SetPsychotypeOverride".Translate(),
                DrawPsychotypeOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("SetPsychotypeOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.SetPsychotypeOverride(s, f.psychotypeSourceId, f.psychotypeRule);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("SetPsychotypeOverride", oneLine,
                        oneLine + "\nsourceId=" + f.psychotypeSourceId + "\nrule=" + f.psychotypeRule);
                }));

            list.Add(Leaf(psychotypeCategory, "ResetPsychotypeOverride(pawn, sourceId)",
                "PawnDiaryExampleAdapter.Summary.ResetPsychotypeOverride".Translate(),
                DrawPsychotypeOverrideForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("ResetPsychotypeOverride", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.ResetPsychotypeOverride(s, f.psychotypeSourceId);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("ResetPsychotypeOverride", oneLine, oneLine);
                }));

            list.Add(Leaf(psychotypeCategory, "SetPsychotype(pawn, defName, pin)",
                "PawnDiaryExampleAdapter.Summary.SetPsychotype".Translate(),
                DrawPsychotypeDefForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("SetPsychotype", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.SetPsychotype(s, f.psychotypeDefName, f.psychotypePin);
                    string oneLine = "ok=" + ok + " pin=" + f.psychotypePin;
                    ExplorerState.AppendLog("SetPsychotype", oneLine,
                        oneLine + "\ndefName=" + f.psychotypeDefName);
                }));

            list.Add(Leaf(psychotypeCategory, "SetPsychotypeCustomRule(pawn, rule)",
                "PawnDiaryExampleAdapter.Summary.SetPsychotypeCustomRule".Translate(),
                DrawPsychotypeRuleForm,
                f =>
                {
                    Pawn s = ExplorerState.ResolveSubjectPawn();
                    if (s == null) { ExplorerState.AppendLog("SetPsychotypeCustomRule", "no pawn", "(no eligible pawn)"); return; }
                    bool ok = PawnDiaryExampleApi.SetPsychotypeCustomRule(s, f.psychotypeRule);
                    string oneLine = "ok=" + ok;
                    ExplorerState.AppendLog("SetPsychotypeCustomRule", oneLine,
                        oneLine + "\nrule=" + f.psychotypeRule);
                }));

            list.Add(Leaf(psychotypeCategory, "RegisterExternalPsychotypeGenerator(generator)",
                "PawnDiaryExampleAdapter.Summary.RegisterPsychotypeGenerator".Translate(),
                DrawPsychotypeOverrideForm,
                f =>
                {
                    string sourceId = f.psychotypeSourceId;
                    string generatedRule = f.psychotypeRule;
                    PawnDiaryExampleApi.RegisterExternalPsychotypeGenerator(new ExternalPsychotypeGenerator
                    {
                        sourceId = sourceId,
                        canReroll = pawn => pawn != null && PawnDiaryExampleApi.IsDiaryEligible(pawn),
                        isBusy = pawn => false,
                        reroll = pawn =>
                        {
                            bool saved = PawnDiaryExampleApi.SetPsychotypeOverride(pawn, sourceId, generatedRule);
                            string pawnLabel = ExplorerPawns.LabelOrEmpty(pawn);
                            string oneLine = "pawn=" + pawnLabel + " saved=" + saved;
                            ExplorerState.AppendLog("ExternalPsychotypeGenerator.reroll", oneLine,
                                oneLine + "\nsourceId=" + sourceId + "\nrule=" + generatedRule);
                        }
                    });
                    ExplorerState.AppendLog("RegisterExternalPsychotypeGenerator", "registered sourceId=" + sourceId,
                        "registered sourceId=" + sourceId
                        + "\nOpen the selected pawn's Psychotype Studio and use Regenerate to invoke the demo callback.");
                }));

            // ── HOOKS ───────────────────────────────────────────────────────────────────────────
            list.Add(Leaf("Hooks", "Activity log",
                "Shows recent status-listener events and context-provider calls from this example adapter.",
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
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey,
                "PawnDiaryExampleAdapter.Help.EventKey");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.Summary", ref f.summaryText,
                "PawnDiaryExampleAdapter.Help.Summary");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventLabel", ref f.eventLabel,
                "PawnDiaryExampleAdapter.Help.EventLabel");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext, 78f,
                "PawnDiaryExampleAdapter.Help.ExtraContext");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.PromptFragment", ref f.promptFragment, 64f,
                "PawnDiaryExampleAdapter.Help.PromptFragment");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.EnchantmentCandidates", ref f.enchantmentCandidates, 78f,
                "PawnDiaryExampleAdapter.Help.EnchantmentCandidates");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.EnchantmentMode", ref f.enchantmentMode,
                new[] { "keep", "add", "replace" }, "PawnDiaryExampleAdapter.Help.EnchantmentMode");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.ForceRecord", ref f.forceRecord,
                "PawnDiaryExampleAdapter.Help.ForceRecord");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DedupKey", ref f.dedupKey,
                "PawnDiaryExampleAdapter.Help.DedupKey");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DedupTicks", ref f.dedupTicks,
                "PawnDiaryExampleAdapter.Help.DedupTicks");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PovRole", ref f.povRole,
                "PawnDiaryExampleAdapter.Help.PovRole");
            list.End();
        }

        private static void DrawPromptEntryForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            // Prompt-entry reuses the event-request shape + adds promptInstruction on top.
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.PromptInstruction", ref f.promptInstruction, 72f,
                "PawnDiaryExampleAdapter.Help.PromptInstruction");
            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey,
                "PawnDiaryExampleAdapter.Help.EventKey");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.Summary", ref f.summaryText,
                "PawnDiaryExampleAdapter.Help.Summary");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext, 78f,
                "PawnDiaryExampleAdapter.Help.ExtraContext");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.ForceRecord", ref f.forceRecord,
                "PawnDiaryExampleAdapter.Help.ForceRecord");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PovRole", ref f.povRole,
                "PawnDiaryExampleAdapter.Help.PovRole");
            list.End();
        }

        private static void DrawDirectEntryForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.EventKey", ref f.eventKey,
                "PawnDiaryExampleAdapter.Help.EventKey");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.DirectText", ref f.directText, 96f,
                "PawnDiaryExampleAdapter.Help.DirectText");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.DirectPartnerText", ref f.directPartnerText, 78f,
                "PawnDiaryExampleAdapter.Help.DirectPartnerText");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectTitle", ref f.directTitle,
                "PawnDiaryExampleAdapter.Help.DirectTitle");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.DirectPartnerTitle", ref f.directPartnerTitle,
                "PawnDiaryExampleAdapter.Help.DirectPartnerTitle");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.DirectGenerateTitle", ref f.directGenerateTitle,
                "PawnDiaryExampleAdapter.Help.DirectGenerateTitle");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.ForceRecord", ref f.forceRecord,
                "PawnDiaryExampleAdapter.Help.ForceRecord");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.ExtraContext", ref f.extraContext, 78f,
                "PawnDiaryExampleAdapter.Help.ExtraContext");
            list.End();
        }

        private static void DrawReadEntryByIdForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.HelpLabel("PawnDiaryExampleAdapter.ReadEntry.HandleHint", "PawnDiaryExampleAdapter.Help.ReadEntryHandle");
            DrawRememberedHandlePicker(list, f);
            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ManualEventId", ref f.manualEventId,
                "PawnDiaryExampleAdapter.Help.ManualEventId");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.ManualPovRole", ref f.manualPovRole,
                "PawnDiaryExampleAdapter.Help.ManualPovRole");
            list.End();
        }

        private static void DrawReadPawnForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.MaxCount", ref f.maxCount,
                "PawnDiaryExampleAdapter.Help.MaxCount");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.ApplyQuery", ref f.applyPawnQuery,
                "PawnDiaryExampleAdapter.Help.ApplyQuery");
            if (!f.applyPawnQuery)
            {
                list.End();
                return;
            }

            list.GapLine();
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QDomain", ref f.qDomain,
                "PawnDiaryExampleAdapter.Help.QDomain");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QAtmosphereCue", ref f.qAtmosphereCue,
                "PawnDiaryExampleAdapter.Help.QAtmosphereCue");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QPovRole", ref f.qPovRole,
                "PawnDiaryExampleAdapter.Help.QPovRole");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QSourceId", ref f.qSourceId,
                "PawnDiaryExampleAdapter.Help.QSourceId");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QEventKey", ref f.qEventKey,
                "PawnDiaryExampleAdapter.Help.QEventKey");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.QDateContains", ref f.qDateContains,
                "PawnDiaryExampleAdapter.Help.QDateContains");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QImportant", ref f.qImportant, new[] { "any", "no", "yes" },
                "PawnDiaryExampleAdapter.Help.QImportant");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QHasTitle", ref f.qHasTitle, new[] { "any", "no", "yes" },
                "PawnDiaryExampleAdapter.Help.QHasTitle");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QHasGeneratedText", ref f.qHasGeneratedText, new[] { "any", "no", "yes" },
                "PawnDiaryExampleAdapter.Help.QHasGeneratedText");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QIncludeActive", ref f.qIncludeActive, new[] { "no", "yes" },
                "PawnDiaryExampleAdapter.Help.QIncludeActive");
            DrawEnumRow(list, "PawnDiaryExampleAdapter.Field.QIncludeArchived", ref f.qIncludeArchived, new[] { "no", "yes" },
                "PawnDiaryExampleAdapter.Help.QIncludeArchived");
            list.End();
        }

        private static void DrawContextBundleForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.MaxCount", ref f.maxCount,
                "PawnDiaryExampleAdapter.Help.MaxCount");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.BundleUseQuery", ref f.bundleUseQuery,
                "PawnDiaryExampleAdapter.Help.BundleUseQuery");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.BundleUseImportant", ref f.bundleUseImportant,
                "PawnDiaryExampleAdapter.Help.BundleUseImportant");
            if (f.bundleUseImportant)
            {
                DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.IncludeImportantToggle", ref f.bundleIncludeImportant,
                    "PawnDiaryExampleAdapter.Help.IncludeImportant");
            }

            list.End();
        }

        private static void DrawStyleOverrideForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.StyleSourceId", ref f.styleSourceId,
                "PawnDiaryExampleAdapter.Help.StyleSourceId");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.StyleRule", ref f.styleRule, 72f,
                "PawnDiaryExampleAdapter.Help.StyleRule");
            list.End();
        }

        private static void DrawPsychotypeDefForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PsychotypeDefName", ref f.psychotypeDefName,
                "PawnDiaryExampleAdapter.Help.PsychotypeDefName");
            DrawCheckboxEntry(list, "PawnDiaryExampleAdapter.Field.PsychotypePin", ref f.psychotypePin,
                "PawnDiaryExampleAdapter.Help.PsychotypePin");
            list.End();
        }

        private static void DrawPsychotypeOverrideForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.PsychotypeSourceId", ref f.psychotypeSourceId,
                "PawnDiaryExampleAdapter.Help.PsychotypeSourceId");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.PsychotypeRule", ref f.psychotypeRule, 88f,
                "PawnDiaryExampleAdapter.Help.PsychotypeRule");
            list.End();
        }

        private static void DrawPsychotypeRuleForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.PsychotypeRule", ref f.psychotypeRule, 96f,
                "PawnDiaryExampleAdapter.Help.PsychotypeRule");
            list.End();
        }

        private static void DrawLlmCompletionRequestForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.HelpLabel("PawnDiaryExampleAdapter.LlmCompletion.TokenWarning",
                "PawnDiaryExampleAdapter.Help.LlmCompletionTokenWarning");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.LlmLaneIndex", ref f.llmLaneIndex,
                "PawnDiaryExampleAdapter.Help.LlmLaneIndex");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.LlmSystemPrompt", ref f.llmSystemPrompt, 80f,
                "PawnDiaryExampleAdapter.Help.LlmSystemPrompt");
            list.TextAreaEntryLabeled("PawnDiaryExampleAdapter.Field.LlmUserText", ref f.llmUserText, 96f,
                "PawnDiaryExampleAdapter.Help.LlmUserText");
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.LlmMaxTokens", ref f.llmMaxTokens,
                "PawnDiaryExampleAdapter.Help.LlmMaxTokens");
            list.End();
        }

        private static void DrawLlmCompletionHandleForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.TextFieldEntryLabeled("PawnDiaryExampleAdapter.Field.LlmHandle", ref f.llmHandle,
                "PawnDiaryExampleAdapter.Help.LlmHandle");
            list.End();
        }

        private static void DrawHooksForm(FormState f, Rect r)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            list.HelpLabel("PawnDiaryExampleAdapter.Hooks.Hint", "PawnDiaryExampleAdapter.Help.Hooks");
            list.Label(ExplorerState.ActivitySummary());
            list.End();
        }

        // ── small widget helpers (the ones Listing_Standard doesn't have built in) ──────────

        private static void DrawToggleRow(Rect r, string labelKey, ref bool value)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            DrawCheckboxEntry(list, labelKey, ref value, "PawnDiaryExampleAdapter.Help.SetEnabled");
            list.End();
        }

        private static void DrawCheckboxRow(Rect r, string labelKey, ref bool value)
        {
            Listing_Standard list = new Listing_Standard();
            list.ColumnWidth = r.width;
            list.Begin(r);
            DrawCheckboxEntry(list, labelKey, ref value, "PawnDiaryExampleAdapter.Help.IncludeImportant");
            list.End();
        }

        private static void DrawCheckboxEntry(Listing_Standard list, string labelKey, ref bool value, string helpKey)
        {
            Rect row = list.GetRect(FormStateExtensions.CheckboxEntryHeight(labelKey, list.ColumnWidth));
            string label = labelKey.Translate();
            Rect labelRect = new Rect(row.x, row.y + 3f, row.width, row.height - 3f);
            Widgets.CheckboxLabeled(labelRect, label, ref value);
            if (!string.IsNullOrEmpty(helpKey))
            {
                FormStateExtensions.AttachHelpPopover(labelRect, helpKey);
            }
        }

        private static void DrawEnumRow(Listing_Standard list, string labelKey, ref int value, string[] options, string helpKey)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, list.ColumnWidth);
            float labelWidth = Mathf.Min(240f, Mathf.Max(150f, availableWidth * 0.38f));
            bool stacked = Text.CalcHeight(label, labelWidth) > 24f || availableWidth < 430f;
            float labelHeight = stacked ? Mathf.Max(22f, Text.CalcHeight(label, availableWidth)) : 30f;
            float rowHeight = FormStateExtensions.EnumEntryHeight(labelKey, availableWidth);

            Rect row = list.GetRect(rowHeight);
            Rect seg;
            if (stacked)
            {
                Rect labelRect = new Rect(row.x, row.y, row.width, labelHeight);
                Widgets.Label(labelRect, label);
                if (!string.IsNullOrEmpty(helpKey))
                {
                    FormStateExtensions.AttachHelpPopover(labelRect, helpKey);
                }
                seg = new Rect(row.x, row.y + labelHeight + 4f, row.width, 30f);
            }
            else
            {
                Rect labelRect = new Rect(row.x, row.y + 3f, labelWidth, row.height);
                Widgets.Label(labelRect, label);
                if (!string.IsNullOrEmpty(helpKey))
                {
                    FormStateExtensions.AttachHelpPopover(labelRect, helpKey);
                }
                seg = new Rect(row.x + labelWidth + 6f, row.y, row.width - labelWidth - 6f, row.height);
            }

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

        /// <summary>
        /// Mirrors the form draw helpers' row-height math so the window's scroll view cannot clip the
        /// last rows when labels wrap at narrower widths.
        /// </summary>
        public static float EstimateFormHeight(ExplorerMethodNode node, FormState f, float width)
        {
            if (node == null)
            {
                return 0f;
            }

            float w = Mathf.Max(180f, width);
            float h = 12f; // breathing room after the last row
            if (node.category == "Submit")
            {
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.EventKey", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.Summary", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.EventLabel", w);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.ExtraContext", w, 78f);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.PromptFragment", w, 64f);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.EnchantmentCandidates", w, 78f);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.EnchantmentMode", w);
                h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.ForceRecord", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.DedupKey", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.DedupTicks", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.PovRole", w);
                return h;
            }

            if (node.category == "Prompt Entry")
            {
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.PromptInstruction", w, 72f);
                h += 16f; // GapLine
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.EventKey", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.Summary", w);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.ExtraContext", w, 78f);
                h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.ForceRecord", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.PovRole", w);
                return h;
            }

            if (node.category == "Direct Entry")
            {
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.EventKey", w);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.DirectText", w, 96f);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.DirectPartnerText", w, 78f);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.DirectTitle", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.DirectPartnerTitle", w);
                h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.DirectGenerateTitle", w);
                h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.ForceRecord", w);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.ExtraContext", w, 78f);
                return h;
            }

            if (node.category == "Read Entry")
            {
                h += FormStateExtensions.LabelEntryHeight("PawnDiaryExampleAdapter.ReadEntry.HandleHint", w);
                h += 30f; // remembered handle picker
                h += 16f; // GapLine
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.ManualEventId", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.ManualPovRole", w);
                return h;
            }

            if (node.category == "Read Pawn")
            {
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.MaxCount", w);
                h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.ApplyQuery", w);
                if (f == null || !f.applyPawnQuery)
                {
                    return h;
                }

                h += 16f; // GapLine
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QDomain", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QAtmosphereCue", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QPovRole", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QSourceId", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QEventKey", w);
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.QDateContains", w);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.QImportant", w);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.QHasTitle", w);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.QHasGeneratedText", w);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.QIncludeActive", w);
                h += FormStateExtensions.EnumEntryHeight("PawnDiaryExampleAdapter.Field.QIncludeArchived", w);
                return h;
            }

            if (node.category == "Machinery")
            {
                if (node.label.StartsWith("GetPromptEnchantments"))
                {
                    return h + FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.IncludeImportantToggle", w);
                }

                if (node.label.StartsWith("GetContextBundle"))
                {
                    h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.MaxCount", w);
                    h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.BundleUseQuery", w);
                    h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.Field.BundleUseImportant", w);
                    if (f != null && f.bundleUseImportant)
                    {
                        h += FormStateExtensions.CheckboxEntryHeight("PawnDiaryExampleAdapter.IncludeImportantToggle", w);
                    }
                    return h;
                }

                return h + 30f;
            }

            if (node.category == "Style")
            {
                h += FormStateExtensions.TextFieldEntryHeight("PawnDiaryExampleAdapter.Field.StyleSourceId", w);
                h += FormStateExtensions.TextAreaEntryHeight("PawnDiaryExampleAdapter.Field.StyleRule", w, 72f);
                return h;
            }

            if (node.category == "Hooks")
            {
                h += FormStateExtensions.LabelEntryHeight("PawnDiaryExampleAdapter.Hooks.Hint", w);
                h += Text.CalcHeight(ExplorerState.ActivitySummary(), w) + 8f;
                return h;
            }

            return h + 30f;
        }
    }

    /// <summary>
    /// Extension methods that live next to FormState because they reference RimWorld types the
    /// FormState field bag itself avoids. Kept in this file so the form state bag stays plain.
    /// </summary>
    internal static class FormStateExtensions
    {
        private const float FieldHeight = 30f;
        private const float FieldGap = 6f;

        /// <summary>
        /// One labeled single-line text field row. Short labels use a compact two-column layout;
        /// longer labels switch to a stacked layout so translated/helpful labels never collide with
        /// the field beside them.
        /// </summary>
        public static void TextFieldEntryLabeled(this Listing_Standard list, string labelKey, ref string value, string helpKey = null)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, list.ColumnWidth);
            float labelWidth = Mathf.Min(240f, Mathf.Max(150f, availableWidth * 0.38f));
            bool stacked = Text.CalcHeight(label, labelWidth) > 24f || availableWidth < 430f;
            float labelHeight = stacked ? Mathf.Max(22f, Text.CalcHeight(label, availableWidth)) : FieldHeight;
            float rowHeight = TextFieldEntryHeight(labelKey, list.ColumnWidth);

            Rect row = list.GetRect(rowHeight);
            Rect field;
            if (stacked)
            {
                Rect labelRect = new Rect(row.x, row.y, row.width, labelHeight);
                Widgets.Label(labelRect, label);
                if (!string.IsNullOrEmpty(helpKey))
                {
                    AttachHelpPopover(labelRect, helpKey);
                }
                field = new Rect(row.x, row.y + labelHeight + FieldGap, row.width, FieldHeight);
            }
            else
            {
                Rect labelRect = new Rect(row.x, row.y + 3f, labelWidth, row.height);
                Widgets.Label(labelRect, label);
                if (!string.IsNullOrEmpty(helpKey))
                {
                    AttachHelpPopover(labelRect, helpKey);
                }
                field = new Rect(row.x + labelWidth + FieldGap, row.y, row.width - labelWidth - FieldGap, FieldHeight);
            }

            string current = value ?? string.Empty;
            string next = Widgets.TextField(field, current);
            if (next != current)
            {
                value = next;
            }
        }

        /// <summary>
        /// Labeled multiline text area for request fields where line breaks matter. It uses the full
        /// row width so long prose and key=value context are actually editable in the explorer.
        /// </summary>
        public static void TextAreaEntryLabeled(this Listing_Standard list, string labelKey, ref string value, float areaHeight, string helpKey = null)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, list.ColumnWidth);
            float labelHeight = Mathf.Max(22f, Text.CalcHeight(label, availableWidth));
            float fieldHeight = Mathf.Max(FieldHeight, areaHeight);
            Rect row = list.GetRect(TextAreaEntryHeight(labelKey, list.ColumnWidth, areaHeight));

            Rect labelRect = new Rect(row.x, row.y, row.width, labelHeight);
            Widgets.Label(labelRect, label);
            if (!string.IsNullOrEmpty(helpKey))
            {
                AttachHelpPopover(labelRect, helpKey);
            }

            Rect field = new Rect(row.x, row.y + labelHeight + FieldGap, row.width, fieldHeight);
            string current = value ?? string.Empty;
            string next = Widgets.TextArea(field, current, false);
            if (next != current)
            {
                value = next;
            }
        }

        public static void HelpLabel(this Listing_Standard list, string labelKey, string helpKey)
        {
            string label = labelKey.Translate();
            float rowHeight = LabelEntryHeight(labelKey, list.ColumnWidth);
            Rect row = list.GetRect(rowHeight);
            Rect labelRect = new Rect(row.x, row.y, row.width, rowHeight);
            Widgets.Label(labelRect, label);
            AttachHelpPopover(labelRect, helpKey);
        }

        public static float TextFieldEntryHeight(string labelKey, float width)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, width);
            float labelWidth = Mathf.Min(240f, Mathf.Max(150f, availableWidth * 0.38f));
            bool stacked = Text.CalcHeight(label, labelWidth) > 24f || availableWidth < 430f;
            float labelHeight = stacked ? Mathf.Max(22f, Text.CalcHeight(label, availableWidth)) : FieldHeight;
            return stacked ? labelHeight + FieldGap + FieldHeight : FieldHeight;
        }

        public static float TextAreaEntryHeight(string labelKey, float width, float areaHeight)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, width);
            float labelHeight = Mathf.Max(22f, Text.CalcHeight(label, availableWidth));
            return labelHeight + FieldGap + Mathf.Max(FieldHeight, areaHeight);
        }

        public static float EnumEntryHeight(string labelKey, float width)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, width);
            float labelWidth = Mathf.Min(240f, Mathf.Max(150f, availableWidth * 0.38f));
            bool stacked = Text.CalcHeight(label, labelWidth) > 24f || availableWidth < 430f;
            float labelHeight = stacked ? Mathf.Max(22f, Text.CalcHeight(label, availableWidth)) : 30f;
            return stacked ? labelHeight + 34f : 30f;
        }

        public static float CheckboxEntryHeight(string labelKey, float width)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, width);
            return Mathf.Max(30f, Text.CalcHeight(label, availableWidth) + 6f);
        }

        public static float LabelEntryHeight(string labelKey, float width)
        {
            string label = labelKey.Translate();
            float availableWidth = Mathf.Max(180f, width);
            return Mathf.Max(30f, Text.CalcHeight(label, availableWidth) + 6f);
        }

        public static void AttachHelpPopover(Rect rect, string helpKey)
        {
            TooltipHandler.TipRegion(rect, helpKey.Translate());
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
                + "\nforceRecord=" + req.forceRecord
                + "\nextraContext=" + (req.extraContext == null ? 0 : req.extraContext.Count) + " line(s)"
                + "\nenchantmentCandidates=" + (req.promptEnchantmentCandidates == null ? 0 : req.promptEnchantmentCandidates.Count)
                + "  replace=" + req.replacePromptEnchantments;
        }
    }
}
