// XML-backed diary text decoration rules.
//
// This file is intentionally a thin Def adapter. The rule classes and text transforms live in the
// pure pipeline layer (DiaryTextDecorations.cs); this Def only lets RimWorld load the rule list from
// XML and provides a code fallback if the XML is missing.
using System.Collections.Generic;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Root XML Def for diary text decoration rules.
    /// </summary>
    public class DiaryTextDecorationDef : Def
    {
        public List<DiaryTextDecorationRule> rules = new List<DiaryTextDecorationRule>();
    }

    /// <summary>
    /// Loads the active decoration rules from XML. Falls back to the same defaults if the Def is absent.
    /// </summary>
    public static class DiaryTextDecorationDefs
    {
        private static DiaryTextDecorationDef cached;
        // Defs load once at startup, so we resolve the lookup a single time. `resolved` lets us
        // remember a genuine "Def absent" result instead of re-querying DefDatabase on every access.
        private static bool resolved;
        private static List<DiaryTextDecorationRule> fallbackCached;

        public static List<DiaryTextDecorationRule> CurrentRules
        {
            get
            {
                if (!resolved)
                {
                    cached = DefDatabase<DiaryTextDecorationDef>.GetNamedSilentFail("Diary_TextDecorations");
                    resolved = true;
                    WarnUnknownDecorationKinds(cached);
                }

                if (cached != null && cached.rules != null)
                {
                    return cached.rules;
                }

                // This getter runs per text block per frame from the diary UI, so build the fallback
                // list once and reuse it rather than re-allocating every call when the Def is absent.
                return fallbackCached ?? (fallbackCached = FallbackRules());
            }
        }

        // Logs once (at first Def resolution) if the XML declares a decoration kind that has no
        // registered renderer in DiaryRichTextDecorators. Selection is data-driven, so such a rule
        // would be matched and sorted but render nothing; surfacing it here turns a silent no-op into
        // an actionable warning for pack authors. The built-in FallbackRules only use known kinds, so
        // they never trip this.
        private static void WarnUnknownDecorationKinds(DiaryTextDecorationDef def)
        {
            if (def?.rules == null)
            {
                return;
            }

            HashSet<string> reported = null;
            for (int i = 0; i < def.rules.Count; i++)
            {
                DiaryTextDecorationRule rule = def.rules[i];
                string kind = rule?.decoration;
                if (string.IsNullOrWhiteSpace(kind) || DiaryRichTextDecorators.IsKnownKind(kind))
                {
                    continue;
                }

                reported = reported ?? new HashSet<string>();
                if (reported.Add(kind.Trim()))
                {
                    Log.Warning("[PawnDiary] DiaryTextDecorationDef rule uses unknown decoration kind '"
                        + kind.Trim()
                        + "'; it will be selected but render nothing. Known kinds: "
                        + DiaryTextDecorationKinds.StaggeredWordSizes + ", "
                        + DiaryTextDecorationKinds.DimmedWords + ", "
                        + DiaryTextDecorationKinds.Zalgo + ".");
                }
            }
        }

        private static List<DiaryTextDecorationRule> FallbackRules()
        {
            return new List<DiaryTextDecorationRule>
            {
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.StaggeredWordSizes,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 20,
                    intensity = 4,
                    when = new DiaryTextDecorationCondition
                    {
                        anyHediffDefName = new List<string>
                        {
                            "AlcoholHigh",
                            "SmokeleafHigh",
                            "PsychiteHigh",
                            "YayoHigh",
                            "FlakeHigh",
                            "WakeUpHigh",
                            "GoJuiceHigh",
                            "Anesthetic",
                            "Anesthesia"
                        },
                        anyHediffDefNameContains = new List<string>
                        {
                            "Smokeleaf",
                            "Psychite",
                            "Yayo",
                            "Flake",
                            "WakeUp",
                            "Wake-Up",
                            "GoJuice",
                            "Go-Juice",
                            "Anesth"
                        },
                        anyHediffLabelContains = new List<string>
                        {
                            "drunk",
                            "alcohol",
                            "hangover",
                            "smokeleaf",
                            "psychite",
                            "yayo",
                            "flake",
                            "wake-up",
                            "wakeup",
                            "go-juice",
                            "gojuice",
                            "anesth"
                        }
                    }
                },
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.DimmedWords,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 30,
                    intensity = 3,
                    when = new DiaryTextDecorationCondition
                    {
                        anyColorCue = new List<string>
                        {
                            DiaryEvent.ExtremeDarkColorCue
                        }
                    }
                },
                new DiaryTextDecorationRule
                {
                    decoration = DiaryTextDecorationKinds.Zalgo,
                    scope = DiaryTextDecorationScopes.DirectSpeech,
                    sequence = 30,
                    intensity = 1,
                    when = new DiaryTextDecorationCondition
                    {
                        anyColorCue = new List<string>
                        {
                            DiaryEvent.StrangeChatColorCue
                        }
                    }
                }
            };
        }
    }
}
