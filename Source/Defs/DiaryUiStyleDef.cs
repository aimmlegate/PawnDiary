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
    /// Maps a saved DiaryEvent color cue key to an accent color in the Diary tab.
    /// </summary>
    public class DiaryUiCueColor
    {
        public string cue;
        public DiaryUiColorSpec color = new DiaryUiColorSpec();
    }

    /// <summary>
    /// Display-only style Def for the Diary inspector tab.
    /// </summary>
    public class DiaryUiStyleDef : Def
    {
        // ---- Tab window ----
        public float tabWidth = 720f;
        public float tabHeight = 800f;

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
        public DiaryUiColorSpec combatPageTintColor = Color(0.70f, 0.10f, 0.07f, 0.18f);
        public DiaryUiColorSpec combatHeaderRuleColor = Color(0.95f, 0.18f, 0.12f, 0.65f);
        public DiaryUiColorSpec socialFightPageTintColor = Color(0.90f, 0.34f, 0.05f, 0.16f);
        public DiaryUiColorSpec socialFightHeaderRuleColor = Color(1f, 0.52f, 0.16f, 0.68f);
        public DiaryUiColorSpec mentalBreakPageTintColor = Color(0.18f, 0.34f, 0.22f, 0.09f);
        public DiaryUiColorSpec mentalBreakHeaderRuleColor = Color(0.40f, 0.58f, 0.40f, 0.42f);
        public DiaryUiColorSpec accentHighlightColor = Color(1f, 1f, 1f, 0.10f);
        public DiaryUiColorSpec titleTextColor = Color(0.88f, 0.86f, 0.79f, 1f);
        public DiaryUiColorSpec pendingTitlePrefixColor = Color(0.86f, 0.86f, 0.86f, 0.95f);
        public DiaryUiColorSpec pendingTitleDotBaseColor = Color(0.68f, 0.72f, 0.76f, 1f);
        public DiaryUiColorSpec writingPlaceholderLowColor = Color(0.58f, 0.72f, 0.66f, 1f);
        public DiaryUiColorSpec writingPlaceholderHighColor = Color(0.80f, 0.96f, 0.84f, 1f);
        public DiaryUiColorSpec yearDisabledColor = Color(1f, 1f, 1f, 0.42f);
        public DiaryUiColorSpec expansionIndicatorBaseColor = Color(0.62f, 0.65f, 0.68f, 0.85f);
        public DiaryUiColorSpec modelNameColor = Color(0.45f, 0.48f, 0.50f, 0.62f);
        public DiaryUiColorSpec regenerateEntryButtonColor = Color(0.48f, 0.51f, 0.53f, 0.46f);
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
        public List<DiaryUiCueColor> cueColors = new List<DiaryUiCueColor>
        {
            Cue(DiaryEvent.CombatColorCue, Preset("hostile")),
            Cue(DiaryEvent.DangerColorCue, Color(0.94f, 0.28f, 0.12f, 1f)),
            Cue(DiaryEvent.SocialFightColorCue, Color(1f, 0.52f, 0.16f, 1f)),
            Cue(DiaryEvent.MentalBreakColorCue, Color(0.48f, 0.64f, 0.48f, 1f)),
            Cue(DiaryEvent.DazeColorCue, Preset("gene")),
            Cue(DiaryEvent.ExtremeDarkColorCue, Color(0.58f, 0.05f, 0.08f, 1f)),
            Cue(DiaryEvent.StrangeChatColorCue, Color(0.42f, 0.96f, 0.50f, 1f)),
            Cue(DiaryEvent.WhiteColorCue, Color(0.92f, 0.92f, 0.86f, 1f)),
            // Body-part events get their own accents so prosthetics, living changes, and loss do
            // not all read as generic important entries.
            Cue(DiaryEvent.BodyPartAnomalousColorCue, Color(0.80f, 0.22f, 0.45f, 1f)),
            Cue(DiaryEvent.BodyPartArtificialColorCue, Color(0.36f, 0.78f, 0.84f, 1f)),
            Cue(DiaryEvent.BodyPartLostColorCue, Color(0.95f, 0.40f, 0.30f, 1f)),
            // Psychic events (psylink gains, psycast abilities) get a bright violet, distinct from the
            // dark blood-red extremeDark cue used by Anomaly dread content.
            Cue(DiaryEvent.PsychicColorCue, Color(0.78f, 0.42f, 1f, 1f)),
            // Royal-title gains and royal rituals get a gold, distinct from the warm-white cue shared
            // by heartfelt moments, birthdays, and skill passions.
            Cue(DiaryEvent.RoyaltyColorCue, Color(0.96f, 0.80f, 0.32f, 1f)),
            Cue(DiaryEvent.QuietColorCue, Color(0.74f, 0.74f, 0.70f, 1f))
        };

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
        public Color PendingTitlePrefixColor => pendingTitlePrefixColor.ToColor(new Color(0.86f, 0.86f, 0.86f, 0.95f));
        public Color PendingTitleDotBaseColor => pendingTitleDotBaseColor.ToColor(new Color(0.68f, 0.72f, 0.76f));
        public Color WritingPlaceholderLowColor => writingPlaceholderLowColor.ToColor(new Color(0.58f, 0.72f, 0.66f));
        public Color WritingPlaceholderHighColor => writingPlaceholderHighColor.ToColor(new Color(0.80f, 0.96f, 0.84f));
        public Color YearDisabledColor => yearDisabledColor.ToColor(new Color(1f, 1f, 1f, 0.42f));
        public Color ExpansionIndicatorBaseColor => expansionIndicatorBaseColor.ToColor(new Color(0.62f, 0.65f, 0.68f, 0.85f));
        public Color ModelNameColor => modelNameColor.ToColor(new Color(0.45f, 0.48f, 0.50f, 0.62f));
        public Color RegenerateEntryButtonColor => regenerateEntryButtonColor.ToColor(new Color(0.48f, 0.51f, 0.53f, 0.46f));
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
            if (!string.IsNullOrWhiteSpace(cue) && cueColors != null)
            {
                string key = cue.Trim();
                for (int i = 0; i < cueColors.Count; i++)
                {
                    DiaryUiCueColor entry = cueColors[i];
                    if (entry != null && string.Equals(entry.cue, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.color == null ? (important ? DefaultCueColor : QuietCueColor) : entry.color.ToColor(important ? DefaultCueColor : QuietCueColor);
                    }
                }
            }

            return important ? DefaultCueColor : QuietCueColor;
        }

        public Color PageTintForCue(string cue)
        {
            if (CueEquals(cue, DiaryEvent.CombatColorCue))
            {
                return CombatPageTintColor;
            }

            if (CueEquals(cue, DiaryEvent.SocialFightColorCue))
            {
                return SocialFightPageTintColor;
            }

            return CueEquals(cue, DiaryEvent.MentalBreakColorCue) ? MentalBreakPageTintColor : PageTintColor;
        }

        public Color HeaderRuleForCue(string cue)
        {
            if (CueEquals(cue, DiaryEvent.CombatColorCue))
            {
                return CombatHeaderRuleColor;
            }

            if (CueEquals(cue, DiaryEvent.SocialFightColorCue))
            {
                return SocialFightHeaderRuleColor;
            }

            return CueEquals(cue, DiaryEvent.MentalBreakColorCue) ? MentalBreakHeaderRuleColor : HeaderRuleColor;
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

        private static DiaryUiCueColor Cue(string cue, DiaryUiColorSpec color)
        {
            return new DiaryUiCueColor { cue = cue, color = color };
        }

        private static bool CueEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Accessor for the single DiaryUiStyleDef with safe code fallbacks when XML is absent.
    /// </summary>
    public static class DiaryUiStyles
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
        /// Builds the pure reflow options from the current UI style, clamping maxChars to be at least
        /// targetChars so a misconfigured XML cannot make ReflowLine loop forever.
        /// </summary>
        public static DiaryParagraphReflowOptions BuildParagraphReflowOptions()
        {
            DiaryUiStyleDef style = Current;
            int target = style.paragraphReflowTargetChars;
            int max = style.paragraphReflowMaxChars;
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
