// Pure decision logic for the VSIE gathering bridge: given a RimWorld GatheringDef defName, decide
// whether Pawn Diary should record a first-class "a gathering happened" diary event and, if so, which
// stable eventKey + localization keys to use.
//
// This file is deliberately free of RimWorld/Verse/Unity types so its edge cases are covered by
// tests/VsieBridgeLogicTests without booting the game (AGENTS.md / SKILL.md: "if a function can be
// pure, make it pure"). The impure caller (VsieGatheringBridge) turns a returned plan into a localized
// ExternalEventRequest.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo. (TS analogy: a small pure lookup that
// returns a plain result object or null — no I/O, no game state.)
//
// Scope is intentionally narrow (design/MOD_COMPAT_PLAN.md §3 "Tier 2"): only the two
// colony-narratively important gatherings get their own diary entry. VSIE's flavor gatherings
// (movie night, skygazing, snowmen, dates, parties, ...) are NOT captured here — their emotional
// residue already reaches the diary as "social afterthoughts" through the vsie_thoughts group.
using System;
using System.Collections.Generic;

namespace PawnDiaryVsie.Pure
{
    /// <summary>
    /// Immutable "record this gathering" plan produced by <see cref="VsieGatheringMap.Plan"/>.
    /// Carries only stable string tokens; the impure adapter resolves the localization keys.
    /// </summary>
    public sealed class VsieGatheringPlan
    {
        public VsieGatheringPlan(string eventKey, string labelKey, string summaryKey)
        {
            EventKey = eventKey;
            LabelKey = labelKey;
            SummaryKey = summaryKey;
        }

        /// <summary>Stable classifier submitted to Pawn Diary and matched by an External-domain group in
        /// <c>1.6/Defs/DiaryExternalGroups_Vsie.xml</c>. A frozen save token — never rename after release.</summary>
        public string EventKey { get; }

        /// <summary>Keyed key for the diary event label (see the adapter's Languages/*/Keyed files).</summary>
        public string LabelKey { get; }

        /// <summary>Keyed key for the factual "what happened" summary line; format arg {0} is the
        /// organizer's short name.</summary>
        public string SummaryKey { get; }
    }

    /// <summary>
    /// Maps a VSIE GatheringDef defName to its diary-capture plan, or null when the gathering is one we
    /// intentionally leave to the ambient thought capture.
    /// </summary>
    public static class VsieGatheringMap
    {
        // GatheringDef defNames are RimWorld identifiers: exact, case-sensitive strings. We match them
        // as plain strings so this adapter never references a VSIE type (mirrors Pawn Diary's core
        // string-matcher pattern; see AGENTS.md "DLC-safety"). Verified against the installed VSIE 1.6
        // source (1.6/Defs/GatheringDefs/Gatherings.xml) on 2026-07-11.
        public const string BirthdayDefName = "VSIE_BirthdayParty";
        public const string FuneralDefName = "VSIE_Funeral";

        // Stable classifiers submitted to Pawn Diary. Exposed as constants so the External-group XML,
        // the settings toggles (VsieBridgeSettings.AllowsEventKey), and the tests all share one source
        // of truth. eventKeys use the mod prefix convention ("vsie_") to avoid cross-adapter collisions.
        // Frozen save tokens — never rename after release.
        public const string BirthdayEventKey = "vsie_birthday";
        public const string FuneralEventKey = "vsie_funeral";

        private static readonly Dictionary<string, VsieGatheringPlan> Plans =
            new Dictionary<string, VsieGatheringPlan>(StringComparer.Ordinal)
            {
                {
                    BirthdayDefName,
                    new VsieGatheringPlan(BirthdayEventKey, "PawnDiaryVsie.Event.BirthdayLabel", "PawnDiaryVsie.Event.BirthdaySummary")
                },
                {
                    FuneralDefName,
                    new VsieGatheringPlan(FuneralEventKey, "PawnDiaryVsie.Event.FuneralLabel", "PawnDiaryVsie.Event.FuneralSummary")
                },
            };

        /// <summary>
        /// Returns the capture plan for a gathering defName, or null when it should not be recorded as
        /// its own diary event. Null/blank input returns null.
        /// </summary>
        public static VsieGatheringPlan Plan(string gatheringDefName)
        {
            if (string.IsNullOrEmpty(gatheringDefName))
            {
                return null;
            }

            VsieGatheringPlan plan;
            return Plans.TryGetValue(gatheringDefName, out plan) ? plan : null;
        }
    }
}
