// Impure formatter glue: turns the PawnDiary.Integration DTOs the explorer reads into readable
// multi-line strings for the result panel. Field names mirror the public DTO field names verbatim
// (English by design — these are schema labels, the same carve-out as the prompt-schema labels in
// AGENTS.md §12). Values come straight from the API and are already localized by PawnDiary.
//
// Why not pure-test these? The DTOs live in PawnDiary.dll, which references RimWorld — pulling them
// into a console test project would drag RimWorld/Unity in transitively, breaking the "pure tests
// compile without RimWorld" rule. The logic here is straightforward field emission; the load-bearing
// parsing edge cases live in ExplorerParsing, which IS pure-tested.
//
// New to C#? See AGENTS.md.
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Static formatters for every DTO the public PawnDiaryApi returns. Each method returns a clean
    /// multi-line string; null inputs return "(null)" so a returned-null (e.g. pawn not eligible)
    /// still prints something readable in the log.
    /// </summary>
    internal static class SnapshotFormatter
    {
        // ---- readiness / eligibility (value types) --------------------------------

        public static string FormatBool(string methodName, bool value)
        {
            return methodName + " → " + value;
        }

        public static string FormatApiVersion(int version)
        {
            return "PawnDiaryApi.ApiVersion → " + version;
        }

        // ---- SubmitEvent outcome -------------------------------------------------

        public static string FormatSubmitOutcome(bool recorded, SubmitEventOutcome outcome)
        {
            return "SubmitEvent → recorded=" + recorded + ", outcome=" + outcome;
        }

        // ---- submission results --------------------------------------------------

        public static string Format(DiaryEventSubmissionResult result)
        {
            if (result == null)
            {
                return "(null submission result)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryEventSubmissionResult\n");
            sb.Append("  sourceId = ").Append(NullEscape(result.sourceId)).Append('\n');
            sb.Append("  eventKey = ").Append(NullEscape(result.eventKey)).Append('\n');
            sb.Append("  recorded = ").Append(result.recorded).Append('\n');
            sb.Append("  pairwise = ").Append(result.pairwise).Append('\n');
            sb.Append("  primary  = ").Append(Format(result.primary, indent: "    ")).Append('\n');
            sb.Append("  partner  = ").Append(Format(result.partner, indent: "    "));
            return sb.ToString();
        }

        public static string Format(DiaryEntryHandle handle, string indent = "  ")
        {
            if (handle == null)
            {
                return "(null handle)";
            }

            return "eventId=" + NullEscape(handle.eventId)
                + " povRole=" + NullEscape(handle.povRole)
                + " pawnId=" + NullEscape(handle.pawnId)
                + " entryKey=" + NullEscape(handle.entryKey);
        }

        // ---- status / entry snapshots -------------------------------------------

        public static string Format(DiaryEntryStatusSnapshot s)
        {
            if (s == null)
            {
                return "(null status snapshot)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryEntryStatusSnapshot\n");
            AppendStatusFields(sb, s);
            sb.Append("  groupLabel       = ").Append(NullEscape(s.groupLabel)).Append('\n');
            sb.Append("  externallyAuthor = ").Append(s.externallyAuthored).Append('\n');
            sb.Append("  externalSourceId = ").Append(NullEscape(s.externalSourceId)).Append('\n');
            sb.Append("  domain           = ").Append(NullEscape(s.domain)).Append('\n');
            sb.Append("  atmosphereCue    = ").Append(NullEscape(s.atmosphereCue)).Append('\n');
            sb.Append("  summary          = ").Append(NullEscape(s.summary));
            return sb.ToString();
        }

        public static string Format(DiaryEntrySnapshot s)
        {
            if (s == null)
            {
                return "(null entry snapshot)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryEntrySnapshot\n");
            AppendEntryStatusFields(sb,
                s.handle, s.tick, s.date, s.status, s.pending, s.complete, s.failed, s.skipped,
                s.promptOnly, s.archived, s.archivedGenerationStale, s.hasGeneratedText,
                s.title, s.titleStatus, s.titlePending, s.titleComplete);
            sb.Append("  groupLabel       = ").Append(NullEscape(s.groupLabel)).Append('\n');
            sb.Append("  externallyAuthor = ").Append(s.externallyAuthored).Append('\n');
            sb.Append("  externalSourceId = ").Append(NullEscape(s.externalSourceId)).Append('\n');
            sb.Append("  domain           = ").Append(NullEscape(s.domain)).Append('\n');
            sb.Append("  atmosphereCue    = ").Append(NullEscape(s.atmosphereCue)).Append('\n');
            sb.Append("  generatedText    = ").Append(NullEscape(s.generatedText));
            return sb.ToString();
        }

        // The two snapshots above share the same lifecycle/display field block. Emit it once via
        // primitive parameters so the helper works for both DiaryEntryStatusSnapshot and
        // DiaryEntrySnapshot (they have the same field names but no shared base).
        private static void AppendEntryStatusFields(
            StringBuilder sb,
            DiaryEntryHandle handle, int tick, string date, string status,
            bool pending, bool complete, bool failed, bool skipped,
            bool promptOnly, bool archived, bool archivedStale, bool hasGeneratedText,
            string title, string titleStatus, bool titlePending, bool titleComplete)
        {
            sb.Append("  handle           = ").Append(Format(handle)).Append('\n');
            sb.Append("  tick / date      = ").Append(tick).Append(" / ").Append(NullEscape(date)).Append('\n');
            sb.Append("  status           = ").Append(NullEscape(status)).Append('\n');
            sb.Append("  pending/complete = ").Append(pending).Append(" / ").Append(complete).Append('\n');
            sb.Append("  failed/skipped   = ").Append(failed).Append(" / ").Append(skipped).Append('\n');
            sb.Append("  promptOnly       = ").Append(promptOnly).Append('\n');
            sb.Append("  archived / stale = ").Append(archived).Append(" / ").Append(archivedStale).Append('\n');
            sb.Append("  hasGeneratedText = ").Append(hasGeneratedText).Append('\n');
            sb.Append("  title            = ").Append(NullEscape(title)).Append('\n');
            sb.Append("  titleStatus      = ").Append(NullEscape(titleStatus)).Append('\n');
            sb.Append("  titlePend/Compl  = ").Append(titlePending).Append(" / ").Append(titleComplete).Append('\n');
        }

        // Convenience for the DiaryEntryStatusSnapshot (used directly above).
        private static void AppendStatusFields(StringBuilder sb, DiaryEntryStatusSnapshot s)
        {
            AppendEntryStatusFields(sb,
                s.handle, s.tick, s.date, s.status, s.pending, s.complete, s.failed, s.skipped,
                s.promptOnly, s.archived, s.archivedGenerationStale, s.hasGeneratedText,
                s.title, s.titleStatus, s.titlePending, s.titleComplete);
        }

        // ---- title list / context reads -----------------------------------------

        public static string Format(IReadOnlyList<DiaryEntryTitleSnapshot> titles)
        {
            if (titles == null || titles.Count == 0)
            {
                return "GetRecentEntryTitles → (empty list)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("GetRecentEntryTitles → ").Append(titles.Count).Append(" entries\n");
            for (int i = 0; i < titles.Count; i++)
            {
                DiaryEntryTitleSnapshot t = titles[i];
                if (t == null) continue;
                sb.Append("  [").Append(i).Append("] ")
                  .Append(NullEscape(t.date)).Append("  ").Append(NullEscape(t.title))
                  .Append("  (").Append(NullEscape(t.groupLabel)).Append(")")
                  .Append(t.externallyAuthored ? "  ext=" + NullEscape(t.externalSourceId) : string.Empty)
                  .Append(t.archived ? "  (archived)" : string.Empty)
                  .Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        public static string Format(DiaryContextSnapshot ctx)
        {
            if (ctx == null)
            {
                return "(null context snapshot)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryContextSnapshot\n");
            sb.Append("  entryCount = ").Append(ctx.entryCount).Append('\n');
            sb.Append("  range      = ").Append(NullEscape(ctx.oldestDate)).Append(" → ").Append(NullEscape(ctx.newestDate)).Append('\n');
            sb.Append("  entries:\n");
            if (ctx.entries == null)
            {
                sb.Append("    (null)");
                return sb.ToString();
            }

            for (int i = 0; i < ctx.entries.Count; i++)
            {
                DiaryEntryProseSnapshot p = ctx.entries[i];
                if (p == null) continue;
                sb.Append("    [").Append(i).Append("] ")
                  .Append(NullEscape(p.date)).Append("  ").Append(NullEscape(p.title))
                  .Append("  (").Append(NullEscape(p.domain)).Append('/').Append(NullEscape(p.atmosphereCue)).Append(")")
                  .Append('\n');
                sb.Append("         summary: ").Append(NullEscape(p.summary)).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        public static string Format(DiaryEntryStatsSnapshot stats)
        {
            if (stats == null)
            {
                return "(null stats snapshot)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryEntryStatsSnapshot\n");
            sb.Append("  total / active / archived = ").Append(stats.total).Append(" / ").Append(stats.active).Append(" / ").Append(stats.archived).Append('\n');
            sb.Append("  status: complete=").Append(stats.complete)
              .Append(" pending=").Append(stats.pending)
              .Append(" failed=").Append(stats.failed)
              .Append(" skipped=").Append(stats.skipped)
              .Append(" promptOnly=").Append(stats.promptOnly)
              .Append(" notGenerated=").Append(stats.notGenerated).Append('\n');
            sb.Append("  withTitle=").Append(stats.withTitle)
              .Append(" withGeneratedText=").Append(stats.withGeneratedText).Append('\n');
            sb.Append("  range = ").Append(NullEscape(stats.oldestDate)).Append(" → ").Append(NullEscape(stats.newestDate));
            return sb.ToString();
        }

        // ---- machinery reads ----------------------------------------------------

        public static string Format(DiaryPawnSummarySnapshot s)
        {
            if (s == null)
            {
                return "(null pawn summary)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryPawnSummarySnapshot\n");
            sb.Append("  sex        = ").Append(NullEscape(s.sex)).Append('\n');
            sb.Append("  lifeStage  = ").Append(NullEscape(s.lifeStage)).Append('\n');
            sb.Append("  xenotype   = ").Append(NullEscape(s.xenotype)).Append('\n');
            sb.Append("  royalTitle = ").Append(NullEscape(s.royalTitle)).Append('\n');
            sb.Append("  faith      = ").Append(NullEscape(s.faith)).Append('\n');
            sb.Append("  mood       = ").Append(NullEscape(s.mood)).Append('\n');
            sb.Append("  health     = ").Append(Format(s.health)).Append('\n');
            sb.Append("  lowCapacit = ").Append(JoinList(s.lowCapacities)).Append('\n');
            sb.Append("  topThought = ").Append(JoinList(s.topThoughts)).Append('\n');
            sb.Append("  providerLn = ").Append(JoinList(s.providerLines));
            return sb.ToString();
        }

        public static string Format(DiaryHealthSummarySnapshot h)
        {
            if (h == null)
            {
                return "(null health)";
            }

            return "downed=" + h.downed
                + " painShock=" + h.painShock
                + " pain=[" + NullEscape(h.pain) + "]"
                + " bleeding=[" + NullEscape(h.bleeding) + "]"
                + " conditions=[" + JoinList(h.conditions) + "]";
        }

        public static string Format(IReadOnlyList<DiaryPromptEnchantmentCandidateSnapshot> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return "GetPromptEnchantments → (empty list)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("GetPromptEnchantments → ").Append(candidates.Count).Append(" candidates\n");
            for (int i = 0; i < candidates.Count; i++)
            {
                DiaryPromptEnchantmentCandidateSnapshot c = candidates[i];
                if (c == null) continue;
                sb.Append("  [").Append(i).Append("] weight=").Append(c.weight)
                  .Append("  hediff=").Append(NullEscape(c.sourceHediffDefName)).Append('\n');
                sb.Append("        priority: ").Append(NullEscape(c.priorityText)).Append('\n');
                sb.Append("        condition: ").Append(NullEscape(c.conditionText)).Append('\n');
                sb.Append("        impactCues: [").Append(JoinList(c.impactCues)).Append("]\n");
                sb.Append("        configuredCues: [").Append(JoinList(c.configuredCues)).Append("]");
                if (i < candidates.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Format(DiaryContextBundleSnapshot b)
        {
            if (b == null)
            {
                return "(null context bundle)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("=== DiaryContextBundleSnapshot ===\n");
            sb.Append("--- writingStyle ---\n").Append(Format(b.writingStyle)).Append("\n\n");
            sb.Append("--- pawnSummary ---\n").Append(Format(b.pawnSummary)).Append("\n\n");
            sb.Append("--- promptEnchantments ---\n").Append(Format(b.promptEnchantments as IReadOnlyList<DiaryPromptEnchantmentCandidateSnapshot>)).Append("\n\n");
            sb.Append("--- recentContext ---\n").Append(Format(b.recentContext));
            return sb.ToString();
        }

        public static string Format(DiaryWritingStyleSnapshot style)
        {
            if (style == null)
            {
                return "(null writing style)";
            }

            return "DiaryWritingStyleSnapshot\n"
                + "  styleDefName = " + NullEscape(style.styleDefName) + "\n"
                + "  label        = " + NullEscape(style.label) + "\n"
                + "  rule         = " + NullEscape(style.rule);
        }

        public static string FormatStyles(IReadOnlyList<DiaryWritingStyleSnapshot> styles)
        {
            if (styles == null || styles.Count == 0)
            {
                return "GetAvailableWritingStyles → (empty list)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("GetAvailableWritingStyles → ").Append(styles.Count).Append(" styles\n");
            for (int i = 0; i < styles.Count; i++)
            {
                DiaryWritingStyleSnapshot s = styles[i];
                if (s == null) continue;
                sb.Append("  [").Append(i).Append("] ").Append(NullEscape(s.styleDefName))
                  .Append("  (").Append(NullEscape(s.label)).Append(")\n");
                sb.Append("        rule: ").Append(NullEscape(s.rule));
                if (i < styles.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Format(DiaryPromptPreviewSnapshot p)
        {
            if (p == null)
            {
                return "(null preview — request was declined or threw)";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("DiaryPromptPreviewSnapshot\n");
            sb.Append("  sourceId / eventKey = ").Append(NullEscape(p.sourceId)).Append(" / ").Append(NullEscape(p.eventKey)).Append('\n');
            sb.Append("  povRole / pairwise  = ").Append(NullEscape(p.povRole)).Append(" / ").Append(p.pairwise).Append('\n');
            sb.Append("  subjectPawnId       = ").Append(NullEscape(p.subjectPawnId)).Append('\n');
            sb.Append("  partnerPawnId       = ").Append(NullEscape(p.partnerPawnId)).Append('\n');
            sb.Append("  groupDefName        = ").Append(NullEscape(p.groupDefName)).Append('\n');
            sb.Append("  templateKey         = ").Append(NullEscape(p.templateKey)).Append('\n');
            sb.Append("  eventPromptKey      = ").Append(NullEscape(p.eventPromptKey)).Append('\n');
            sb.Append("  forcedModelName     = ").Append(NullEscape(p.forcedModelName)).Append('\n');
            sb.Append("  maxTokens           = ").Append(p.maxTokens).Append('\n');
            sb.Append("  requiresPriorPovText= ").Append(p.requiresPriorPovText).Append('\n');
            sb.Append("  ----- systemPrompt -----\n").Append(NullEscape(p.systemPrompt)).Append('\n');
            sb.Append("  ----- userPrompt ------\n").Append(NullEscape(p.userPrompt)).Append('\n');
            sb.Append("  ----- combinedPrompt --\n").Append(NullEscape(p.combinedPrompt));
            return sb.ToString();
        }

        // ---- helpers ------------------------------------------------------------

        private static string NullEscape(string s)
        {
            return string.IsNullOrEmpty(s) ? "(empty)" : s;
        }

        private static string JoinList(IReadOnlyList<string> list)
        {
            if (list == null || list.Count == 0)
            {
                return "(empty)";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(list[i]);
            }
            return sb.ToString();
        }
    }
}
