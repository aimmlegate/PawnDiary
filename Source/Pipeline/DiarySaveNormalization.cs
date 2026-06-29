// Pure helpers that repair/normalize diary save-model fields after Scribe loads them. The save
// models (DiaryEvent, ArchivedDiaryEntry) are IExposable and run under RimWorld's Unity Mono
// runtime, so their ExposeData / NormalizeOnLoad methods are impure: they read/write Scribe and
// (for DiaryEvent) call ResolveColorCue, which hits DefDatabase via GroupForDisplay. The pieces
// in this file are pure functions of plain strings/ints only — the regression-prone defaulting
// logic (cross-slot surroundings chain, neutral-text merge, legacy gameContext rebuild, year
// extraction, defensive clamps) — so save-compatibility can be unit-tested without RimWorld.
//
// Extraction contract: every behavior change here is a save-compat decision. The exact output
// shapes ("def=...; label=...", "name: text\nname: text", the "unknown"/"none" sentinels, the
// 0..4 staggered range, int.MinValue for unknown year) are part of the persisted/prompt contract
// and must not drift. See DOCUMENTATION.md §9 (Save Data And Compatibility) for the full Scribe-key
// stability contract, and AGENTS.md ("IExposable").
using System;

namespace PawnDiary
{
    /// <summary>
    /// Pure post-load normalization for the diary save models. Holds only the parts of
    /// <c>NormalizeOnLoad</c> that are pure functions of plain strings/ints; the impure Scribe I/O,
    /// GUID minting, and DefDatabase-backed color-cue resolution stay on the save models themselves.
    /// </summary>
    public static class DiarySaveNormalization
    {
        /// <summary>Mood-impact direction written for mood-event entries that saved no direction.</summary>
        public const string DefaultMoodImpact = "neutral";

        /// <summary>Sentinel written when a POV pawn summary was not captured (e.g. pre-summary saves).</summary>
        public const string DefaultPawnSummary = "unknown";

        /// <summary>Sentinel written when no prompt-continuity opener history was saved.</summary>
        public const string DefaultContinuity = "none";

        /// <summary>Sentinel written when a POV surroundings string was not captured.</summary>
        public const string DefaultSurroundings = "unknown";

        /// <summary>Year value meaning "no year could be derived from the saved date". int.MinValue.</summary>
        public const int UnknownYear = int.MinValue;

        /// <summary>Inclusive upper bound for the staggered-handwriting intensity (0..4).</summary>
        public const int MaxStaggeredIntensity = 4;

        /// <summary>
        /// Null-coalesces a loaded string field to empty. Scribe leaves unset string fields as null on
        /// cross-version loads, and downstream prompt/UI code assumes non-null throughout.
        /// </summary>
        public static string NormalizeString(string value)
        {
            return value ?? string.Empty;
        }

        /// <summary>
        /// Returns <paramref name="fallback"/> when the value is null/whitespace, otherwise the value
        /// unchanged (NOT trimmed — preserves the saved bytes). Used for pawn summary / continuity,
        /// where a blank saved value is meaningful as "not captured" rather than "".
        /// </summary>
        public static string NormalizeWhitespaceOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        /// <summary>
        /// Clamps the display-only staggered-handwriting intensity to the valid 0..4 range. Negative
        /// or garbage values from older saves collapse to 0 (normal handwriting); anything above the
        /// max collapses to the max. Mirrors the clamp on the capture side (PawnFactCapture).
        /// </summary>
        public static int ClampStaggeredIntensity(int intensity)
        {
            if (intensity < 0)
            {
                return 0;
            }

            return intensity > MaxStaggeredIntensity ? MaxStaggeredIntensity : intensity;
        }

        /// <summary>
        /// Rebuilds the <c>gameContext</c> string for older saves that predate the field. The exact
        /// shape <c>"def={defName}; label={label}"</c> is load-bearing: it is re-parsed by
        /// <c>DiaryContextFields</c> and embedded in the LLM prompt, so changing it would silently
        /// shift prompt content for every legacy save on first load. Inputs are null-safe.
        /// </summary>
        public static string BuildDefaultGameContext(string interactionDefName, string interactionLabel)
        {
            return "def=" + NormalizeString(interactionDefName)
                + "; label=" + NormalizeString(interactionLabel);
        }

        /// <summary>
        /// Older saves have no per-event instruction; the interaction label is the closest stable
        /// stand-in. Null-safe; returns empty when the label was never set.
        /// </summary>
        public static string BuildDefaultInstruction(string interactionLabel)
        {
            return NormalizeString(interactionLabel);
        }

        /// <summary>
        /// Resolves the cross-slot surroundings default chain. The initiator falls back to "unknown"
        /// when blank; the recipient then borrows the already-defaulted initiator value when blank,
        /// so the two POVs of a pair event share surroundings unless the recipient explicitly had its
        /// own. Returns the resolved pair via out parameters so the caller can assign back to slots.
        /// Pure: takes the two already-loaded strings, returns two normalized strings.
        /// </summary>
        public static void ResolveSurroundingsChain(
            string initiatorSurroundings,
            string recipientSurroundings,
            out string resolvedInitiator,
            out string resolvedRecipient)
        {
            resolvedInitiator = string.IsNullOrWhiteSpace(initiatorSurroundings)
                ? DefaultSurroundings
                : initiatorSurroundings;
            resolvedRecipient = string.IsNullOrWhiteSpace(recipientSurroundings)
                ? resolvedInitiator
                : recipientSurroundings;
        }

        /// <summary>
        /// Rebuilds the neutral chronicle raw text when a save has none. Neutral pages are not
        /// pawn-authored, so the merge is the canonical "both POVs in one page" shape: if the two POV
        /// raw texts already agree, keep a single copy; otherwise prefix each line with the speaker's
        /// name and join with "\n". The exact delimiter and name-prefix shape are part of the
        /// prompt/preview contract — changing them would alter the merged body every legacy pair-event
        /// neutral page renders on first load. Returns <paramref name="currentNeutralText"/> unchanged
        /// when it is non-blank. All inputs are null-safe.
        /// </summary>
        public static string BuildDefaultNeutralText(
            string currentNeutralText,
            string initiatorName,
            string initiatorText,
            string recipientName,
            string recipientText)
        {
            if (!string.IsNullOrWhiteSpace(currentNeutralText))
            {
                return currentNeutralText;
            }

            string initText = NormalizeString(initiatorText);
            if (string.Equals(initText, recipientText, StringComparison.OrdinalIgnoreCase))
            {
                return initText;
            }

            return NormalizeString(initiatorName) + ": " + initText
                + "\n" + NormalizeString(recipientName) + ": " + NormalizeString(recipientText);
        }

        /// <summary>
        /// Extracts the last run of digits in a human-readable RimWorld date string as the year
        /// (e.g. "10 Spring 5502" -> 5502). Returns <see cref="UnknownYear"/> when the date is blank
        /// or contains no digits. Pure; mirrors the body that used to live on ArchivedDiaryEntry so
        /// the year-repair path can be tested without a save.
        /// </summary>
        public static int ExtractYear(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return UnknownYear;
            }

            int end = -1;
            for (int i = date.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(date[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
            {
                return UnknownYear;
            }

            int start = end;
            while (start > 0 && char.IsDigit(date[start - 1]))
            {
                start--;
            }

            int parsed;
            return int.TryParse(date.Substring(start, end - start + 1), out parsed) ? parsed : UnknownYear;
        }
    }
}
