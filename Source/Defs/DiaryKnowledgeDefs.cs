// DiaryKnowledgeDefs.cs — Def classes for the deterministic pawn-knowledge system
// (design/MEMORY_SYSTEM_REDESIGN_PLAN.md §5): the XML-owned important-event allowlist, the
// culture interpretation topics, the per-CultureDef profiles, and the tuning singleton. This file
// replaces the old DiaryMemoryTuningDef.cs / DiaryLoreSeedDef.cs.
//
// All cross-references are plain strings (CultureDef names, interaction defNames, hediff
// defNames), never Def references — absent DLC content cleanly no-ops (AGENTS.md "DLC-safety").
//
// New to C#/RimWorld? A Def is a data object loaded from XML at startup; `defName` is its unique
// id. The `To*` copiers translate Defs into the plain DTO snapshots the pure pipeline consumes,
// so Verse types never leak past this file.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// One row of the closed important-event allowlist (§2.1). External mods add their own kinds
    /// by shipping more of these Defs — no C# needed.
    /// </summary>
    public class DiaryImportantEventDef : Def
    {
        /// <summary>Stable event-kind token stored in saved records. Never rename.</summary>
        public string eventKind;
        /// <summary>Ranking family for retrieval tier 3 (§3.1), e.g. "relationship"/"body".</summary>
        public string topicKey;
        /// <summary>Capture channel: event, hediffQuiet, hediffRemoved, roleAssigned,
        /// roleUnassigned, ideoConversion, deathInstigator, deathFamily.</summary>
        public string signal = KnowledgeTokens.SignalEvent;
        /// <summary>Ascending evaluation order; first matching row wins within a channel.</summary>
        public int order = 100;
        public List<string> matchDefNames = new List<string>();
        public List<string> matchSuffixes = new List<string>();
        /// <summary>Extra gameContext gates: "key=" (present, non-sentinel) or "key=value".</summary>
        public List<string> requireContext = new List<string>();
        /// <summary>initiator / recipient / both (event channel) or provided (other channels).</summary>
        public string owners = KnowledgeTokens.OwnersBoth;
        public List<DiaryKnowledgeSubjectKeyRow> subjectKeys = new List<DiaryKnowledgeSubjectKeyRow>();
        /// <summary>Additional pawn identities copied from structured context (for example a newborn
        /// child who is the subject but not necessarily one of the two adult page writers).</summary>
        public List<DiaryKnowledgeParticipantKeyRow> participantKeys =
            new List<DiaryKnowledgeParticipantKeyRow>();
        public List<string> constantSubjectKeys = new List<string>();
        /// <summary>gameContext keys copied into the record's fact rows.</summary>
        public List<string> factKeys = new List<string>();
        /// <summary>Localized one-line fact template ("married {other}"). DefInjected translates
        /// it; "{other}" and "{&lt;factKey&gt;}" placeholders are substituted at render time.</summary>
        public string lineTemplate;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (string.IsNullOrWhiteSpace(eventKind))
            {
                yield return "eventKind must be a stable non-blank token.";
            }

            bool hasMatcher = HasAny(matchDefNames) || HasAny(matchSuffixes) || HasAny(requireContext);
            if (string.Equals(signal, KnowledgeTokens.SignalEvent, System.StringComparison.OrdinalIgnoreCase)
                && !hasMatcher)
            {
                yield return "event-channel rows need matchDefNames, matchSuffixes, or requireContext.";
            }
        }

        private static bool HasAny(List<string> values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal ImportantEventRule ToRule()
        {
            ImportantEventRule rule = new ImportantEventRule
            {
                defName = defName,
                enabled = true,
                eventKind = eventKind ?? string.Empty,
                topicKey = topicKey ?? string.Empty,
                signal = string.IsNullOrWhiteSpace(signal) ? KnowledgeTokens.SignalEvent : signal.Trim(),
                order = order,
                owners = string.IsNullOrWhiteSpace(owners) ? KnowledgeTokens.OwnersBoth : owners.Trim(),
                lineTemplate = lineTemplate ?? string.Empty
            };
            CopyStrings(matchDefNames, rule.matchDefNames);
            CopyStrings(matchSuffixes, rule.matchSuffixes);
            CopyStrings(requireContext, rule.requireContext);
            CopyStrings(constantSubjectKeys, rule.constantSubjectKeys);
            CopyStrings(factKeys, rule.factKeys);
            if (subjectKeys != null)
            {
                for (int i = 0; i < subjectKeys.Count; i++)
                {
                    DiaryKnowledgeSubjectKeyRow row = subjectKeys[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.contextKey))
                    {
                        rule.subjectKeyRules.Add(new KnowledgeSubjectKeyRule
                        {
                            contextKey = row.contextKey.Trim(),
                            prefix = (row.prefix ?? string.Empty).Trim()
                        });
                    }
                }
            }

            if (participantKeys != null)
            {
                for (int i = 0; i < participantKeys.Count; i++)
                {
                    DiaryKnowledgeParticipantKeyRow row = participantKeys[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.contextKey))
                    {
                        rule.participantKeyRules.Add(new KnowledgeParticipantKeyRule
                        {
                            contextKey = row.contextKey.Trim(),
                            nameContextKey = (row.nameContextKey ?? string.Empty).Trim()
                        });
                    }
                }
            }

            return rule;
        }

        internal static void CopyStrings(List<string> source, List<string> target)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(source[i]))
                {
                    target.Add(source[i].Trim());
                }
            }
        }
    }

    /// <summary>One subject-key extraction row: gameContext key → "prefix:value" subject key.</summary>
    public class DiaryKnowledgeSubjectKeyRow
    {
        public string contextKey;
        public string prefix;
    }

    /// <summary>One extra participant extraction row: id key plus optional display-name key.</summary>
    public class DiaryKnowledgeParticipantKeyRow
    {
        public string contextKey;
        public string nameContextKey;
    }

    /// <summary>One cultural interpretation topic (§4.2) with structured triggers (§4.3).</summary>
    public class DiaryCultureTopicDef : Def
    {
        public string topicKey;
        /// <summary>Ascending priority; lower fires first when more topics match than the cap.</summary>
        public int order = 100;
        public List<string> triggerContextKeys = new List<string>();
        /// <summary>"key=value" rows matched against rendered GameContext fields.</summary>
        public List<string> triggerContextPairs = new List<string>();
        /// <summary>Stable schema markers ("xenotype=") searched in scannable field text.</summary>
        public List<string> triggerValueMarkers = new List<string>();
        public List<string> triggerDefNames = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (string.IsNullOrWhiteSpace(topicKey))
            {
                yield return "topicKey must be a stable non-blank token.";
            }
        }

        internal CultureTopicRule ToRule()
        {
            CultureTopicRule rule = new CultureTopicRule
            {
                topicKey = topicKey ?? string.Empty,
                enabled = true,
                order = order
            };
            DiaryImportantEventDef.CopyStrings(triggerContextKeys, rule.triggerContextKeys);
            DiaryImportantEventDef.CopyStrings(triggerContextPairs, rule.triggerContextPairs);
            DiaryImportantEventDef.CopyStrings(triggerValueMarkers, rule.triggerValueMarkers);
            DiaryImportantEventDef.CopyStrings(triggerDefNames, rule.triggerDefNames);
            return rule;
        }
    }

    /// <summary>One authored clause row: cultural stance for one topic, ≤80 chars (§4.2).</summary>
    public class DiaryCultureClauseRow
    {
        public string topicKey;
        public string clause;
    }

    /// <summary>
    /// The writing lens of one CultureDef (§4.2), keyed by name string so a missing DLC culture
    /// (Sophian without Royalty) simply never resolves. Mods add profiles for their own cultures;
    /// a known culture WITHOUT a profile falls back to the isFallback profile (Astropolitan).
    /// </summary>
    public class DiaryCultureProfileDef : Def
    {
        public string cultureDefName;
        /// <summary>Exactly one shipped profile is the fallback lens for modded cultures that
        /// provide no profile of their own.</summary>
        public bool isFallback;
        public List<DiaryCultureClauseRow> clauses = new List<DiaryCultureClauseRow>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (string.IsNullOrWhiteSpace(cultureDefName))
            {
                yield return "cultureDefName must name a CultureDef (as a plain string).";
            }

            if (clauses != null)
            {
                for (int i = 0; i < clauses.Count; i++)
                {
                    DiaryCultureClauseRow row = clauses[i];
                    if (row == null || string.IsNullOrWhiteSpace(row.topicKey))
                    {
                        yield return "clause row " + i + " needs a topicKey.";
                    }
                    else if (!string.IsNullOrWhiteSpace(row.clause) && row.clause.Trim().Length > 80)
                    {
                        yield return "clause for topic '" + row.topicKey + "' exceeds 80 characters.";
                    }
                }
            }
        }

        internal CultureProfile ToProfile()
        {
            CultureProfile profile = new CultureProfile
            {
                cultureDefName = (cultureDefName ?? string.Empty).Trim()
            };
            if (clauses != null)
            {
                for (int i = 0; i < clauses.Count; i++)
                {
                    DiaryCultureClauseRow row = clauses[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.topicKey))
                    {
                        profile.clauses.Add(new CultureClause
                        {
                            topicKey = row.topicKey.Trim(),
                            clause = (row.clause ?? string.Empty).Trim()
                        });
                    }
                }
            }

            return profile;
        }
    }

    /// <summary>Tuning singleton for caps, prompt shape, and annotation policy (§2.3, §3.2, §4.3).</summary>
    public class DiaryKnowledgeTuningDef : Def
    {
        /// <summary>XML kill-switch ANDed with the player's injection setting.</summary>
        public bool enabled = true;
        public int maxRecordsPerPawn = 512;
        public int maxRecordsGlobal = 20000;
        public int fallbackSummaryMaxChars = 240;
        public int relevantPastMaxLines = 2;
        public int relevantPastMaxChars = 500;
        public string relevantPastLineFormat = "- ({0}) {1}";
        /// <summary>Prompt guidance: past context, used only when natural — never a demand.</summary>
        public string relevantPastInstruction;
        public int maxCultureTopicsPerPrompt = 2;
        public string annotationSingleFormat = "(culture: {0})";
        public string annotationDualFormat = "(origin: {0}; adopted: {1})";
        public List<string> scannableSources = new List<string>();
        public List<DiaryKnowledgeSubjectKeyRow> querySubjectKeys = new List<DiaryKnowledgeSubjectKeyRow>();
        /// <summary>Global-cap eviction scan cadence.</summary>
        public int evictionScanIntervalTicks = 150000;
    }

    /// <summary>
    /// Impure accessor: snapshots the knowledge Defs + the player's injection switch into the pure
    /// policy DTOs. Caches are session-static and reset from the DiaryGameComponent constructor
    /// (statics leak across exit-to-menu + load — see AGENTS.md).
    /// </summary>
    internal static class DiaryKnowledgePolicy
    {
        public const string TuningDefName = "Diary_Knowledge";

        private static List<ImportantEventRule> cachedRules;
        private static List<CultureTopicRule> cachedTopics;
        private static Dictionary<string, CultureProfile> cachedProfiles;
        private static CultureProfile cachedFallbackProfile;
        private static Dictionary<string, ImportantEventRule> cachedRulesByKind;

        public static void ResetCache()
        {
            cachedRules = null;
            cachedTopics = null;
            cachedProfiles = null;
            cachedFallbackProfile = null;
            cachedRulesByKind = null;
        }

        /// <summary>
        /// The rule that owns one saved eventKind token — the source of the CURRENT-language line
        /// template when re-rendering a stored record. Null when the Def is gone (mod removed);
        /// the record's capture-time fallbackSummary covers that (§5).
        /// </summary>
        public static ImportantEventRule RuleForKind(string eventKind)
        {
            if (string.IsNullOrWhiteSpace(eventKind))
            {
                return null;
            }

            if (cachedRulesByKind == null)
            {
                cachedRulesByKind = new Dictionary<string, ImportantEventRule>();
                List<ImportantEventRule> rules = ImportantEventRules();
                for (int i = 0; i < rules.Count; i++)
                {
                    ImportantEventRule rule = rules[i];
                    if (rule != null && !string.IsNullOrWhiteSpace(rule.eventKind)
                        && !cachedRulesByKind.ContainsKey(rule.eventKind))
                    {
                        cachedRulesByKind.Add(rule.eventKind, rule);
                    }
                }
            }

            ImportantEventRule found;
            return cachedRulesByKind.TryGetValue(eventKind.Trim(), out found) ? found : null;
        }

        /// <summary>The full pure policy snapshot: XML tuning ANDed with the player switch.</summary>
        public static KnowledgePolicySnapshot Snapshot()
        {
            KnowledgePolicySnapshot policy = KnowledgePolicySnapshot.CreateDefault();
            DiaryKnowledgeTuningDef tuning = DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail(TuningDefName);
            bool settingEnabled = PawnDiaryMod.Settings == null || PawnDiaryMod.Settings.enableMemorySystem;
            if (tuning == null)
            {
                policy.injectionEnabled = settingEnabled;
                return policy;
            }

            policy.injectionEnabled = tuning.enabled && settingEnabled;
            policy.maxRecordsPerPawn = tuning.maxRecordsPerPawn;
            policy.maxRecordsGlobal = tuning.maxRecordsGlobal;
            policy.fallbackSummaryMaxChars = tuning.fallbackSummaryMaxChars;
            policy.relevantPastMaxLines = tuning.relevantPastMaxLines;
            policy.relevantPastMaxChars = tuning.relevantPastMaxChars;
            if (!string.IsNullOrWhiteSpace(tuning.relevantPastLineFormat))
            {
                policy.relevantPastLineFormat = tuning.relevantPastLineFormat;
            }

            policy.relevantPastInstruction = tuning.relevantPastInstruction ?? string.Empty;
            policy.maxCultureTopicsPerPrompt = tuning.maxCultureTopicsPerPrompt;
            if (!string.IsNullOrWhiteSpace(tuning.annotationSingleFormat))
            {
                policy.annotationSingleFormat = tuning.annotationSingleFormat;
            }

            if (!string.IsNullOrWhiteSpace(tuning.annotationDualFormat))
            {
                policy.annotationDualFormat = tuning.annotationDualFormat;
            }

            if (tuning.scannableSources != null && tuning.scannableSources.Count > 0)
            {
                policy.scannableSources.Clear();
                DiaryImportantEventDef.CopyStrings(tuning.scannableSources, policy.scannableSources);
            }

            if (tuning.querySubjectKeys != null && tuning.querySubjectKeys.Count > 0)
            {
                policy.querySubjectKeyRules.Clear();
                for (int i = 0; i < tuning.querySubjectKeys.Count; i++)
                {
                    DiaryKnowledgeSubjectKeyRow row = tuning.querySubjectKeys[i];
                    if (row != null && !string.IsNullOrWhiteSpace(row.contextKey))
                    {
                        policy.querySubjectKeyRules.Add(new KnowledgeSubjectKeyRule
                        {
                            contextKey = row.contextKey.Trim(),
                            prefix = (row.prefix ?? string.Empty).Trim()
                        });
                    }
                }
            }

            return policy;
        }

        public static int EvictionScanIntervalTicks()
        {
            DiaryKnowledgeTuningDef tuning = DefDatabase<DiaryKnowledgeTuningDef>.GetNamedSilentFail(TuningDefName);
            return tuning != null ? tuning.evictionScanIntervalTicks : 150000;
        }

        /// <summary>All important-event rules (allowlist), copied once per session.</summary>
        public static List<ImportantEventRule> ImportantEventRules()
        {
            if (cachedRules == null)
            {
                cachedRules = new List<ImportantEventRule>();
                foreach (DiaryImportantEventDef def in DefDatabase<DiaryImportantEventDef>.AllDefsListForReading)
                {
                    if (def != null)
                    {
                        cachedRules.Add(def.ToRule());
                    }
                }
            }

            return cachedRules;
        }

        /// <summary>All culture topics, copied once per session.</summary>
        public static List<CultureTopicRule> CultureTopics()
        {
            if (cachedTopics == null)
            {
                cachedTopics = new List<CultureTopicRule>();
                foreach (DiaryCultureTopicDef def in DefDatabase<DiaryCultureTopicDef>.AllDefsListForReading)
                {
                    if (def != null)
                    {
                        cachedTopics.Add(def.ToRule());
                    }
                }
            }

            return cachedTopics;
        }

        /// <summary>
        /// Profile for one CultureDef name. A KNOWN culture without an authored profile uses the
        /// fallback (Astropolitan) lens; a blank culture name returns null — no invented culture.
        /// </summary>
        public static CultureProfile ProfileFor(string cultureDefName)
        {
            if (string.IsNullOrWhiteSpace(cultureDefName))
            {
                return null;
            }

            EnsureProfilesBuilt();
            CultureProfile profile;
            if (cachedProfiles.TryGetValue(cultureDefName.Trim().ToLowerInvariant(), out profile))
            {
                return profile;
            }

            return cachedFallbackProfile;
        }

        /// <summary>True when an authored (non-fallback) profile exists for the culture — the dev
        /// tab's "profile found/missing" line (§7).</summary>
        public static bool HasAuthoredProfile(string cultureDefName)
        {
            if (string.IsNullOrWhiteSpace(cultureDefName))
            {
                return false;
            }

            EnsureProfilesBuilt();
            return cachedProfiles.ContainsKey(cultureDefName.Trim().ToLowerInvariant());
        }

        /// <summary>All authored profiles as pure DTOs (for the prompt policy snapshot).</summary>
        public static List<CultureProfile> AllProfiles()
        {
            EnsureProfilesBuilt();
            List<CultureProfile> profiles = new List<CultureProfile>(cachedProfiles.Count);
            foreach (KeyValuePair<string, CultureProfile> pair in cachedProfiles)
            {
                profiles.Add(pair.Value);
            }

            return profiles;
        }

        /// <summary>The fallback lens for modded cultures without a profile (Astropolitan).</summary>
        public static CultureProfile FallbackProfile()
        {
            EnsureProfilesBuilt();
            return cachedFallbackProfile;
        }

        private static void EnsureProfilesBuilt()
        {
            if (cachedProfiles != null)
            {
                return;
            }

            cachedProfiles = new Dictionary<string, CultureProfile>();
            cachedFallbackProfile = null;
            foreach (DiaryCultureProfileDef def in DefDatabase<DiaryCultureProfileDef>.AllDefsListForReading)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.cultureDefName))
                {
                    continue;
                }

                CultureProfile profile = def.ToProfile();
                cachedProfiles[def.cultureDefName.Trim().ToLowerInvariant()] = profile;
                if (def.isFallback && cachedFallbackProfile == null)
                {
                    cachedFallbackProfile = profile;
                }
            }
        }
    }
}
