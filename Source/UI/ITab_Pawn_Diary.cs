// The inspector tab (UI) that renders the selected pawn's finished diary entries.
// RimWorld calls FillTab() to draw it using immediate-mode GUI (the whole tab is re-emitted each
// frame). It reads entries via DiaryGameComponent.EntriesFor. See AGENTS.md ("lifecycle").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    // Inspector tab that shows the selected pawn's diary. Replaces the old standalone
    // Diary window/gizmo and is injected immediately after the vanilla Social tab at startup.
    public class ITab_Pawn_Diary : ITab
    {
        // Sentinel for no entries; avoids allocating a new empty list on every frame when the component is null.
        private static readonly IReadOnlyList<DiaryEntryView> EmptyList = new List<DiaryEntryView>();

        // Vertical space constants for the per-pawn controls above the scroll view. The actual
        // height is dynamic because dev-mode controls are hidden during normal play.
        private const float ControlLineHeight = 28f;
        private const float ControlGap = 2f;
        private const float EntryTitleHeight = 28f;
        private const float EntryTextTop = 38f;
        private const float EntryBottomPadding = 10f;
        private const float StatusBadgeWidth = 34f;
        private const float StatusBadgeHeight = 24f;
        private const float StatusBadgeRightPadding = 24f;
        private const float RoleplayLineGap = 3f;
        private const float RoleplayParagraphGap = 6f;
        private const float LinkedEntryPadding = 8f;
        private const float LinkedEntryLabelHeight = 20f;
        private const float LinkedEntryTextHeight = 36f;
        private const float LinkedEntryTotalHeight = 64f;
        private const float ModelNameTopPadding = 4f;
        private const float ModelNameHeight = 20f;
        private const float DebugTextTopPadding = 8f;
        private const float EntryAccentWidth = 6f;
        private const float EntryLabelMaxWidth = 148f;
        private const float EntryFadeDurationSeconds = 0.55f;
        private const float TitleFadeDurationSeconds = 0.8f;
        private const float WritingDotSize = 4f;
        private const float WritingDotGap = 5f;

        private static readonly Color QuietColor = new Color(0.42f, 0.48f, 0.52f);
        private static readonly Color NarrativeColor = new Color(0.78f, 0.78f, 0.72f);
        private static readonly Color FallbackDialogueColor = new Color(0.58f, 0.80f, 1f);
        private static readonly Color LinkedEntryBgColor = new Color(0.15f, 0.17f, 0.20f, 0.85f);
        private static readonly Color LinkedEntryBorderColor = new Color(0.35f, 0.45f, 0.55f);
        private static readonly Color LinkedEntryTextColor = new Color(0.65f, 0.70f, 0.75f);
        private static readonly Color LinkedEntryHoverColor = new Color(0.25f, 0.30f, 0.38f, 0.90f);
        // Pleasant, readable accents for group-coded diary cards. The group label selects the color
        // deterministically, so old entries get the same marker every time without adding save data.
        private static readonly Color[] EntryAccentPalette =
        {
            new Color(0.95f, 0.58f, 0.32f),
            new Color(0.84f, 0.70f, 0.34f),
            new Color(0.48f, 0.72f, 0.50f),
            new Color(0.38f, 0.70f, 0.72f),
            new Color(0.45f, 0.63f, 0.92f),
            new Color(0.70f, 0.56f, 0.88f),
            new Color(0.86f, 0.50f, 0.66f),
            new Color(0.62f, 0.68f, 0.76f)
        };

        // Cached roleplay text styles, reused every frame to avoid allocating a fresh GUIStyle per
        // line. Built once from the active font, then refreshed (font size + color) on each use so
        // they still track UI-scale changes. Shared by the measure pass and the draw pass.
        private static GUIStyle dialogueStyle;
        private static GUIStyle narrativeStyle;
        private static readonly Dictionary<string, float> EntryFirstSeenSeconds = new Dictionary<string, float>();

        // Unity scroll position; persists across frames so the user's scroll offset isn't lost on redraw.
        private Vector2 scrollPosition;
        // Set by the Social-tab click patch before opening this tab. FillTab consumes it once
        // the relevant pawn's generated entry list is available.
        private static string pendingScrollPawnId;
        private static string pendingScrollEventId;

        public ITab_Pawn_Diary()
        {
            size = new Vector2(600f, 500f);
            labelKey = "PawnDiaryTabLabel";
        }

        /// <summary>
        /// Requests that the next Diary tab draw for this pawn scroll to the given event card.
        /// Used by the Social-tab play-log click patch.
        /// </summary>
        public static void RequestScrollToEntry(Pawn pawn, string eventId)
        {
            pendingScrollPawnId = pawn?.GetUniqueLoadID();
            pendingScrollEventId = eventId;
        }

        /// <summary>
        /// Clears a pending scroll request when the tab could not be opened after all.
        /// </summary>
        public static void ClearPendingScrollRequest()
        {
            pendingScrollPawnId = null;
            pendingScrollEventId = null;
        }

        /// <summary>
        /// Only show the diary tab for colonist pawns (or corpses of colonists).
        /// </summary>
        public override bool IsVisible
        {
            get { return PawnToShow() != null; }
        }

        /// <summary>
        /// Resolves the pawn to display a diary for, handling both selected colonists
        /// and selected corpses of colonists.
        /// </summary>
        private Pawn PawnToShow()
        {
            Pawn pawn = SelPawn;
            if (pawn == null)
            {
                Corpse corpse = SelThing as Corpse;
                pawn = corpse?.InnerPawn;
            }

            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike || !pawn.IsColonist)
            {
                return null;
            }

            return pawn;
        }

        /// <summary>
        /// RimWorld's immediate-mode draw callback for the entire tab content.
        /// Called every frame; the whole UI is rebuilt from scratch each time.
        /// </summary>
        protected override void FillTab()
        {
            Pawn pawn = PawnToShow();
            if (pawn == null)
            {
                return;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            // Singleton component that owns all diary state for the current game.
            DiaryGameComponent component = DiaryGameComponent.Current;

            IReadOnlyList<DiaryEntryView> entries = component?.EntriesFor(pawn) ?? EmptyList;
            int generatingCount = entries.Count(IsGenerating);
            bool showLlmDebugInfo = ShouldShowLlmDebugInfo();
            // Dev-mode-only: when on, reveal in-progress/stuck entries in the list (the full debug
            // toggle already shows them, so this only matters when debug info is off).
            bool showGeneratingEntries = ShouldShowGeneratingEntries();

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 34f);
            if (generatingCount > 0)
            {
                Rect statusRect = new Rect(
                    rect.xMax - StatusBadgeRightPadding - StatusBadgeWidth,
                    rect.y + 3f,
                    StatusBadgeWidth,
                    StatusBadgeHeight);
                headerRect.width = Mathf.Max(0f, statusRect.x - rect.x - 8f);
                DrawWritingIndicator(statusRect);
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "PawnDiary.Tab.DiaryHeader".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;

            // In normal play the header stands alone; the dev-only controls (and the space they
            // need) appear only when RimWorld dev mode is on. PawnControlsHeight() returns 0 outside
            // dev mode, so entries sit directly under the header.
            float controlsY = rect.y + 36f;
            float controlsHeight = PawnControlsHeight();
            if (Prefs.DevMode)
            {
                Rect controlsRect = new Rect(rect.x, controlsY, rect.width, controlsHeight);
                DrawPawnControls(pawn, component, controlsRect);
            }

            // The controls are part of the diary tab, so reserve fixed space before the scroll view.
            float entriesY = controlsY + controlsHeight + 8f;
            Rect outRect = new Rect(rect.x, entriesY, rect.width, rect.yMax - entriesY);

            // Production view: show only completed LLM output. Dev mode can reveal raw/pending
            // rows plus the diagnostic prompt/status block for troubleshooting.
            List<DiaryEntryView> ordered = entries
                .Where(entry => showLlmDebugInfo || IsGenerated(entry) || (showGeneratingEntries && IsGenerating(entry)))
                .OrderByDescending(entry => entry.Tick)
                .ToList();

            if (ordered.Count == 0)
            {
                Widgets.Label(outRect, (showLlmDebugInfo ? "PawnDiary.Tab.NoEntries" : "PawnDiary.Tab.NoGeneratedEntries").Translate());
                return;
            }

            // Subtract 16f to leave room for the scrollbar grip inside the scroll view.
            float viewWidth = outRect.width - 16f;
            // Measure each card once per frame and reuse the result for the scroll-height sum, the
            // pending-scroll jump, and the draw loop. Wrapping height is otherwise recomputed two or
            // three times per entry every frame.
            float[] heights = new float[ordered.Count];
            float viewHeight = 12f; // includes 12f bottom padding
            for (int i = 0; i < ordered.Count; i++)
            {
                heights[i] = EntryHeight(ordered[i], viewWidth, showLlmDebugInfo);
                viewHeight += heights[i];
            }
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            TryApplyPendingScroll(pawn, ordered, heights, viewHeight, outRect.height);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;
            Color dialogueColor = PreferredDialogueColor(pawn);
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                float height = heights[i];
                Rect entryRect = new Rect(0f, curY, viewRect.width, height);

                Color accentColor = EntryAccentColor(entry);
                Widgets.DrawMenuSection(entryRect);
                Widgets.DrawHighlightIfMouseover(entryRect);

                Rect titleRect = new Rect(entryRect.x, entryRect.y, entryRect.width, EntryTitleHeight);
                Widgets.DrawTitleBG(titleRect);
                Widgets.DrawBoxSolid(new Rect(entryRect.x + 1f, entryRect.y + 1f, EntryAccentWidth, entryRect.height - 2f), accentColor);

                Rect groupRect = GroupLabelRect(titleRect, entry.GroupLabel);
                if (groupRect.width > 0f)
                {
                    DrawGroupLabel(groupRect, entry.GroupLabel, accentColor);
                }
                float headerRight = groupRect.width > 0f ? groupRect.x - 6f : entryRect.xMax - 8f;
                DrawEntryHeader(
                    new Rect(entryRect.x + 14f, entryRect.y + 5f, Mathf.Max(80f, headerRight - entryRect.x - 14f), 22f),
                    entry,
                    accentColor);

                // Linked entry for the OTHER pawn rendered BEFORE main text when this pawn is the
                // recipient (shows the initiator's perspective first). When this pawn is the
                // initiator, the linked recipient entry goes AFTER the main text instead.
                float textY = entryRect.y + EntryTextTop;
                LinkedEntryView linked = entry.LinkedEntry;
                bool linkedBefore = linked != null && DiaryEvent.RoleEquals(entry.PovRole, DiaryEvent.RecipientRole);
                bool linkedAfter = linked != null && !linkedBefore;
                bool showModelName = HasModelName(entry);
                string bodyText = EntryBodyText(entry, showLlmDebugInfo);
                string debugText = showLlmDebugInfo ? entry.DebugText : string.Empty;
                float innerTextWidth = entryRect.width - 20f;
                float mainTextHeight = RoleplayTextHeight(bodyText, innerTextWidth);
                float debugTextHeight = DebugTextHeight(debugText, innerTextWidth);

                if (linkedBefore)
                {
                    Rect linkedRect = new Rect(entryRect.x + 10f, textY, entryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, pawn);
                    textY = linkedRect.yMax + LinkedEntryPadding;
                }

                Rect textRect = new Rect(entryRect.x + 12f, textY, entryRect.width - 20f, mainTextHeight);
                if (IsGenerating(entry))
                {
                    DrawWritingPlaceholder(textRect);
                }
                else
                {
                    DrawRoleplayText(textRect, bodyText, dialogueColor, EntryTextAlpha(entry));
                }
                float afterTextY = textRect.yMax;

                if (linkedAfter)
                {
                    Rect linkedRect = new Rect(entryRect.x + 10f, afterTextY + LinkedEntryPadding, entryRect.width - 20f, LinkedEntryTotalHeight);
                    DrawLinkedEntry(linked, linkedRect, pawn);
                    afterTextY = linkedRect.yMax;
                }

                if (debugTextHeight > 0f)
                {
                    Rect debugRect = new Rect(entryRect.x + 12f, afterTextY + DebugTextTopPadding, entryRect.width - 20f, debugTextHeight);
                    DrawDebugText(debugRect, debugText);
                }

                if (showModelName)
                {
                    Rect modelRect = new Rect(entryRect.x + 12f, entryRect.yMax - EntryBottomPadding - ModelNameHeight, entryRect.width - 24f, ModelNameHeight);
                    DrawModelName(modelRect, entry.LlmModel);
                }

                curY += height + 8f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Consumes a pending Social-tab jump request by placing the target card near the top
        /// of the scroll view. If the entry disappeared before redraw, clear the stale request.
        /// </summary>
        private void TryApplyPendingScroll(Pawn pawn, List<DiaryEntryView> ordered, float[] heights, float viewHeight, float outHeight)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(pendingScrollPawnId) || string.IsNullOrWhiteSpace(pendingScrollEventId))
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (pawnId != pendingScrollPawnId)
            {
                return;
            }

            float curY = 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                DiaryEntryView entry = ordered[i];
                if (entry != null && entry.EventId == pendingScrollEventId)
                {
                    float maxScroll = Mathf.Max(0f, viewHeight - outHeight);
                    scrollPosition.y = Mathf.Clamp(curY, 0f, maxScroll);
                    ClearPendingScrollRequest();
                    return;
                }

                curY += heights[i] + 8f;
            }

            ClearPendingScrollRequest();
        }

        /// <summary>
        /// Returns the height needed for the per-pawn controls above the diary list.
        /// Dev-only rows are omitted in normal play, keeping the tab focused on entries.
        /// </summary>
        private static float PawnControlsHeight()
        {
            if (!Prefs.DevMode)
            {
                return 0f;
            }

            float lines = 1f; // generation toggle
            if (PawnDiaryMod.Settings != null)
            {
                lines += 3f; // dev toggles: persona controls, LLM diagnostics, show generating
                if (ShouldShowPersonaSettings())
                {
                    lines += 1f; // persona picker
                }
            }

            return lines * ControlLineHeight + (lines - 1f) * ControlGap;
        }

        /// <summary>
        /// Renders the per-pawn generation toggle plus dev-mode-only troubleshooting controls.
        /// </summary>
        private static void DrawPawnControls(Pawn pawn, DiaryGameComponent component, Rect rect)
        {
            if (pawn == null || component == null)
            {
                return;
            }

            if (!Prefs.DevMode)
            {
                return;
            }

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            // Toggling this only gates future LLM requests. Recorded events remain visible as raw
            // diary entries, which lets players pause generation without losing history.
            bool enabled = component.DiaryGenerationEnabledFor(pawn);
            bool before = enabled;
            listing.CheckboxLabeled(
                "PawnDiary.Tab.GenerateForPawn".Translate(),
                ref enabled,
                "PawnDiary.Tab.GenerateForPawnTip".Translate());
            if (enabled != before)
            {
                component.SetDiaryGenerationEnabled(pawn, enabled);
            }

            PawnDiarySettings settings = PawnDiaryMod.Settings;
            bool writeGlobalSettings = false;
            if (Prefs.DevMode && settings != null)
            {
                bool showPersonaSettings = settings.showPersonaSettings;
                bool showPersonaBefore = showPersonaSettings;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowPersonaSettings".Translate(),
                    ref showPersonaSettings,
                    "PawnDiary.Tab.ShowPersonaSettingsTip".Translate());
                if (showPersonaSettings != showPersonaBefore)
                {
                    settings.showPersonaSettings = showPersonaSettings;
                    writeGlobalSettings = true;
                }

                bool showLlmDebugInfo = settings.showLlmDebugInfo;
                bool showDebugBefore = showLlmDebugInfo;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowLlmDebugInfo".Translate(),
                    ref showLlmDebugInfo,
                    "PawnDiary.Tab.ShowLlmDebugInfoTip".Translate());
                if (showLlmDebugInfo != showDebugBefore)
                {
                    settings.showLlmDebugInfo = showLlmDebugInfo;
                    writeGlobalSettings = true;
                }

                bool showGeneratingEntries = settings.showGeneratingEntries;
                bool showGeneratingBefore = showGeneratingEntries;
                listing.CheckboxLabeled(
                    "PawnDiary.Tab.ShowGeneratingEntries".Translate(),
                    ref showGeneratingEntries,
                    "PawnDiary.Tab.ShowGeneratingEntriesTip".Translate());
                if (showGeneratingEntries != showGeneratingBefore)
                {
                    settings.showGeneratingEntries = showGeneratingEntries;
                    writeGlobalSettings = true;
                }
            }

            // Persona picker. Options come from DiaryPersonaDefs.xml so new presets can be added
            // without touching UI code; the choice is saved per pawn and used for future generations.
            if (ShouldShowPersonaSettings())
            {
                DiaryPersonaDef persona = component.PersonaFor(pawn);
                if (listing.ButtonText("PawnDiary.Tab.PersonaButton".Translate(PersonaLabel(persona))))
                {
                    List<FloatMenuOption> options = DiaryPersonas.All
                        .OrderBy(PersonaLabel)
                        .Select(option =>
                        {
                            DiaryPersonaDef selected = option;
                            return new FloatMenuOption(PersonaLabel(selected), delegate
                            {
                                component.SetPersona(pawn, selected.defName);
                            });
                        })
                        .ToList();

                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            listing.End();

            if (writeGlobalSettings)
            {
                WriteGlobalSettings();
            }
        }

        /// <summary>
        /// Returns the human-readable label for a persona, falling back to "default" if null
        /// or to defName if the label is blank.
        /// </summary>
        private static string PersonaLabel(DiaryPersonaDef persona)
        {
            if (persona == null)
            {
                return "PawnDiary.Persona.DefaultLabel".Translate();
            }

            return string.IsNullOrWhiteSpace(persona.label) ? persona.defName : persona.label;
        }

        /// <summary>
        /// Draws compact animated dots while hidden pending entries are being written.
        /// </summary>
        private static void DrawWritingIndicator(Rect rect)
        {
            DrawWritingDots(
                new Rect(rect.x + rect.width * 0.5f - 10f, rect.y + rect.height * 0.5f - 2f, 24f, 8f),
                new Color(0.78f, 0.95f, 0.78f),
                0f);
        }

        /// <summary>
        /// True when an entry has actual LLM output ready for the production diary list.
        /// </summary>
        private static bool IsGenerated(DiaryEntryView entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(entry.GeneratedText);
        }

        /// <summary>
        /// Dev-mode preference gate for manual persona editing in the Diary tab.
        /// </summary>
        private static bool ShouldShowPersonaSettings()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showPersonaSettings;
        }

        /// <summary>
        /// Dev-mode preference gate for raw/pending entries and the LLM prompt/status block.
        /// </summary>
        private static bool ShouldShowLlmDebugInfo()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showLlmDebugInfo;
        }

        /// <summary>
        /// Dev-mode preference gate for revealing entries still in the LLM generation pipeline
        /// (in-progress or stuck), without the full prompt/status diagnostic block.
        /// </summary>
        private static bool ShouldShowGeneratingEntries()
        {
            return Prefs.DevMode && PawnDiaryMod.Settings != null && PawnDiaryMod.Settings.showGeneratingEntries;
        }

        /// <summary>
        /// Persists global mod UI preferences changed from this pawn tab.
        /// </summary>
        private static void WriteGlobalSettings()
        {
            PawnDiaryMod mod = LoadedModManager.GetMod<PawnDiaryMod>();
            if (mod != null)
            {
                mod.WriteSettings();
            }
        }

        /// <summary>
        /// Returns the entry body text for the current view mode: polished generated output in
        /// normal play, or the existing generated/raw/status fallback when debug info is enabled.
        /// </summary>
        private static string EntryBodyText(DiaryEntryView entry, bool showLlmDebugInfo)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            // Generating entries (revealed by the debug toggle OR the dev-only "show generating"
            // toggle) use DisplayText so an in-progress card shows the "writing..." placeholder /
            // raw text instead of rendering blank. Both the measure and draw passes call this, so
            // card height stays consistent.
            if (showLlmDebugInfo || IsGenerating(entry))
            {
                return entry.DisplayText;
            }

            return entry.GeneratedText;
        }

        /// <summary>
        /// True while an entry is still waiting on the background LLM generation pipeline.
        /// </summary>
        private static bool IsGenerating(DiaryEntryView entry)
        {
            return entry != null && entry.LlmStatus == DiaryEvent.PendingStatus;
        }

        /// <summary>
        /// Title follow-ups run after the main entry succeeds, so a completed diary page can still
        /// be waiting on its short header title. This flag drives the small header-only animation.
        /// </summary>
        private static bool IsTitleGenerating(DiaryEntryView entry)
        {
            return entry != null
                && TitlesEnabled()
                && entry.TitlePending
                && string.IsNullOrWhiteSpace(entry.Title);
        }

        /// <summary>
        /// True when an entry has a model id worth showing as a quiet provenance hint.
        /// </summary>
        private static bool HasModelName(DiaryEntryView entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(entry.LlmModel);
        }

        /// <summary>
        /// Produces the compact header shown on each entry card: the date, then a stored
        /// LLM-generated subject ("date — title") when title display is enabled and one exists.
        /// Otherwise entries show just the date — no dangling separator.
        /// </summary>
        private static string EntryHeader(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (!TitlesEnabled() || string.IsNullOrWhiteSpace(entry.Title))
            {
                return entry.Date ?? string.Empty;
            }

            return (entry.Date ?? string.Empty) + " \u2014 " + entry.Title;
        }

        /// <summary>
        /// Draws the date/title line. Finished titles get a short fade and a soft color pulse;
        /// pending title follow-ups keep the date visible and animate a tiny placeholder where the
        /// subject will appear.
        /// </summary>
        private static void DrawEntryHeader(Rect rect, DiaryEntryView entry, Color accent)
        {
            if (IsTitleGenerating(entry))
            {
                DrawPendingTitleHeader(rect, entry, accent);
                return;
            }

            string header = EntryHeader(entry);
            if (string.IsNullOrWhiteSpace(header))
            {
                return;
            }

            bool hasTitle = entry != null && TitlesEnabled() && !string.IsNullOrWhiteSpace(entry.Title);
            Color oldColor = GUI.color;
            if (hasTitle)
            {
                float age = Time.realtimeSinceStartup - TitleFirstSeenAt(entry);
                float alpha = Mathf.Clamp01(age / TitleFadeDurationSeconds);
                float pulse = Mathf.Lerp(0.22f, 0.38f, WritingPulse(1.4f));
                Color titleColor = Color.Lerp(new Color(0.86f, 0.86f, 0.86f), accent, pulse);
                titleColor.a = alpha;
                GUI.color = titleColor;
            }
            else
            {
                GUI.color = new Color(0.86f, 0.86f, 0.86f);
            }

            Widgets.LabelFit(rect, header);
            GUI.color = oldColor;
        }

        /// <summary>
        /// Keeps the completed date readable while the cheaper title follow-up is still in flight.
        /// The animated dots occupy the future title slot so the card still feels active.
        /// </summary>
        private static void DrawPendingTitleHeader(Rect rect, DiaryEntryView entry, Color accent)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            const float dotsWidth = WritingDotSize * 3f + WritingDotGap * 2f;
            string prefix = string.IsNullOrWhiteSpace(entry?.Date)
                ? string.Empty
                : (entry.Date + " \u2014");

            Color oldColor = GUI.color;
            float dotsX = rect.x;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                float prefixWidth = Mathf.Max(0f, rect.width - dotsWidth - 10f);
                if (prefixWidth > 0f)
                {
                    Rect prefixRect = new Rect(rect.x, rect.y, prefixWidth, rect.height);
                    GUI.color = new Color(0.86f, 0.86f, 0.86f, 0.95f);
                    Widgets.LabelFit(prefixRect, prefix);
                    dotsX = prefixRect.xMax + 6f;
                }
            }

            if (dotsX + dotsWidth > rect.xMax)
            {
                dotsX = Mathf.Max(rect.x, rect.xMax - dotsWidth);
            }

            Color dotColor = Color.Lerp(new Color(0.68f, 0.72f, 0.76f), accent, 0.45f);
            Rect dotsRect = new Rect(dotsX, rect.y + rect.height * 0.5f - 3f, dotsWidth, 8f);
            DrawWritingDots(dotsRect, dotColor, 1.1f);
            GUI.color = oldColor;
        }

        /// <summary>
        /// The title-generation setting doubles as the display toggle: disabling it means no
        /// titles in card headers, even if older entries already have stored titles.
        /// </summary>
        private static bool TitlesEnabled()
        {
            return PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.generateTitles;
        }

        /// <summary>
        /// Returns the color strip used to mark the entry group. Important groups keep the palette
        /// color bright; quieter groups use a softer version so the diary stays calm at a glance.
        /// </summary>
        private static Color EntryAccentColor(DiaryEntryView entry)
        {
            Color accent = PaletteColor(entry?.GroupLabel);
            if (entry == null || entry.Important)
            {
                return accent;
            }

            return Color.Lerp(QuietColor, accent, 0.42f);
        }

        /// <summary>
        /// Picks a stable palette color from a localized group label without relying on runtime
        /// string hash behavior. New to hashing? It turns text into a repeatable number.
        /// </summary>
        private static Color PaletteColor(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return EntryAccentPalette[EntryAccentPalette.Length - 1];
            }

            int hash = 17;
            for (int i = 0; i < label.Length; i++)
            {
                hash = hash * 31 + char.ToLowerInvariant(label[i]);
            }

            if (hash < 0)
            {
                hash = ~hash;
            }

            return EntryAccentPalette[hash % EntryAccentPalette.Length];
        }

        /// <summary>
        /// Reserves a small right-side label for the event group, leaving the date/title room to
        /// shrink gracefully on narrow tabs.
        /// </summary>
        private static Rect GroupLabelRect(Rect titleRect, string groupLabel)
        {
            if (string.IsNullOrWhiteSpace(groupLabel) || titleRect.width < 240f)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float width = Mathf.Min(EntryLabelMaxWidth, Text.CalcSize(groupLabel).x + 18f);
            Text.Font = oldFont;

            return new Rect(titleRect.xMax - width - 8f, titleRect.y + 5f, width, 18f);
        }

        /// <summary>
        /// Draws the color-coded group name as a quiet chip in the card header.
        /// </summary>
        private static void DrawGroupLabel(Rect rect, string label, Color accent)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(accent.r * 0.23f, accent.g * 0.23f, accent.b * 0.23f, 0.72f),
                new Color(accent.r, accent.g, accent.b, 0.92f),
                1);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.Lerp(accent, Color.white, 0.55f);
            Widgets.LabelFit(new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, rect.height - 2f), label);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// New cards fade their text in the first time this tab sees the finished entry. The key
        /// includes the status so a row seen as "writing" can still animate when the page completes.
        /// </summary>
        private static float EntryTextAlpha(DiaryEntryView entry)
        {
            if (entry == null)
            {
                return 1f;
            }

            float firstSeen = EntryFirstSeenAt(entry);
            return Mathf.Clamp01((Time.realtimeSinceStartup - firstSeen) / EntryFadeDurationSeconds);
        }

        private static float EntryFirstSeenAt(DiaryEntryView entry)
        {
            string key = (entry.EventId ?? string.Empty)
                + "|"
                + (entry.PovRole ?? string.Empty)
                + "|"
                + (IsGenerated(entry) ? "written" : entry.LlmStatus ?? string.Empty);

            return FirstSeenAt(key);
        }

        private static float TitleFirstSeenAt(DiaryEntryView entry)
        {
            string key = (entry?.EventId ?? string.Empty)
                + "|"
                + (entry?.PovRole ?? string.Empty)
                + "|title|"
                + (entry?.Title ?? string.Empty);

            return FirstSeenAt(key);
        }

        private static float FirstSeenAt(string key)
        {
            float firstSeen;
            if (!EntryFirstSeenSeconds.TryGetValue(key, out firstSeen))
            {
                firstSeen = Time.realtimeSinceStartup;
                EntryFirstSeenSeconds[key] = firstSeen;
            }

            return firstSeen;
        }

        private static float WritingPulse(float phaseOffset)
        {
            return (Mathf.Sin(Time.realtimeSinceStartup * 5.5f + phaseOffset) + 1f) * 0.5f;
        }

        /// <summary>
        /// Uses the pawn's RimWorld favorite color for dialogue, brightened enough for dark UI.
        /// </summary>
        private static Color PreferredDialogueColor(Pawn pawn)
        {
            Color color = pawn?.story?.favoriteColor != null ? pawn.story.favoriteColor.color : FallbackDialogueColor;
            color.a = 1f;

            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (max < 0.55f)
            {
                float lift = 0.55f - max;
                color.r = Mathf.Clamp01(color.r + lift);
                color.g = Mathf.Clamp01(color.g + lift);
                color.b = Mathf.Clamp01(color.b + lift);
            }

            return color;
        }

        /// <summary>
        /// Draws the model id as a tiny, low-contrast note at the end of a diary card.
        /// </summary>
        private static void DrawModelName(Rect rect, string modelName)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.45f, 0.48f, 0.50f, 0.62f);
            Widgets.LabelFit(rect, modelName);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws the existing English-only diagnostic block in tiny muted text.
        /// </summary>
        private static void DrawDebugText(Rect rect, string debugText)
        {
            if (string.IsNullOrWhiteSpace(debugText))
            {
                return;
            }

            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.54f, 0.58f, 0.60f, 0.90f);
            Widgets.Label(rect, debugText);

            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws generated text in a light roleplay style: narration is muted/italic, while
        /// dialogue-looking lines are bold and colored with the pawn's preferred color.
        /// </summary>
        private static void DrawRoleplayText(Rect rect, string text, Color dialogueColor, float alpha)
        {
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            float curY = rect.y;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    curY += RoleplayParagraphGap;
                    continue;
                }

                bool dialogue = IsDialogueLine(line);
                GUIStyle style = RoleplayStyle(dialogue, dialogueColor, alpha);
                float height = style.CalcHeight(new GUIContent(line), rect.width);
                GUI.Label(new Rect(rect.x, curY, rect.width, height), line, style);
                curY += height + RoleplayLineGap;
            }

            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        /// <summary>
        /// Draws a soft pending-row indicator. The dots are simple rectangles, which keeps the
        /// animation cheap in RimWorld's immediate-mode GUI.
        /// </summary>
        private static void DrawWritingPlaceholder(Rect rect)
        {
            Color textColor = Color.Lerp(new Color(0.58f, 0.72f, 0.66f), new Color(0.80f, 0.96f, 0.84f), WritingPulse(0f));
            Rect dotsRect = new Rect(rect.x, rect.y + Text.LineHeight * 0.5f - 1f, 28f, 8f);
            DrawWritingDots(dotsRect, textColor, 0.4f);
        }

        private static void DrawWritingDots(Rect rect, Color color, float phaseOffset)
        {
            Color oldColor = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                float pulse = WritingPulse(phaseOffset - i * 0.75f);
                Color dotColor = new Color(color.r, color.g, color.b, Mathf.Lerp(0.25f, 0.95f, pulse));
                float yOffset = Mathf.Lerp(2f, -1f, pulse);
                Widgets.DrawBoxSolid(
                    new Rect(rect.x + i * (WritingDotSize + WritingDotGap), rect.y + yOffset, WritingDotSize, WritingDotSize),
                    dotColor);
            }

            GUI.color = oldColor;
        }

        /// <summary>
        /// Draws a compact linked-entry card showing a truncated preview of the other pawn's
        /// diary entry for the same event. Clicking it selects the other pawn, opens their diary
        /// tab, and scrolls to the same event.
        /// </summary>
        private static void DrawLinkedEntry(LinkedEntryView link, Rect rect, Pawn currentPawn)
        {
            if (link == null)
            {
                return;
            }

            // Hover highlight for the clickable area
            bool hovered = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(rect, hovered ? LinkedEntryHoverColor : LinkedEntryBgColor);
            // Draw border using thin solid rects (Widgets.DrawBox with color is unavailable)
            GUI.color = LinkedEntryBorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // Left-side accent strip colored by the linked role
            Color stripColor = DiaryEvent.RoleEquals(link.OtherRole, DiaryEvent.InitiatorRole)
                ? EntryAccentPalette[0] : EntryAccentPalette[4];
            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, 4f, rect.height - 2f), stripColor);

            // Label line: "Alice's perspective (initiator):"
            string roleLabel = DiaryEvent.RoleEquals(link.OtherRole, DiaryEvent.InitiatorRole)
                ? "PawnDiary.Tab.LinkedInitiator".Translate(link.OtherPawnName)
                : "PawnDiary.Tab.LinkedRecipient".Translate(link.OtherPawnName);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.80f, 0.85f, 0.92f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 14f, LinkedEntryLabelHeight), roleLabel);

            // Truncated preview text
            GUI.color = LinkedEntryTextColor;
            Text.Font = GameFont.Tiny;
            string preview = link.TruncatedText;
            if (!link.Generated && !string.IsNullOrWhiteSpace(preview))
            {
                preview = "PawnDiary.Tab.LinkedNotGenerated".Translate() + " " + preview;
            }
            else if (string.IsNullOrWhiteSpace(preview))
            {
                preview = "PawnDiary.Tab.LinkedNoText".Translate();
            }
            Widgets.Label(new Rect(rect.x + 10f, rect.y + LinkedEntryLabelHeight + 2f, rect.width - 14f, LinkedEntryTextHeight), preview);
            GUI.color = oldColor;
            Text.Font = oldFont;

            // Click handler: navigate to the other pawn's diary at this event
            if (Widgets.ButtonInvisible(rect, false))
            {
                NavigateToLinkedEntry(link, currentPawn);
            }

            // Tooltip hint
            if (hovered)
            {
                TooltipHandler.TipRegion(rect, "PawnDiary.Tab.LinkedTooltip".Translate(link.OtherPawnName));
            }
        }

        /// <summary>
        /// Selects the other pawn involved in a linked entry, opens their diary tab,
        /// and scrolls to the same event.
        /// </summary>
        private static void NavigateToLinkedEntry(LinkedEntryView link, Pawn currentPawn)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.OtherPawnId))
            {
                return;
            }

            Pawn otherPawn = FindPawnByLoadId(link.OtherPawnId);
            if (otherPawn == null || !otherPawn.Spawned)
            {
                return;
            }

            // Select the other pawn (same pattern as the Social-tab click patch)
            if (Find.Selector == null)
            {
                return;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(otherPawn, true, false);

            // Request scroll to the shared event and open the diary tab
            ITab_Pawn_Diary.RequestScrollToEntry(otherPawn, link.EventId);
            InspectTabBase opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
            if (!(opened is ITab_Pawn_Diary))
            {
                ITab_Pawn_Diary.ClearPendingScrollRequest();
            }
        }

        /// <summary>
        /// Finds a live Pawn by its RimWorld unique load ID. Searches all pawns on the
        /// current map first, then falls back to the world pawns list.
        /// </summary>
        private static Pawn FindPawnByLoadId(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId))
            {
                return null;
            }

            // Fast path: check the current map's pawn list
            if (Find.CurrentMap != null)
            {
                foreach (Pawn p in Find.CurrentMap.mapPawns.AllPawns)
                {
                    if (p != null && p.GetUniqueLoadID() == pawnId)
                    {
                        return p;
                    }
                }
            }

            // Fallback: world pawns (off-map colonists, etc.)
            if (Find.WorldPawns != null)
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAlive)
                {
                    if (p != null && p.GetUniqueLoadID() == pawnId)
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates the height needed for a single diary entry card, accounting for
        /// dynamic text wrapping of the generated diary text and the linked-entry card
        /// (if present) positioned before or after the main text.
        /// </summary>
        private static float EntryHeight(DiaryEntryView entry, float width, bool showLlmDebugInfo)
        {
            // Must match the draw width in FillTab (entryRect.width - 20f) so the measured wrap
            // height equals what is actually rendered; a wider measure clips long entries at the bottom.
            float innerWidth = width - 20f;

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float textHeight = RoleplayTextHeight(EntryBodyText(entry, showLlmDebugInfo), innerWidth);
            Text.Font = oldFont;

            float height = EntryTextTop + textHeight + EntryBottomPadding;

            // Add space for the linked-entry card and its surrounding padding
            if (entry.LinkedEntry != null)
            {
                height += LinkedEntryTotalHeight + LinkedEntryPadding;
            }

            if (HasModelName(entry))
            {
                height += ModelNameTopPadding + ModelNameHeight;
            }

            if (showLlmDebugInfo)
            {
                float debugHeight = DebugTextHeight(entry?.DebugText, innerWidth);
                if (debugHeight > 0f)
                {
                    height += DebugTextTopPadding + debugHeight;
                }
            }

            return height;
        }

        /// <summary>
        /// Measures the tiny diagnostic text block shown only when the dev debug toggle is enabled.
        /// </summary>
        private static float DebugTextHeight(string debugText, float width)
        {
            if (string.IsNullOrWhiteSpace(debugText))
            {
                return 0f;
            }

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float height = Text.CalcHeight(debugText, width);
            Text.Font = oldFont;
            return height;
        }

        /// <summary>
        /// Measures the same roleplay lines that DrawRoleplayText renders.
        /// </summary>
        private static float RoleplayTextHeight(string text, float width)
        {
            float height = 0f;
            foreach (string line in RoleplayLines(text))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    height += RoleplayParagraphGap;
                    continue;
                }

                GUIStyle style = RoleplayStyle(IsDialogueLine(line), FallbackDialogueColor, 1f);
                height += style.CalcHeight(new GUIContent(line), width) + RoleplayLineGap;
            }

            return Mathf.Max(Text.LineHeight, height);
        }

        /// <summary>
        /// Splits generated text into author-provided lines while preserving blank paragraph breaks.
        /// </summary>
        private static IEnumerable<string> RoleplayLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                yield return lines[i].Trim();
            }
        }

        /// <summary>
        /// Heuristic for lines that should read as dialogue in a roleplay log.
        /// </summary>
        private static bool IsDialogueLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.TrimStart();
            return trimmed.StartsWith("\"", StringComparison.Ordinal)
                || trimmed.StartsWith("'", StringComparison.Ordinal)
                || trimmed.StartsWith("-\"", StringComparison.Ordinal)
                || trimmed.StartsWith("- '", StringComparison.Ordinal)
                || trimmed.IndexOf('"') >= 0
                || trimmed.IndexOf(':') > 0 && trimmed.IndexOf(':') <= 24;
        }

        /// <summary>
        /// Creates an isolated GUIStyle so line colors/font styles do not leak into other RimWorld UI.
        /// </summary>
        private static GUIStyle RoleplayStyle(bool dialogue, Color dialogueColor, float alpha)
        {
            GUIStyle baseStyle = Text.CurFontStyle;
            GUIStyle style;
            if (dialogue)
            {
                if (dialogueStyle == null)
                {
                    dialogueStyle = new GUIStyle(baseStyle) { wordWrap = true, fontStyle = FontStyle.BoldAndItalic };
                }
                style = dialogueStyle;
            }
            else
            {
                if (narrativeStyle == null)
                {
                    narrativeStyle = new GUIStyle(baseStyle) { wordWrap = true, fontStyle = FontStyle.Italic };
                }
                style = narrativeStyle;
            }

            // Refresh the bits that can change at runtime (UI scale) without reallocating the style.
            style.font = baseStyle.font;
            style.fontSize = baseStyle.fontSize;
            Color color = dialogue ? dialogueColor : NarrativeColor;
            color.a *= Mathf.Clamp01(alpha);
            style.normal.textColor = color;
            return style;
        }
    }
}
