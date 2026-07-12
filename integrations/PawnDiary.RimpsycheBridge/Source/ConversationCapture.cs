// Tier-C Rimpsyche conversation-outcome capture.
//
// Rimpsyche deliberately exposes an empty, NoInlining InteractionHook after it has calculated the
// conversation topic, signed alignment, and opinion offsets. This bridge attaches ONE Harmony Postfix
// to that hook, filters for unusually charged alignment, and submits a factual ExternalEventRequest.
// It observes only: no Rimpsyche result or pawn state is changed.
//
// Version drift is isolated at startup. The target is found by full type name + exact verified
// parameter shape rather than a fragile attribute. If v-current renames the hook, one warning disables
// Tier C while Tier A/B and XML groups keep working. The Postfix itself uses object for Topic, so its
// method signature does not force RimPsyche.dll resolution when the target mod is absent.
//
// New to C#/RimWorld? See AGENTS.md (optional-mod hooks), docs/lore/harmony.md, and EXTERNAL_API.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Integration;
using PawnDiaryRimpsyche.Pure;
using Verse;

namespace PawnDiaryRimpsyche
{
    /// <summary>Signature-checked Harmony registration plus runtime charged-conversation forwarding.</summary>
    internal static class ConversationCapture
    {
        private const string TargetTypeName = "Maux36.RimPsyche.InteractionWorker_StartConversation";
        private const string TargetMethodName = "InteractionHook";
        private const string TopicTypeName = "Maux36.RimPsyche.Topic";

        // Accepted pair -> tick. Runtime writes only after Pawn Diary actually records the request.
        // The GameComponent snapshots/restores this primitive dictionary across saves.
        private static readonly Dictionary<string, int> LastAcceptedTickByPair =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool targetSearched;
        private static MethodInfo targetMethod;
        private static FieldInfo topicLabelField;

