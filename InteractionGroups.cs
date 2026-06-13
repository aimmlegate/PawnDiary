using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnDiary
{
    // Which kind of game event a group classifies. Interaction groups match InteractionDefs
    // (social log entries); MentalState groups match MentalStateDefs (breaks, social fights).
    public enum GroupDomain
    {
        Interaction,
        MentalState
    }

    // A themed bucket of events. Each group is one row in settings: an enable toggle plus a
    // single diary prompt instruction shared by every event in it. This replaces both the old
    // per-defName "significant" allowlist and the per-defName instruction map.
    public sealed class InteractionGroup
    {
        public readonly string Key;
        public readonly string Label;
        public readonly bool DefaultEnabled;
        public readonly string DefaultInstruction;
        public readonly GroupDomain Domain;

        private readonly HashSet<string> defNames;
        private readonly string[] tokens;
        private readonly bool catchAll;

        public InteractionGroup(string key, string label, bool defaultEnabled, string defaultInstruction,
            IEnumerable<string> defNames, IEnumerable<string> tokens,
            GroupDomain domain = GroupDomain.Interaction, bool catchAll = false)
        {
            Key = key;
            Label = label;
            DefaultEnabled = defaultEnabled;
            DefaultInstruction = defaultInstruction;
            Domain = domain;
            this.defNames = new HashSet<string>(defNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            this.tokens = (tokens ?? Enumerable.Empty<string>()).ToArray();
            this.catchAll = catchAll;
        }

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

            if (defNames.Contains(defName))
            {
                return true;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (defName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class InteractionGroups
    {
        // Order matters: an interaction is classified into the FIRST group it matches,
        // so put specific/strong themes before generic ones. "Other" is the catch-all and
        // must stay last.
        public static readonly List<InteractionGroup> All = new List<InteractionGroup>
        {
            new InteractionGroup("nsfw", "NSFW / RJW", true,
                "explicit sexual encounter; keep it in-character and not graphic",
                new[] { "Necro_Vaginal" },
                new[] { "Sex_", "Rape", "Necro", "SexTame", "Speech_sex" }),

            new InteractionGroup("romance", "Romance & dating", true,
                "romantic moment between the pawns; may be an advance, commitment, date, or breakup depending on context",
                new[] { "RomanceAttempt", "MarriageProposal", "Breakup",
                    "Sentence_RomanceAttemptAccepted", "Sentence_RomanceAttemptRejected",
                    "Sentence_MarriageProposalAccepted", "Sentence_MarriageProposalRejected",
                    "Sentence_MarriageProposalRejectedBrokeUp" },
                new[] { "Romance", "Marriage", "Breakup", "Date", "Hookup" }),

            new InteractionGroup("recruit", "Recruitment & prison", true,
                "captor works on a prisoner: building rapport, recruiting, reducing will, enslaving, or stirring escape",
                new[] { "BuildRapport", "RecruitAttempt", "Sentence_RecruitAttemptAccepted",
                    "Sentence_RecruitAttemptRejected", "ReduceWill", "EnslaveAttempt", "SparkJailbreak" },
                new[] { "Recruit", "Rapport", "Jailbreak", "Enslave", "ReduceWill" }),

            new InteractionGroup("slavery", "Slavery", true,
                "interaction over slavery: suppression by an overseer or a spark of rebellion",
                new[] { "Suppress", "SparkSlaveRebellion" },
                new[] { "Suppress", "Slave", "Rebellion" }),

            new InteractionGroup("conversion", "Ideology & conversion", true,
                "ideological pressure: conversion, counseling, preaching, indoctrination, or motivation",
                new[] { "ConvertIdeoAttempt", "Convert_Success", "Convert_Failure",
                    "Counsel_Success", "Counsel_Failure", "PreachHealth", "WorkDrive",
                    "Indoctrinate", "RS_WorshipInteraction" },
                new[] { "Convert", "Counsel", "Preach", "Indoctrinate", "Worship", "WorkDrive", "Ideo" }),

            new InteractionGroup("trial", "Trials & accusations", true,
                "formal trial exchange: an accusation or a defense before the colony",
                new[] { "Trial_Accuse", "Trial_Defend" },
                new[] { "Trial", "Accuse" }),

            new InteractionGroup("anomaly", "Anomaly & dark", true,
                "unsettling or anomalous exchange; convey unease without explaining the supernatural away",
                new[] { "DarkDialogue", "CreepyWords", "InhumanRambling", "DisturbingChat",
                    "OccultTeaching", "PrisonerStudyAnomaly", "InterrogateIdentity" },
                new[] { "Dark", "Creepy", "Inhuman", "Disturbing", "Occult", "Anomaly", "Interrogate" }),

            new InteractionGroup("insults", "Insults & fights", true,
                "hostile exchange: an insult, a slight, or the start of a social fight",
                new[] { "Insult", "Slight", "Sentence_SocialFightStarted",
                    "Sentence_SocialFightConvoInitiatorStarted", "Sentence_SocialFightConvoRecipientStarted" },
                new[] { "Insult", "Slight", "Fight", "Rebuff" }),

            new InteractionGroup("ritual", "Rituals & speeches", true,
                "ceremonial or ritual address rather than a private chat; reflect its solemn, public nature",
                new[] { "SpeechUtility", "Speech_Duel", "Speech_Funeral", "Speech_Leader",
                    "Speech_Sacrifice", "Speech_Scarification", "Speech_Blinding", "Speech_Execution",
                    "Speech_TreeConnection", "Speech_Conversion", "Speech_AcceptRole", "Speech_RemoveRole",
                    "WordOfTrust", "WordOfJoy", "WordOfLove", "WordOfSerenity", "WordOfInspiration" },
                new[] { "Speech", "WordOf", "Ritual" }),

            new InteractionGroup("animal", "Animal handling", true,
                "moment between a pawn and an animal: chatting, taming, training, a nuzzle, or release",
                new[] { "AnimalChat", "TameAttempt", "TrainAttempt", "Nuzzle", "ReleaseToWild" },
                new[] { "Animal", "Tame", "Train", "Nuzzle", "ReleaseToWild" }),

            new InteractionGroup("heartfelt", "Heartfelt talk", true,
                "warm or meaningful exchange: a deep talk, kind words, reassurance, or calming someone down",
                new[] { "DeepTalk", "KindWords", "Reassure", "SnapOut_CalmDownInteraction" },
                new[] { "DeepTalk", "KindWords", "Reassure", "Comfort", "CalmDown", "SnapOut" }),

            new InteractionGroup("teaching", "Teaching & lessons", false,
                "an adult teaches or plays with a child",
                new[] { "BabyPlay" },
                new[] { "Lesson", "Teaching", "BabyPlay", "Baby" }),

            new InteractionGroup("smalltalk", "Small talk", false,
                "casual low-stakes chatter",
                new[] { "Chitchat", "Conversation", "EndConversation", "HangOut", "PrudeSeen",
                    "TourFinished", "GR_TalkingToHumans", "GR_UwUTalkingToHumans",
                    "LetsTalkEatTogether", "OfferFood", "SanguophageChat" },
                new[] { "Chitchat", "Chat", "Conversation", "HangOut" }),

            new InteractionGroup("other", "Other / uncategorized", false,
                "a social interaction between the pawns",
                Enumerable.Empty<string>(), Enumerable.Empty<string>(), catchAll: true),

            // ---- Mental-state domain ----
            new InteractionGroup("socialfight", "Social fights", true,
                "the two pawns lost their tempers and came to blows; write it as a heated physical fight",
                new[] { "SocialFighting" }, Enumerable.Empty<string>(),
                domain: GroupDomain.MentalState),

            new InteractionGroup("mentalbreak", "Mental breaks", true,
                "the pawn suffered a mental break; write their inner state and what they did, in their own voice",
                Enumerable.Empty<string>(), Enumerable.Empty<string>(),
                domain: GroupDomain.MentalState, catchAll: true),
        };

        public static InteractionGroup Classify(InteractionDef interactionDef)
        {
            return ClassifyIn(GroupDomain.Interaction, interactionDef?.defName);
        }

        public static InteractionGroup ClassifyMentalState(MentalStateDef stateDef)
        {
            return ClassifyIn(GroupDomain.MentalState, stateDef?.defName);
        }

        private static InteractionGroup ClassifyIn(GroupDomain domain, string defName)
        {
            InteractionGroup fallback = null;
            for (int i = 0; i < All.Count; i++)
            {
                InteractionGroup group = All[i];
                if (group.Domain != domain)
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

        public static InteractionGroup ByKey(string key)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }

            return null;
        }
    }
}
