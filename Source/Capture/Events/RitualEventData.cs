// Payload + pure decision for an Ideology ritual completion. The live hook fans one finished
// LordJob_Ritual out to organizer/target/participant/spectator solo entries; this payload is the
// plain per-pawn slice that keeps decision logic testable without RimWorld assemblies.
using System;
using System.Collections.Generic;

namespace PawnDiary.Capture
{
    /// <summary>
    /// One XML-configurable ritual quality bucket. The first bucket whose maxExclusive is greater
    /// than the ritual value supplies the saved quality label.
    /// </summary>
    public class RitualQualityBand
    {
        public float maxExclusive = 1f;
        public string label;
    }

    /// <summary>
    /// Captured facts for one pawn's perspective on a finished ritual.
    /// </summary>
    internal class RitualEventData : DiaryEventData
    {
        public const string PerspectiveOrganizer = "author";
        public const string PerspectiveTarget = "target";
        public const string PerspectiveParticipant = "participant";
        public const string PerspectiveSpectator = "spectator";
        public const string PerspectiveInvoker = "invoker";
        public const string FallbackRole = "participant";
        public const string FallbackTitle = "ritual";

        public override DiaryEventType EventType => DiaryEventType.Ritual;

        /// <summary>The Precept_Ritual defName.</summary>
        public string DefName;

        /// <summary>The ritual title/label shown to the player.</summary>
        public string Title;

        /// <summary>The ritual behavior worker class name, when available.</summary>
        public string BehaviorClass;

        /// <summary>The pawn's perspective bucket: author, target, participant, or spectator.</summary>
        public string Perspective;

        /// <summary>The specific ritual assignment label, such as speaker, leader, or spectator.</summary>
        public string RitualRole;

        /// <summary>Whether the live ritual was canceled. Canceled rituals do not record.</summary>
        public bool Cancelled;

        /// <summary>
        /// Pure decision for one pawn's ritual entry. Finished, non-canceled, eligible, enabled
        /// ritual perspectives become solo diary entries.
        /// </summary>
        public static CaptureDecision Decide(RitualEventData data, CaptureContext ctx)
        {
            if (data == null || ctx == null || string.IsNullOrEmpty(data.DefName))
            {
                return CaptureDecision.Drop;
            }

            if (data.Cancelled)
            {
                return CaptureDecision.Drop;
            }

            if (!ctx.Eligible || !ctx.UserEnabled || !ctx.SignalEnabled)
            {
                return CaptureDecision.Drop;
            }

            return CaptureDecision.GenerateSolo;
        }

        /// <summary>
        /// Pure assembly of the ritual game-context marker. The leading "ritual=" marker is
        /// load-bearing for domain classification. Field order is locked by tests.
        /// </summary>
        public static string BuildGameContext(
            string defName,
            string title,
            string behaviorClass,
            string perspective,
            string ritualRole,
            string royalTitle,
            string ideologicalRole,
            string outcome,
            string quality)
        {
            return "ritual=" + Clean(defName)
                + "; ritual_title=" + Fallback(title, FallbackTitle)
                + "; ritual_behavior=" + Fallback(behaviorClass, "unknown")
                + "; ritual_perspective=" + Fallback(perspective, FallbackRole)
                + "; ritual_role=" + Fallback(ritualRole, FallbackRole)
                + "; royal_title=" + Fallback(royalTitle, "none")
                + "; ideological_role=" + Fallback(ideologicalRole, "none")
                + "; outcome=" + Fallback(outcome, "finished")
                + "; quality=" + Fallback(quality, "unknown");
        }

        /// <summary>
        /// Pure assembly for Anomaly psychic rituals. These deliberately do not send ritual
        /// role/title fields; the prompt gets only perspective, outcome, and quality facts.
        /// </summary>
        public static string BuildPsychicGameContext(
            string defName,
            string perspective,
            string outcome,
            string quality)
        {
            return "psychic_ritual=" + Clean(defName)
                + "; psychic_ritual_perspective=" + Fallback(perspective, PerspectiveParticipant)
                + "; outcome=" + Fallback(outcome, "finished")
                + "; quality=" + Fallback(quality, "unknown");
        }

        /// <summary>
        /// Combines the XML-owned ritual theme with the pawn's localized role guidance. Keeping the
        /// join here makes the reachability rule testable without RimWorld: a compatibility group may
        /// add flavor, but it must never erase the organizer/target/participant perspective contract.
        /// </summary>
        public static string CombineInstructions(string groupInstruction, string roleInstruction)
        {
            string group = string.IsNullOrWhiteSpace(groupInstruction) ? string.Empty : groupInstruction.Trim();
            string role = string.IsNullOrWhiteSpace(roleInstruction) ? string.Empty : roleInstruction.Trim();
            if (group.Length == 0)
            {
                return role;
            }

            return role.Length == 0 ? group : group + "\n" + role;
        }

        /// <summary>
        /// Converts RimWorld's 0..1-ish ritual strength/progress values into plain prompt words.
        /// The labels are stable schema values, not UI prose.
        /// </summary>
        public static string QualityLabel(float value, IList<RitualQualityBand> bands)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "unknown";
            }

            IList<RitualQualityBand> safeBands = bands == null || bands.Count == 0
                ? DefaultQualityBands()
                : bands;
            for (int i = 0; i < safeBands.Count; i++)
            {
                RitualQualityBand band = safeBands[i];
                if (band == null || string.IsNullOrWhiteSpace(band.label))
                {
                    continue;
                }

                if (value < band.maxExclusive)
                {
                    return Clean(band.label);
                }
            }

            return "unknown";
        }

        /// <summary>
        /// Safe fallback when XML is absent. Normal runtime policy comes from DiaryTuningDef.xml.
        /// </summary>
        public static List<RitualQualityBand> DefaultQualityBands()
        {
            return new List<RitualQualityBand>
            {
                new RitualQualityBand { maxExclusive = 0.25f, label = "terrible" },
                new RitualQualityBand { maxExclusive = 0.5f, label = "weak" },
                new RitualQualityBand { maxExclusive = 0.75f, label = "decent" },
                new RitualQualityBand { maxExclusive = 1f, label = "strong" },
                new RitualQualityBand { maxExclusive = 9999f, label = "excellent" },
            };
        }

        private static string Fallback(string value, string fallback)
        {
            string clean = Clean(value);
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