        /// <summary>
        /// Locates and patches Rimpsyche's documented hook. Returns false after logging one warning if
        /// the installed assembly has drifted; never throws into the mod constructor.
        /// </summary>
        public static bool TryInstall(Harmony harmony)
        {
            if (harmony == null)
            {
                return false;
            }

            try
            {
                MethodInfo target = FindTargetMethod();
                if (target == null)
                {
                    Log.WarningOnce(
                        PawnDiaryRimpsycheMod.LogPrefix
                        + " could not find Rimpsyche's InteractionHook with the expected pawn/pawn/topic/float/float/float signature; charged-conversation capture is disabled, but psyche context and outlook sync remain available.",
                        "PawnDiaryRimpsyche.InteractionHook.Missing".GetHashCode());
                    return false;
                }

                MethodInfo postfix = typeof(ConversationCapture).GetMethod(
                    nameof(Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null)
                {
                    Log.WarningOnce(
                        PawnDiaryRimpsycheMod.LogPrefix
                        + " could not resolve its own conversation postfix; charged-conversation capture is disabled.",
                        "PawnDiaryRimpsyche.InteractionHook.PostfixMissing".GetHashCode());
                    return false;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    PawnDiaryRimpsycheMod.LogPrefix
                    + " failed to install the Rimpsyche conversation hook; Tier C is disabled: "
                    + exception,
                    "PawnDiaryRimpsyche.InteractionHook.InstallFailed".GetHashCode());
                return false;
            }
        }

        /// <summary>Returns a detached cooldown snapshot for Scribe persistence.</summary>
        public static Dictionary<string, int> CooldownSnapshot()
        {
            return new Dictionary<string, int>(LastAcceptedTickByPair, StringComparer.Ordinal);
        }

        /// <summary>Restores a saved primitive cooldown map after the new-game static reset.</summary>
        public static void RestoreCooldowns(IDictionary<string, int> saved)
        {
            LastAcceptedTickByPair.Clear();
            if (saved == null)
            {
                return;
            }

            foreach (KeyValuePair<string, int> pair in saved)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    LastAcceptedTickByPair[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>Clears process-static pair state before a newly loaded game restores its own map.</summary>
        public static void ResetForNewGame()
        {
            LastAcceptedTickByPair.Clear();
        }

        // Harmony binds by ORIGINAL PARAMETER NAME. Names were verified by decompiling installed
        // RimPsyche.dll v1.0.41. `object convoTopic` avoids a target-assembly type in our signature.
        private static void Postfix(Pawn initiator, Pawn recipient, object convoTopic, float alignment)
        {
            // Hot-path order: settings bool first, then cheap readiness/type/null gates, before any
            // translation, dictionary allocation, or reflected topic read.
            PawnDiaryRimpsycheSettings settings = PawnDiaryRimpsycheMod.Settings;
            if (settings == null || !settings.recordChargedConversations)
            {
                return;
            }

            try
            {
                if (!PawnDiaryRimpsycheMod.RimpsycheActive
                    || !PawnDiaryRimpsycheMod.SupportsApiVersion(1)
                    || !PawnDiaryApi.IsReady
                    || !PawnDiaryApi.IsExternalApiEnabled
                    || initiator == null
                    || recipient == null
                    || ReferenceEquals(initiator, recipient))
                {
                    return;
                }

                // The plan deliberately requires BOTH POVs to be eligible. Pawn Diary would otherwise
                // downgrade an ineligible partner to a solo event, which would misrepresent a two-person
                // conversation outcome as a one-sided event.
                if (!PawnDiaryApi.IsDiaryEligible(initiator)
                    || !PawnDiaryApi.IsDiaryEligible(recipient))
                {
                    return;
                }

                string pairKey = ConversationCapturePolicy.PairKey(
                    initiator.GetUniqueLoadID(), recipient.GetUniqueLoadID());
                if (pairKey.Length == 0)
                {
                    return;
                }

                RimpsycheBridgeTuningDef tuning = RimpsycheBridgeTuningDef.Current;
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                int lastAccepted;
                bool hasLast = LastAcceptedTickByPair.TryGetValue(pairKey, out lastAccepted);
                int cooldownTicks = Math.Max(0, tuning.conversationPairCooldownTicks);
                if (!ConversationCapturePolicy.ShouldCapture(
                    alignment,
                    tuning.conversationAlignmentThreshold,
                    now,
                    hasLast,
                    lastAccepted,
                    cooldownTicks))
                {
                    return;
                }

                string topic = TopicLabel(convoTopic);
                if (string.IsNullOrWhiteSpace(topic))
                {
                    topic = "PawnDiaryRimpsyche.Event.UnknownTopic".Translate().Resolve();
                }

                string summaryKey = alignment > 0f
                    ? "PawnDiaryRimpsyche.Event.ConversationSummary.Positive"
                    : "PawnDiaryRimpsyche.Event.ConversationSummary.Negative";

                bool recorded = PawnDiaryApi.SubmitEvent(new ExternalEventRequest
                {
                    sourceId = BridgeIds.ModId,
                    eventKey = BridgeIds.ConversationEventKey,
                    subject = initiator,
                    partner = recipient,
                    eventLabel = "PawnDiaryRimpsyche.Event.ConversationLabel".Translate().Resolve(),
                    // Factual only: topic label + sign bucket. No raw alignment or invented quotation.
                    summaryText = summaryKey.Translate(topic).Resolve(),
                    // The core dedup is a second backstop; this adapter's saved dictionary is the primary
                    // per-pair gate and survives reloads.
                    dedupKey = BridgeIds.ConversationEventKey + ":" + pairKey,
                    dedupTicks = cooldownTicks
                });

                if (recorded)
                {
                    LastAcceptedTickByPair[pairKey] = now;
                }
            }
            catch (Exception exception)
            {
                // A conversation hook must never interrupt Rimpsyche's already-completed interaction.
                Log.ErrorOnce(
                    PawnDiaryRimpsycheMod.LogPrefix
                    + " failed while forwarding a charged Rimpsyche conversation: " + exception,
                    "PawnDiaryRimpsyche.InteractionHook.RuntimeFailed".GetHashCode());
            }
        }

        private static MethodInfo FindTargetMethod()
        {
            if (targetSearched)
            {
                return targetMethod;
            }

            targetSearched = true;
            Type targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
            {
                return null;
            }

            MethodInfo[] methods = targetType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (!string.Equals(candidate.Name, TargetMethodName, StringComparison.Ordinal)
                    || candidate.ReturnType != typeof(void))
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != 6
                    || parameters[0].ParameterType != typeof(Pawn)
                    || parameters[1].ParameterType != typeof(Pawn)
                    || !string.Equals(parameters[2].ParameterType.FullName, TopicTypeName, StringComparison.Ordinal)
                    || parameters[3].ParameterType != typeof(float)
                    || parameters[4].ParameterType != typeof(float)
                    || parameters[5].ParameterType != typeof(float))
                {
                    continue;
                }

                targetMethod = candidate;
                topicLabelField = parameters[2].ParameterType.GetField(
                    "label", BindingFlags.Public | BindingFlags.Instance);
                return targetMethod;
            }

            return null;
        }

        private static string TopicLabel(object topic)
        {
            if (topic == null || topicLabelField == null)
            {
                return string.Empty;
            }

            return topicLabelField.GetValue(topic) as string ?? string.Empty;
        }
    }
}
