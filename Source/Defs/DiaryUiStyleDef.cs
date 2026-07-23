// Diary tab visualization style pulled into XML.
//
// This Def owns display-only numbers and colors for the pawn Diary inspector tab: card heights,
// spacing, direct-speech markers, accent colors, and color-cue mappings. It deliberately does not
// own runtime behavior toggles such as "show debug info" or "generate titles"; those remain saved
// player settings. New to C#/RimWorld? See AGENTS.md ("Defs").
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// XML-friendly color value. Use either a preset such as <c>name</c>, <c>hostile</c>,
    /// <c>ally</c>, or <c>gene</c>, or provide explicit r/g/b/a floats in the 0..1 range.
    /// </summary>
    public class DiaryUiColorSpec
    {
        public string preset;
        public float r = -1f;
        public float g = -1f;
        public float b = -1f;
        public float a = 1f;

        public Color ToColor(Color fallback)
        {
            Color presetColor;
            if (TryPresetColor(preset, out presetColor))
            {
                return presetColor;
            }

            if (r < 0f || g < 0f || b < 0f)
            {
                return fallback;
            }

            return new Color(Clamp01(r), Clamp01(g), Clamp01(b), Clamp01(a));
        }

        private static bool TryPresetColor(string key, out Color color)
        {
            string normalized = (key ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                color = Color.white;
                return false;
            }

            if (normalized.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                color = ColoredText.NameColor;
                return true;
            }

            if (normalized.Equals("hostile", StringComparison.OrdinalIgnoreCase))
            {
                color = ColoredText.FactionColor_Hostile;
                return true;
            }

            if (normalized.Equals("ally", StringComparison.OrdinalIgnoreCase))
            {
                color = ColoredText.FactionColor_Ally;
                return true;
            }

            if (normalized.Equals("gene", StringComparison.OrdinalIgnoreCase))
            {
                color = ColoredText.GeneColor;
                return true;
            }

            if (normalized.Equals("white", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.white;
                return true;
            }

            color = Color.white;
            return false;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    /// <summary>
    /// Maps a saved DiaryEvent color cue key to its Diary tab colors: the card accent stripe plus the
    /// optional page tint (the wash behind the whole card) and header rule (the line under its title).
    ///
    /// A null <see cref="pageTint"/>/<see cref="headerRule"/> means "inherit", so a cue that only wants
    /// a distinct accent stays on the shared parchment tint and rule without repeating them.
    /// </summary>
    public class DiaryUiCueColor
    {
        public string cue;
        public DiaryUiColorSpec color = new DiaryUiColorSpec();
        public DiaryUiColorSpec pageTint;
        public DiaryUiColorSpec headerRule;
    }

    /// <summary>
    /// Display-only style Def for the Diary inspector tab.
    /// </summary>
    public class DiaryUiStyleDef : Def
    {
        // ---- Tab window ----
        // Default width now includes the right-hand filter/controls panel (filterPanelWidth +
        // filterPanelGap) on top of the ~696px journal column, so the journal keeps its familiar width.
        public float tabWidth = 992f;
        public float tabHeight = 800f;
        public float tabMinHeight = 360f;
        public float tabScreenHeightMargin = 72f;

        // ---- Right-hand filter/controls panel ----
        // Independent, non-virtualized scroll column on the right of the Diary tab. Holds the year
        // selector, filter controls, and (in dev mode) the diary dev tools.
        public float filterPanelWidth = 260f;
        public float filterPanelGap = 12f;

        // ---- Control/header/card dimensions ----
        public float controlLineHeight = 28f;
        public float controlGap = 2f;
        public float entryTitleHeight = 28f;
        public float entryTextTop = 42f;
        public float entryBottomPadding = 10f;
        public float statusBadgeWidth = 34f;
        public float statusBadgeHeight = 24f;
        public float statusBadgeRightPadding = 24f;
        public float entryGap = 8f;
        public int autoExpandedEntryCount = 3;
        public float collapsedEntryChromePadding = 2f;
        public float expansionAnimationSpeed = 5.5f;
        // Extra pixels rendered above and below the scroll viewport. This keeps newly visible rows
        // already initialized during fast scrolling while preserving virtualization for long years.
        public float virtualizedEntryOverscanHeight = 800f;
        public float entryAccentWidth = 6f;
        public float entryLabelMaxWidth = 148f;
        public float entryFadeDurationSeconds = 0.55f;
        public float titleFadeDurationSeconds = 0.8f;

        // ---- Quadrum/season dividers ----
        // A slim centered "Aprimay · Spring · 5500" header drawn between the year's entry cards where
        // the quadrum changes. Height is the label row; the top gap separates it from the card above.
        // Height sized for the GameFont.Small divider label (up from the old Tiny-sized 24f row).
        public float quadrumDividerHeight = 30f;
        public float quadrumDividerTopGap = 6f;
        public float quadrumDividerLineGap = 10f;
        // Small season glyph drawn just left of the "quadrum · season · year" divider label. Size 0
        // hides it; the gap is the space between the glyph and the label.
        public float quadrumDividerIconSize = 15f;
        public float quadrumDividerIconGap = 6f;
        // Seasonal background-wash crossfade rate (higher = snappier). Frame-rate-independent
        // exponential ease, so the wash smoothly follows the season at the top of the journal.
        public float seasonWashLerpSpeed = 3f;

        // ---- Linked-entry card ----
        public float linkedEntryPadding = 8f;
        public float linkedEntryLabelHeight = 20f;
        public float linkedEntryTextHeight = 36f;
        public float linkedEntryTotalHeight = 64f;
        public int linkedInitiatorPaletteIndex = 0;
        public int linkedRecipientPaletteIndex = 4;

        // ---- Year pager and diagnostics ----
        public float yearFilterHeight = 32f;
        public float yearFilterGap = 6f;
        public float yearButtonWidth = 112f;
        public float modelNameTopPadding = 4f;
        public float modelNameHeight = 20f;
        public float debugTextTopPadding = 8f;

        // ---- Per-pawn writing-style header icon ----
        // The player-facing icon in the Diary header opens the Writing Style dialog without reserving
        // its own vertical row. Alpha values keep it subtle until hover.
        public float writingStyleIconSize = 22f;
        public float writingStyleIconRightGap = 6f;
        public float writingStyleIconAlpha = 0.72f;
        public float writingStyleIconHoverAlpha = 1.0f;

        // ---- Roleplay text and direct-speech blocks ----
        public float roleplayLineGap = 5f;
        public float roleplayParagraphGap = 8f;
        // ---- Render-time paragraph reflow (default atmosphere only) ----
        // Long single-line prose is split into readable paragraphs at punctuation cues and a length
        // cap. Saved GeneratedText is never mutated; this is display-only and runs through the same
        // measure/draw path so wrapped heights stay in sync. See DiaryParagraphReflow.
        public bool paragraphReflowEnabled = true;
        public int paragraphReflowTargetChars = 420;
        public int paragraphReflowMaxChars = 600;
        public bool paragraphReflowSplitOnSentenceEnd = true;
        public bool paragraphReflowSplitOnDateYear = true;
        public bool paragraphReflowSplitOnSemicolon = true;
        public bool paragraphReflowSplitOnEmDash = true;
        public int paragraphReflowMinBreakSpacing = 40;
        public float speechBlockLeftInset = 24f;
        public float speechBlockVerticalPadding = 3f;
        public string speechBlockOpenMarker = "[[speech]]";
        public string speechBlockCloseMarker = "[[/speech]]";
        public float atmosphereInset = 18f;
        public float fracturedPrimaryInsetMultiplier = 1.75f;
        public float fracturedSecondaryInsetMultiplier = 0.72f;
        public float fracturedRightInsetMultiplier = 0.35f;
        public float fracturedLongTopGap = 3f;
        public float fracturedShortTopGap = 7f;
        public float fracturedBottomGap = 3f;
        public float memorialInset = 34f;
        public float writingDotSize = 4f;
        public float writingDotGap = 5f;
        public float writingPulseSpeed = 5.5f;
        public float writingDotMinAlpha = 0.25f;
        public float writingDotMaxAlpha = 0.95f;
        public float writingDotLowYOffset = 2f;
        public float writingDotHighYOffset = -1f;

        // ---- Colors ----
        public DiaryUiColorSpec quietTextColor = Color(0.42f, 0.48f, 0.52f, 1f);
        public DiaryUiColorSpec narrativeTextColor = Color(0.78f, 0.78f, 0.72f, 1f);
        public DiaryUiColorSpec fallbackDialogueColor = Color(0.58f, 0.80f, 1f, 1f);
        public DiaryUiColorSpec speechBlockBgColor = Color(0.10f, 0.14f, 0.16f, 0.55f);
        public float speechBlockAccentAlpha = 0.72f;
        public DiaryUiColorSpec pageTintColor = Color(0.91f, 0.83f, 0.66f, 0.07f);
        public DiaryUiColorSpec headerRuleColor = Color(0.62f, 0.58f, 0.50f, 0.35f);
        // Legacy per-cue tint/rule fields. Page tints and header rules are now declared per cue in the
        // cueColors list below, so these are no longer consulted by PageTintForCue/HeaderRuleForCue —
        // they stay only so an older hand-edited DiaryUiStyleDef.xml keeps parsing without an error.
        // Their values are the seed for the combat/socialFight/mentalBreak rows in that list.
        public DiaryUiColorSpec combatPageTintColor = Color(0.70f, 0.10f, 0.07f, 0.18f);
        public DiaryUiColorSpec combatHeaderRuleColor = Color(0.95f, 0.18f, 0.12f, 0.65f);
        public DiaryUiColorSpec socialFightPageTintColor = Color(0.90f, 0.34f, 0.05f, 0.16f);
        public DiaryUiColorSpec socialFightHeaderRuleColor = Color(1f, 0.52f, 0.16f, 0.68f);
        public DiaryUiColorSpec mentalBreakPageTintColor = Color(0.18f, 0.34f, 0.22f, 0.09f);
        public DiaryUiColorSpec mentalBreakHeaderRuleColor = Color(0.40f, 0.58f, 0.40f, 0.42f);
        public DiaryUiColorSpec accentHighlightColor = Color(1f, 1f, 1f, 0.10f);
        public DiaryUiColorSpec titleTextColor = Color(0.88f, 0.86f, 0.79f, 1f);
        // Date tone for the entry header. Kept a touch quieter than the title, but bright enough to
        // read easily now that the date is drawn at the normal GameFont.Small size.
        public DiaryUiColorSpec entryDateColor = Color(0.80f, 0.79f, 0.74f, 1f);
        // Divider label/line brightened so the season/quadrum separator is clearly visible between
        // month groups instead of fading into the background.
        public DiaryUiColorSpec quadrumDividerLabelColor = Color(0.85f, 0.83f, 0.75f, 1f);
        public DiaryUiColorSpec quadrumDividerLineColor = Color(0.60f, 0.57f, 0.50f, 0.45f);
        // Subtle seasonal background wash behind the journal + filter panel. Very low alpha; the tab
        // eases between these as you scroll across a season divider. Set an alpha to 0 to disable one
        // season's wash (all four at 0 disables the effect entirely).
        public DiaryUiColorSpec springWashColor = Color(0.42f, 0.58f, 0.40f, 0.14f);
        public DiaryUiColorSpec summerWashColor = Color(0.85f, 0.72f, 0.36f, 0.14f);
        public DiaryUiColorSpec fallWashColor = Color(0.80f, 0.47f, 0.22f, 0.16f);
        public DiaryUiColorSpec winterWashColor = Color(0.48f, 0.60f, 0.80f, 0.14f);
        public DiaryUiColorSpec pendingTitlePrefixColor = Color(0.86f, 0.86f, 0.86f, 0.95f);
        public DiaryUiColorSpec pendingTitleDotBaseColor = Color(0.68f, 0.72f, 0.76f, 1f);
        public DiaryUiColorSpec writingPlaceholderLowColor = Color(0.58f, 0.72f, 0.66f, 1f);
        public DiaryUiColorSpec writingPlaceholderHighColor = Color(0.80f, 0.96f, 0.84f, 1f);
        public DiaryUiColorSpec yearDisabledColor = Color(1f, 1f, 1f, 0.42f);
        public DiaryUiColorSpec expansionIndicatorBaseColor = Color(0.62f, 0.65f, 0.68f, 0.85f);
        public DiaryUiColorSpec modelNameColor = Color(0.45f, 0.48f, 0.50f, 0.62f);
        public DiaryUiColorSpec regenerateEntryButtonColor = Color(0.82f, 0.85f, 0.88f, 0.85f);
        // Warm gold used to tint the entry favorite star when it is toggled on; the off state reuses
        // the quiet regenerate/footer tint so an un-favorited star reads as a dim outline.
        public DiaryUiColorSpec favoriteStarColor = Color(0.98f, 0.82f, 0.34f, 1f);
        // Amber accent for the filter-panel toggle when the panel is open AND a filter is engaged, so
        // the third ("active") state stands apart from the plain open (bright) and closed (dim) states.
        public DiaryUiColorSpec filterActiveIconColor = Color(0.96f, 0.78f, 0.40f, 0.98f);
        public DiaryUiColorSpec debugTextColor = Color(0.54f, 0.58f, 0.60f, 0.90f);
        public DiaryUiColorSpec devDangerButtonColor = Color(0.95f, 0.22f, 0.18f, 0.92f);
        public DiaryUiColorSpec linkedEntryLabelColor = Color(0.80f, 0.85f, 0.92f, 1f);
        public DiaryUiColorSpec linkedEntryBgColor = Color(0.15f, 0.17f, 0.20f, 0.85f);
        public DiaryUiColorSpec linkedEntryBorderColor = Color(0.35f, 0.45f, 0.55f, 1f);
        public DiaryUiColorSpec linkedEntryTextColor = Color(0.65f, 0.70f, 0.75f, 1f);
        public DiaryUiColorSpec linkedEntryHoverColor = Color(0.25f, 0.30f, 0.38f, 0.90f);
        public DiaryUiColorSpec defaultCueColor = Preset("name");
        public DiaryUiColorSpec quietCueColor = Color(0.74f, 0.74f, 0.70f, 1f);
        public DiaryUiColorSpec pawnNameSlaveColor = Color(0.96f, 0.72f, 0.26f, 1f);
        public DiaryUiColorSpec pawnNamePrisonerColor = Color(0.95f, 0.48f, 0.20f, 1f);
        public DiaryUiColorSpec pawnNameEnemyColor = Preset("hostile");
        public DiaryUiColorSpec pawnNameNeutralColor = Color(0.55f, 0.72f, 1f, 1f);
        public List<DiaryUiColorSpec> entryAccentPalette = new List<DiaryUiColorSpec>
        {
            Color(0.95f, 0.58f, 0.32f, 1f),
            Color(0.84f, 0.70f, 0.34f, 1f),
            Color(0.48f, 0.72f, 0.50f, 1f),
            Color(0.38f, 0.70f, 0.72f, 1f),
            Color(0.45f, 0.63f, 0.92f, 1f),
            Color(0.70f, 0.56f, 0.88f, 1f),
            Color(0.86f, 0.50f, 0.66f, 1f),
            Color(0.62f, 0.68f, 0.76f, 1f)
        };
        // Every cue's accent, page tint, and header rule in one place. Rows without a tint/rule inherit
        // the shared parchment pageTintColor/headerRuleColor. Mirrors 1.6/Defs/DiaryUiStyleDef.xml row
        // for row: XML replaces this whole list wholesale, so the two must not drift.
        //
        // Tint alphas stay in the 0.05-0.12 band and rule alphas in 0.35-0.65, so a page reads as
        // "tinted parchment" rather than a colored panel. The combat/socialFight/mentalBreak values are
        // the three that shipped before per-cue tints existed and are preserved exactly, including the
        // combat pair that deliberately exceeds those bands.
        public List<DiaryUiCueColor> cueColors = new List<DiaryUiCueColor>
        {
            Cue(DiaryEvent.CombatColorCue, Preset("hostile"),
                Color(0.70f, 0.10f, 0.07f, 0.18f), Color(0.95f, 0.18f, 0.12f, 0.65f)),
            Cue(DiaryEvent.DangerColorCue, Color(0.94f, 0.28f, 0.12f, 1f),
                Color(0.55f, 0.08f, 0.05f, 0.12f), Color(0.80f, 0.15f, 0.10f, 0.55f)),
            Cue(DiaryEvent.SocialFightColorCue, Color(1f, 0.52f, 0.16f, 1f),
                Color(0.90f, 0.34f, 0.05f, 0.16f), Color(1f, 0.52f, 0.16f, 0.68f)),
            Cue(DiaryEvent.MentalBreakColorCue, Color(0.48f, 0.64f, 0.48f, 1f),
                Color(0.18f, 0.34f, 0.22f, 0.09f), Color(0.40f, 0.58f, 0.40f, 0.42f)),
            // Daze and strange chat keep the shared rule: they are "something is off" moods, not the
            // loud events that earn a colored header line.
            Cue(DiaryEvent.DazeColorCue, Preset("gene"), Color(0.10f, 0.30f, 0.28f, 0.08f), null),
            Cue(DiaryEvent.ExtremeDarkColorCue, Color(0.58f, 0.05f, 0.08f, 1f),
                Color(0.25f, 0.02f, 0.05f, 0.12f), Color(0.45f, 0.05f, 0.10f, 0.60f)),
            Cue(DiaryEvent.StrangeChatColorCue, Color(0.42f, 0.96f, 0.50f, 1f),
                Color(0.05f, 0.25f, 0.10f, 0.10f), null),
            // The warm-white cue carries romance, heartfelt moments, birthdays, and skill passions, so
            // its page reads as warm rose paper rather than a colored alert.
            Cue(DiaryEvent.WhiteColorCue, Color(0.92f, 0.92f, 0.86f, 1f),
                Color(0.45f, 0.25f, 0.20f, 0.07f), Color(0.70f, 0.45f, 0.38f, 0.40f)),
            // Body-part events get their own accents so prosthetics, living changes, and loss do
            // not all read as generic important entries. Their tints reuse each accent at low alpha.
            Cue(DiaryEvent.BodyPartAnomalousColorCue, Color(0.80f, 0.22f, 0.45f, 1f),
                Color(0.80f, 0.22f, 0.45f, 0.08f), null),
            Cue(DiaryEvent.BodyPartArtificialColorCue, Color(0.36f, 0.78f, 0.84f, 1f),
                Color(0.36f, 0.78f, 0.84f, 0.08f), null),
            Cue(DiaryEvent.BodyPartLostColorCue, Color(0.95f, 0.40f, 0.30f, 1f),
                Color(0.95f, 0.40f, 0.30f, 0.08f), null),
            // Psychic events (psylink gains, psycast abilities) get a bright violet, distinct from the
            // dark blood-red extremeDark cue used by Anomaly dread content.
            Cue(DiaryEvent.PsychicColorCue, Color(0.78f, 0.42f, 1f, 1f),
                Color(0.25f, 0.10f, 0.40f, 0.08f), Color(0.55f, 0.30f, 0.80f, 0.45f)),
            // Royal-title gains and royal rituals get a gold, distinct from the warm-white cue shared
            // by heartfelt moments, birthdays, and skill passions.
            Cue(DiaryEvent.RoyaltyColorCue, Color(0.96f, 0.80f, 0.32f, 1f),
                Color(0.40f, 0.30f, 0.08f, 0.08f), Color(0.75f, 0.60f, 0.20f, 0.45f)),
            // Busy incident days ("eventful") had no row at all and fell back to the default accent.
            Cue(EventfulColorCue, Color(0.90f, 0.65f, 0.25f, 1f),
                Color(0.35f, 0.25f, 0.08f, 0.06f), null),
            // The end-of-quadrum look-back reads as a calm blue page rather than an incident.
            Cue(QuadrumReflectionColorCue, Color(0.66f, 0.86f, 0.95f, 1f),
                Color(0.15f, 0.20f, 0.35f, 0.07f), null),
            Cue(DiaryEvent.QuietColorCue, Color(0.74f, 0.74f, 0.70f, 1f),
                Color(0.30f, 0.30f, 0.28f, 0.05f), null)
        };

        // Cue keys owned by XML group rows rather than DiaryEvent constants. They are plain saved
        // strings, so they live here only to keep this list and the XML spelling in sync.
        private const string EventfulColorCue = "eventful";
        private const string QuadrumReflectionColorCue = "quadrumReflection";

        public float CollapsedEntryHeight => entryTitleHeight + collapsedEntryChromePadding;
        public float VirtualizedEntryOverscanHeight => virtualizedEntryOverscanHeight > 0f && !float.IsNaN(virtualizedEntryOverscanHeight) ? virtualizedEntryOverscanHeight : 0f;
        public Color QuietTextColor => quietTextColor.ToColor(new Color(0.42f, 0.48f, 0.52f));
        public Color NarrativeTextColor => narrativeTextColor.ToColor(new Color(0.78f, 0.78f, 0.72f));
        public Color FallbackDialogueColor => fallbackDialogueColor.ToColor(new Color(0.58f, 0.80f, 1f));
        public Color SpeechBlockBgColor => speechBlockBgColor.ToColor(new Color(0.10f, 0.14f, 0.16f, 0.55f));
        public Color PageTintColor => pageTintColor.ToColor(new Color(0.91f, 0.83f, 0.66f, 0.07f));
        public Color HeaderRuleColor => headerRuleColor.ToColor(new Color(0.62f, 0.58f, 0.50f, 0.35f));
        public Color CombatPageTintColor => combatPageTintColor.ToColor(new Color(0.70f, 0.10f, 0.07f, 0.18f));
        public Color CombatHeaderRuleColor => combatHeaderRuleColor.ToColor(new Color(0.95f, 0.18f, 0.12f, 0.65f));
        public Color SocialFightPageTintColor => socialFightPageTintColor.ToColor(new Color(0.90f, 0.34f, 0.05f, 0.16f));
        public Color SocialFightHeaderRuleColor => socialFightHeaderRuleColor.ToColor(new Color(1f, 0.52f, 0.16f, 0.68f));
        public Color MentalBreakPageTintColor => mentalBreakPageTintColor.ToColor(new Color(0.18f, 0.34f, 0.22f, 0.09f));
        public Color MentalBreakHeaderRuleColor => mentalBreakHeaderRuleColor.ToColor(new Color(0.40f, 0.58f, 0.40f, 0.42f));
        public Color AccentHighlightColor => accentHighlightColor.ToColor(new Color(1f, 1f, 1f, 0.10f));
        public Color TitleTextColor => titleTextColor.ToColor(new Color(0.88f, 0.86f, 0.79f));
        public Color EntryDateColor => entryDateColor.ToColor(new Color(0.80f, 0.79f, 0.74f, 1f));
        public Color QuadrumDividerLabelColor => quadrumDividerLabelColor.ToColor(new Color(0.85f, 0.83f, 0.75f, 1f));
        public Color QuadrumDividerLineColor => quadrumDividerLineColor.ToColor(new Color(0.60f, 0.57f, 0.50f, 0.45f));
        public Color SpringWashColor => springWashColor.ToColor(new Color(0.42f, 0.58f, 0.40f, 0.14f));
        public Color SummerWashColor => summerWashColor.ToColor(new Color(0.85f, 0.72f, 0.36f, 0.14f));
        public Color FallWashColor => fallWashColor.ToColor(new Color(0.80f, 0.47f, 0.22f, 0.16f));
        public Color WinterWashColor => winterWashColor.ToColor(new Color(0.48f, 0.60f, 0.80f, 0.14f));
        public Color PendingTitlePrefixColor => pendingTitlePrefixColor.ToColor(new Color(0.86f, 0.86f, 0.86f, 0.95f));
        public Color PendingTitleDotBaseColor => pendingTitleDotBaseColor.ToColor(new Color(0.68f, 0.72f, 0.76f));
        public Color WritingPlaceholderLowColor => writingPlaceholderLowColor.ToColor(new Color(0.58f, 0.72f, 0.66f));
        public Color WritingPlaceholderHighColor => writingPlaceholderHighColor.ToColor(new Color(0.80f, 0.96f, 0.84f));
        public Color YearDisabledColor => yearDisabledColor.ToColor(new Color(1f, 1f, 1f, 0.42f));
        public Color ExpansionIndicatorBaseColor => expansionIndicatorBaseColor.ToColor(new Color(0.62f, 0.65f, 0.68f, 0.85f));
        public Color ModelNameColor => modelNameColor.ToColor(new Color(0.45f, 0.48f, 0.50f, 0.62f));
        public Color RegenerateEntryButtonColor => regenerateEntryButtonColor.ToColor(new Color(0.82f, 0.85f, 0.88f, 0.85f));
        public Color FavoriteStarColor => favoriteStarColor.ToColor(new Color(0.98f, 0.82f, 0.34f, 1f));
        public Color FilterActiveIconColor => filterActiveIconColor.ToColor(new Color(0.96f, 0.78f, 0.40f, 0.98f));
        public Color DebugTextColor => debugTextColor.ToColor(new Color(0.54f, 0.58f, 0.60f, 0.90f));
        public Color DevDangerButtonColor => devDangerButtonColor.ToColor(new Color(0.95f, 0.22f, 0.18f, 0.92f));
        public Color LinkedEntryLabelColor => linkedEntryLabelColor.ToColor(new Color(0.80f, 0.85f, 0.92f));
        public Color LinkedEntryBgColor => linkedEntryBgColor.ToColor(new Color(0.15f, 0.17f, 0.20f, 0.85f));
        public Color LinkedEntryBorderColor => linkedEntryBorderColor.ToColor(new Color(0.35f, 0.45f, 0.55f));
        public Color LinkedEntryTextColor => linkedEntryTextColor.ToColor(new Color(0.65f, 0.70f, 0.75f));
        public Color LinkedEntryHoverColor => linkedEntryHoverColor.ToColor(new Color(0.25f, 0.30f, 0.38f, 0.90f));
        public Color DefaultCueColor => defaultCueColor.ToColor(ColoredText.NameColor);
        public Color QuietCueColor => quietCueColor.ToColor(new Color(0.74f, 0.74f, 0.70f));
        public Color PawnNameSlaveColor => pawnNameSlaveColor.ToColor(new Color(0.96f, 0.72f, 0.26f));
        public Color PawnNamePrisonerColor => pawnNamePrisonerColor.ToColor(new Color(0.95f, 0.48f, 0.20f));
        public Color PawnNameEnemyColor => pawnNameEnemyColor.ToColor(ColoredText.FactionColor_Hostile);
        public Color PawnNameNeutralColor => pawnNameNeutralColor.ToColor(new Color(0.55f, 0.72f, 1f));

        public Color ColorForCue(string cue, bool important)
        {
            Color fallback = important ? DefaultCueColor : QuietCueColor;
            DiaryUiCueColor entry = FindCue(cue);
            if (entry == null || entry.color == null)
            {
                return fallback;
            }

            return entry.color.ToColor(fallback);
        }

        /// <summary>
        /// The page wash behind a card. Cues declare their own tint in <see cref="cueColors"/>; an
        /// unknown/modded cue, or one that declares no tint, keeps the shared parchment tint.
        /// </summary>
        public Color PageTintForCue(string cue)
        {
            DiaryUiCueColor entry = FindCue(cue);
            return entry?.pageTint == null ? PageTintColor : entry.pageTint.ToColor(PageTintColor);
        }

        /// <summary>
        /// The subtle seasonal background wash for a season, or fully transparent for an unknown
        /// season (which fades the wash out). Permanent-summer/winter map to their seasonal wash.
        /// </summary>
        public Color SeasonWashColor(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    return SpringWashColor;
                case Season.Summer:
                case Season.PermanentSummer:
                    return SummerWashColor;
                case Season.Fall:
                    return FallWashColor;
                case Season.Winter:
                case Season.PermanentWinter:
                    return WinterWashColor;
                default:
                    return new Color(0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// The line under a card's title. Same inheritance rule as <see cref="PageTintForCue"/>: most
        /// cues stay on the shared rule, and only the loud ones declare their own.
        /// </summary>
        public Color HeaderRuleForCue(string cue)
        {
            DiaryUiCueColor entry = FindCue(cue);
            return entry?.headerRule == null ? HeaderRuleColor : entry.headerRule.ToColor(HeaderRuleColor);
        }

        // One case-insensitive lookup shared by the accent, tint, and rule accessors, so the three can
        // never disagree about which row a cue resolves to. Null means "no row" (blank or modded cue).
        private DiaryUiCueColor FindCue(string cue)
        {
            if (string.IsNullOrWhiteSpace(cue) || cueColors == null)
            {
                return null;
            }

            string key = cue.Trim();
            for (int i = 0; i < cueColors.Count; i++)
            {
                DiaryUiCueColor entry = cueColors[i];
                if (entry != null && string.Equals(entry.cue, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public Color PaletteColor(int index, Color fallback)
        {
            if (entryAccentPalette == null || index < 0 || index >= entryAccentPalette.Count || entryAccentPalette[index] == null)
            {
                return fallback;
            }

            return entryAccentPalette[index].ToColor(fallback);
        }

        private static DiaryUiColorSpec Color(float r, float g, float b, float a)
        {
            return new DiaryUiColorSpec { r = r, g = g, b = b, a = a };
        }

        private static DiaryUiColorSpec Preset(string preset)
        {
            return new DiaryUiColorSpec { preset = preset };
        }

        // A null pageTint/headerRule means the cue inherits the shared parchment tint and rule.
        private static DiaryUiCueColor Cue(string cue, DiaryUiColorSpec color,
            DiaryUiColorSpec pageTint = null, DiaryUiColorSpec headerRule = null)
        {
            return new DiaryUiCueColor
            {
                cue = cue,
                color = color,
                pageTint = pageTint,
                headerRule = headerRule
            };
        }
    }

    /// <summary>
    /// Accessor for the single DiaryUiStyleDef with safe code fallbacks when XML is absent.
    /// </summary>
    internal static class DiaryUiStyles
    {
        private static DiaryUiStyleDef cached;
        private static readonly DiaryUiStyleDef Fallback = new DiaryUiStyleDef();

        public static DiaryUiStyleDef Current
        {
            get
            {
                if (cached == null)
                {
                    cached = DefDatabase<DiaryUiStyleDef>.GetNamedSilentFail("Diary_UiStyle");
                }

                return cached ?? Fallback;
            }
        }

        /// <summary>
        /// Builds the pure reflow options from the current UI style, clamping lengths to a positive
        /// range so a misconfigured XML cannot make ReflowLine loop forever.
        /// </summary>
        public static DiaryParagraphReflowOptions BuildParagraphReflowOptions()
        {
            DiaryUiStyleDef style = Current;
            int target = style.paragraphReflowTargetChars;
            int max = style.paragraphReflowMaxChars;
            if (target < 1)
            {
                target = 1;
            }

            if (max < 1)
            {
                max = 1;
            }

            if (max < target)
            {
                max = target;
            }

            return new DiaryParagraphReflowOptions
            {
                enabled = style.paragraphReflowEnabled,
                targetChars = target,
                maxChars = max,
                splitOnSentenceEnd = style.paragraphReflowSplitOnSentenceEnd,
                splitOnDateYear = style.paragraphReflowSplitOnDateYear,
                splitOnSemicolon = style.paragraphReflowSplitOnSemicolon,
                splitOnEmDash = style.paragraphReflowSplitOnEmDash,
                minBreakSpacing = style.paragraphReflowMinBreakSpacing
            };
        }
    }
}
