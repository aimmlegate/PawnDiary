// The interaction-group catalog is data-driven: each group is a DiaryInteractionGroupDef
// loaded from XML (1.6/Defs/DiaryInteractionGroupDefs.xml), so groups, their matchers, and
// their diary prompts can be retuned by editing XML and restarting — no recompile. This file
// defines the Def type plus the static lookup/classification helpers over the DefDatabase.
// New to C#/RimWorld? See AGENTS.md ("Defs & DefDatabase").
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Which kind of game event a group classifies. Interaction groups match InteractionDefs
    // (social log entries); MentalState groups match MentalStateDefs (breaks, social fights);
    // Tale groups match TaleDefs (RimWorld's notable-history events); MoodEvent groups match
    // GameConditionDefs that affect colonist mood (aurora, eclipse, psychic drone, etc.).
    // RimWorld parses this enum straight from XML text (e.g. <domain>MentalState</domain>).
    public enum GroupDomain
    {
        Interaction,
        MentalState,
        Tale,
        MoodEvent
    }

    // A themed bucket of events, loaded from XML as a RimWorld Def. Each group is one row in
    // settings: an enable toggle (is it recorded?) plus a single diary-prompt instruction
    // shared by every event in it. To add or retune a group, edit
    // 1.6/Defs/DiaryInteractionGroupDefs.xml — no code change needed.
    //
    // `Def` (the base class) already supplies two fields we rely on:
    //   - defName : the stable key (e.g. "nsfw"). Settings store per-group overrides under it,
    //               so renaming a defName would orphan a player's saved settings.
    //   - label   : the human-readable name shown in the settings UI.
    public class DiaryInteractionGroupDef : Def
    {
        // Whether events in this group are recorded by default (a player can override per-save).
        public bool defaultEnabled = true;

        // Whether entries from this group should be visually marked as important in the Diary tab.
        // Low-stakes groups can set this false in XML without changing save data or code.
        public bool important = true;

        // Whether events in this group are combat-related (social fights, insults). Used to decide
        // whether to add the equipped weapon to the prompt; set per group in XML, default false.
        public bool combat = false;

        // The diary-prompt instruction shared by every event in the group.
        public string instruction;

        // Interaction (InteractionDef) vs MentalState (MentalStateDef). Classification is scoped
        // to a domain so the two never cross-match.
        public GroupDomain domain = GroupDomain.Interaction;

        // Exact defName matches (case-insensitive). Optional in XML.
        public List<string> matchDefNames;

        // Substring tokens: a defName that contains any token (case-insensitive) matches. Optional.
        public List<string> matchTokens;

        // When true this group matches everything in its domain (the catch-all). Give it the
        // highest `order` in its domain so the specific groups get first claim.
        public bool catchAll = false;

        // Classification order within a domain: lower numbers are tested first ("first match
        // wins"). Def load order across files is not guaranteed, so this keeps it deterministic.
        public int order = 0;

        // True if this group claims the given defName. Check order mirrors the old catalog:
        // catch-all, then exact defNames, then substring tokens.
        public bool Matches(string defName)
        {
            if (catchAll)
            {
                return true;
            }

            if (string.IsNullOrEmpty(defName))
            {
                return false;
            }

            if (matchDefNames != null)
            {
                for (int i = 0; i < matchDefNames.Count; i++)
                {
                    if (string.Equals(matchDefNames[i], defName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (matchTokens != null)
            {
                for (int i = 0; i < matchTokens.Count; i++)
                {
                    if (defName.IndexOf(matchTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // Static lookup + classification over the loaded DiaryInteractionGroupDefs. (A static class
    // is a namespace-level singleton — no instances; see AGENTS.md.)
    public static class InteractionGroups
    {
        private static List<DiaryInteractionGroupDef> cachedAll;

        // All groups, sorted by `order` so "first match wins" is deterministic. Cached after the
        // first call because Defs are loaded once at startup and don't change during play.
        public static List<DiaryInteractionGroupDef> All
        {
            get
            {
                if (cachedAll == null)
                {
                    cachedAll = DefDatabase<DiaryInteractionGroupDef>.AllDefsListForReading
                        .OrderBy(group => group.order)
                        .ToList();
                }

                return cachedAll;
            }
        }

        // First Interaction-domain group that matches the interaction, else the Interaction
        // catch-all ("Other").
        public static DiaryInteractionGroupDef Classify(InteractionDef interactionDef)
        {
            return ClassifyIn(GroupDomain.Interaction, interactionDef?.defName);
        }

        // First MentalState-domain group that matches the state, else the MentalState catch-all
        // ("Mental breaks").
        public static DiaryInteractionGroupDef ClassifyMentalState(MentalStateDef stateDef)
        {
            return ClassifyIn(GroupDomain.MentalState, stateDef?.defName);
        }

        // First Tale-domain group that matches the tale, else the Tale catch-all.
        public static DiaryInteractionGroupDef ClassifyTale(TaleDef taleDef)
        {
            return ClassifyIn(GroupDomain.Tale, taleDef?.defName);
        }

        // First MoodEvent-domain group that matches the GameConditionDef, else the MoodEvent catch-all.
        public static DiaryInteractionGroupDef ClassifyMoodEvent(GameConditionDef conditionDef)
        {
            return ClassifyIn(GroupDomain.MoodEvent, conditionDef?.defName);
        }

        // Same classifier, but for saved events where we only have the stored defName string.
        // The Diary tab uses this to color-mark old and new entries without adding save fields.
        public static DiaryInteractionGroupDef ClassifyDefName(GroupDomain domain, string defName)
        {
            return ClassifyIn(domain, defName);
        }

        private static DiaryInteractionGroupDef ClassifyIn(GroupDomain domain, string defName)
        {
            DiaryInteractionGroupDef fallback = null;
            List<DiaryInteractionGroupDef> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                DiaryInteractionGroupDef group = all[i];
                if (group.domain != domain)
                {
                    continue;
                }

                if (group.Matches(defName))
                {
                    return group;
                }

                fallback = group;
            }

            return fallback;
        }

        // Look up a group by its defName/key (used to read per-group settings). Null if absent.
        public static DiaryInteractionGroupDef ByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return DefDatabase<DiaryInteractionGroupDef>.GetNamedSilentFail(key);
        }
    }
}
