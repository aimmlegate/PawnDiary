// Impure edge for Tier 2 SpeakUp conversation capture. This file manually resolves the small runtime
// surface verified in the installed 1.6 fork on 2026-07-12:
//   DialogManager.FireStatement(Statement), DialogManager.CleanUp()
//   Statement.Talk / Emitter / Reciever (upstream spelling)
//   Talk.latestReplyCount / remainingReplies / expireTick
// It never references SpeakUp.dll at compile time. Any missing member produces one warning and leaves
// Tier 1's XML groups working normally.
//
// FireStatement encloses TryInteractWith -> PlayLog.Add. Core Pawn Diary renders that new row (under
// its SpeakUp Ensue suppression scope), and our postfix on the vanilla text renderer copies the already-
// rendered emitter POV. We never render grammar a second time and therefore never schedule a reply.
//
// Double-count policy: Tier 2 does not toggle Tier 1 groups. A substantial completed Talk becomes one
// External pair event; its reply rows remain low-frequency ambient evidence. This is intentionally the
// conservative plan default until a real in-game diary-day review justifies hiding that texture.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PawnDiary.Integration;
using PawnDiarySpeakUp.Pure;
using Verse;

namespace PawnDiarySpeakUp
{
    /// <summary>Reflection binder, transient Talk accumulator, and External-event submission edge.</summary>
    internal static class TalkCapture
    {
        private const string DialogManagerTypeName = "SpeakUp.DialogManager";
        private const string StatementTypeName = "SpeakUp.Statement";
        private const string TalkTypeName = "SpeakUp.Talk";

        private static bool registrationAttempted;
        private static bool runtimeAvailable;
        private static bool disabledWarningLogged;

        private static FieldInfo statementTalkField;
        private static FieldInfo statementEmitterField;
        private static FieldInfo statementReceiverField;
        private static FieldInfo talkLatestReplyCountField;
        private static FieldInfo talkExpireTickField;
        private static PropertyInfo talkRemainingRepliesProperty;

        private static readonly Dictionary<object, TalkAccumulator> accumulations =
            new Dictionary<object, TalkAccumulator>(ReferenceComparer.Instance);

        // FireStatement is synchronous on RimWorld's main simulation thread. These three fields form a
        // short-lived scope around its nested TryInteractWith -> PlayLog render calls.
        private static object activeStatement;
        private static TalkAccumulator activeAccumulator;
        private static Pawn activeEmitter;

        /// <summary>
        /// Resolves every required target/accessor before enabling any patch body. A partial Harmony
        /// install remains harmless because runtimeAvailable flips only after all Patch calls succeed.
        /// </summary>
        public static bool TryRegister(Harmony harmony)
        {
            if (registrationAttempted)
            {
                return runtimeAvailable;
            }

            registrationAttempted = true;
            if (harmony == null)
            {
                WarnTier2Disabled("Harmony was unavailable");
                return false;
            }

            try
            {
                Type dialogManagerType = AccessTools.TypeByName(DialogManagerTypeName);
                Type statementType = AccessTools.TypeByName(StatementTypeName);
                Type talkType = AccessTools.TypeByName(TalkTypeName);
                if (dialogManagerType == null || statementType == null || talkType == null)
                {
                    WarnTier2Disabled("a required SpeakUp type was not found");
                    return false;
                }

                MethodBase fireStatement = AccessTools.Method(
                    dialogManagerType, "FireStatement", new[] { statementType });
                MethodBase cleanUp = AccessTools.Method(dialogManagerType, "CleanUp", Type.EmptyTypes);
                MethodBase textRenderer = AccessTools.Method(
                    typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV_Worker",
                    new[] { typeof(Thing), typeof(bool) })
                    ?? AccessTools.Method(
                        typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV",
                        new[] { typeof(Thing), typeof(bool) });

                statementTalkField = AccessTools.Field(statementType, "Talk");
                statementEmitterField = AccessTools.Field(statementType, "Emitter");
                statementReceiverField = AccessTools.Field(statementType, "Reciever");
                talkLatestReplyCountField = AccessTools.Field(talkType, "latestReplyCount");
                talkExpireTickField = AccessTools.Field(talkType, "expireTick");
                talkRemainingRepliesProperty = AccessTools.Property(talkType, "remainingReplies");

                if (fireStatement == null || cleanUp == null || textRenderer == null
                    || statementTalkField == null || statementEmitterField == null
                    || statementReceiverField == null || talkLatestReplyCountField == null
                    || talkExpireTickField == null || talkRemainingRepliesProperty == null)
                {
                    WarnTier2Disabled("a required SpeakUp method, field, or property changed");
                    return false;
                }

                harmony.Patch(
                    fireStatement,
                    prefix: new HarmonyMethod(typeof(TalkCapture), nameof(FireStatementPrefix)),
                    finalizer: new HarmonyMethod(typeof(TalkCapture), nameof(FireStatementFinalizer)));
                harmony.Patch(
                    cleanUp,
                    prefix: new HarmonyMethod(typeof(TalkCapture), nameof(CleanUpPrefix)));
                harmony.Patch(
                    textRenderer,
                    postfix: new HarmonyMethod(typeof(TalkCapture), nameof(RenderedTextPostfix)));

                runtimeAvailable = true;
                return true;
            }
            catch (Exception e)
            {
                WarnTier2Disabled("registration threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Harmony prefix that opens the short capture scope around one delivered reply.</summary>
        public static void FireStatementPrefix(object[] __args)
        {
            if (!runtimeAvailable)
            {
                return;
            }

            try
            {
                object statement = __args != null && __args.Length > 0 ? __args[0] : null;
                BeginStatement(statement);
            }
            catch (Exception e)
            {
                ClearActiveStatement();
                LogHookFailureOnce("FireStatementPrefix", e);
            }
        }

        /// <summary>
        /// Harmony finalizer closes the scope on both success and exception. Returning the original
        /// exception unchanged means the adapter never hides or alters a SpeakUp failure.
        /// </summary>
        public static Exception FireStatementFinalizer(object[] __args, Exception __exception)
        {
            if (!runtimeAvailable)
            {
                return __exception;
            }

            try
            {
                object statement = __args != null && __args.Length > 0 ? __args[0] : null;
                EndStatement(statement, __exception == null);
            }
            catch (Exception e)
            {
                ClearActiveStatement();
                LogHookFailureOnce("FireStatementFinalizer", e);
            }

            return __exception;
        }

        /// <summary>
        /// Copies the emitter-POV result from core Pawn Diary's existing PlayLog render. The original
        /// renderer has parameters named pov/forceLog; Harmony binds this read-only postfix by name.
        /// </summary>
        public static void RenderedTextPostfix(Thing pov, string __result)
        {
            if (!runtimeAvailable || activeAccumulator == null || activeEmitter == null
                || !ReferenceEquals(pov, activeEmitter) || string.IsNullOrWhiteSpace(__result))
            {
                return;
            }

            try
            {
                activeAccumulator.RenderedLines.Add(__result);
            }
            catch (Exception e)
            {
                LogHookFailureOnce("RenderedTextPostfix", e);
            }
        }

        /// <summary>
        /// SpeakUp calls CleanUp every 60 game ticks before removing expired Talks. Sweep first so the
        /// adapter can finalize an otherwise-complete tracked Talk while its reflection object is valid.
        /// </summary>
        public static void CleanUpPrefix()
        {
            if (!runtimeAvailable)
            {
                return;
            }

            try
            {
                SweepExpired();
            }
            catch (Exception e)
            {
                LogHookFailureOnce("CleanUpPrefix", e);
            }
        }

        /// <summary>Clears all per-game transient observations; called from FinalizeInit and the off switch.</summary>
        public static void ResetTransient()
        {
            accumulations.Clear();
            ClearActiveStatement();
        }

        private static bool CaptureEnabled
        {
            get
            {
                SpeakUpBridgeSettings settings = SpeakUpBridgeMod.Settings;
                return settings != null && settings.captureWholeConversations;
            }
        }

        private static void BeginStatement(object statement)
        {
            ClearActiveStatement();
            if (!CaptureEnabled)
            {
                accumulations.Clear();
                return;
            }

            if (statement == null)
            {
                return;
            }

            object talk = statementTalkField.GetValue(statement);
            Pawn emitter = statementEmitterField.GetValue(statement) as Pawn;
            Pawn receiver = statementReceiverField.GetValue(statement) as Pawn;
            if (talk == null || emitter == null || receiver == null || ReferenceEquals(emitter, receiver))
            {
                return;
            }

            TalkAccumulator accumulator;
            if (!accumulations.TryGetValue(talk, out accumulator))
            {
                // Talk's first scheduled reply swaps roles: its receiver is the original interaction
                // initiator and therefore the requested subject; its emitter is the original recipient.
                int now = CurrentTick();
                accumulator = new TalkAccumulator(talk, receiver, emitter, now);
                accumulations.Add(talk, accumulator);
            }

            accumulator.LastReplyCount = ReadInt(talkLatestReplyCountField, talk);
            activeStatement = statement;
            activeAccumulator = accumulator;
            activeEmitter = emitter;
        }

        private static void EndStatement(object statement, bool deliveredSuccessfully)
        {
            TalkAccumulator accumulator = activeAccumulator;
            object talk = accumulator == null ? null : accumulator.Talk;
            bool sameStatement = activeStatement != null && ReferenceEquals(activeStatement, statement);
            ClearActiveStatement();

            if (!sameStatement || accumulator == null || talk == null || !CaptureEnabled)
            {
                if (!CaptureEnabled)
                {
                    accumulations.Clear();
                }
                return;
            }

            if (!deliveredSuccessfully)
            {
                // Leave it for SpeakUp's expiry cleanup. We do not turn a failed delivery into a diary fact.
                return;
            }

            accumulator.DeliveredStatements++;
            accumulator.LastTick = CurrentTick();
            accumulator.LastReplyCount = ReadInt(talkLatestReplyCountField, talk);

            int remainingReplies = ReadInt(talkRemainingRepliesProperty, talk);
            if (remainingReplies <= 0)
            {
                FinalizeTalk(accumulator);
            }
        }

        private static void SweepExpired()
        {
            if (!CaptureEnabled)
            {
                ResetTransient();
                return;
            }

            int now = CurrentTick();
            List<TalkAccumulator> snapshot = new List<TalkAccumulator>(accumulations.Values);
            for (int i = 0; i < snapshot.Count; i++)
            {
                TalkAccumulator accumulator = snapshot[i];
                int expireTick = ReadInt(talkExpireTickField, accumulator.Talk);
                if (expireTick < now)
                {
                    accumulator.LastTick = now;
                    accumulator.LastReplyCount = ReadInt(talkLatestReplyCountField, accumulator.Talk);
                    FinalizeTalk(accumulator);
                }
            }
        }

        private static void FinalizeTalk(TalkAccumulator accumulator)
        {
            if (accumulator == null || !accumulations.Remove(accumulator.Talk))
            {
                return;
            }

            if (ReferenceEquals(activeAccumulator, accumulator))
            {
                ClearActiveStatement();
            }

            // At least one successful FireStatement is required. On expiry, latestReplyCount can include
            // a statement SpeakUp scheduled but failed to deliver; this guard prevents a zero-delivery entry.
            if (accumulator.DeliveredStatements <= 0)
            {
                return;
            }

            SpeakUpBridgeSettings settings = SpeakUpBridgeMod.Settings;
            int minimumReplies = settings == null
                ? TalkSummaryFormat.DefaultMinimumReplies
                : settings.minimumReplies;
            TalkSummaryPlan plan = TalkSummaryFormat.Plan(
                accumulator.LastReplyCount,
                minimumReplies,
                TalkSummaryFormat.DefaultSampleLineLimit,
                accumulator.RenderedLines);
            if (plan == null)
            {
                return;
            }

            Pawn subject = accumulator.Subject;
            Pawn partner = accumulator.Partner;
            if (!ParticipantsStillTogether(subject, partner))
            {
                // A pawn left the map/died/was destroyed mid-talk. SpeakUp's upstream fork has had NREs
                // in this zone; the adapter simply drops the transient conversation without dereferencing it.
                return;
            }

            // A prisoner cannot own a first-person diary. Normal prisoner talks start with the colonist and
            // already put that colonist in subject; if a target mod reversed roles, prefer the eligible pawn.
            if (!PawnDiaryApi.IsDiaryEligible(subject))
            {
                if (!PawnDiaryApi.IsDiaryEligible(partner))
                {
                    return;
                }

                Pawn swap = subject;
                subject = partner;
                partner = swap;
            }

            string samples = plan.JoinedSamples();
            string summary = samples.Length > 0
                ? "PawnDiarySpeakUp.Event.ConversationSummaryWithLines"
                    .Translate(subject.LabelShort, partner.LabelShort, plan.ExchangeCount, samples).Resolve()
                : "PawnDiarySpeakUp.Event.ConversationSummaryWithoutLines"
                    .Translate(subject.LabelShort, partner.LabelShort, plan.ExchangeCount).Resolve();

            ExternalEventRequest request = new ExternalEventRequest
            {
                sourceId = SpeakUpBridgeMod.SourceId,
                eventKey = TalkSummaryFormat.ConversationEventKey,
                subject = subject,
                partner = partner,
                eventLabel = "PawnDiarySpeakUp.Event.ConversationLabel".Translate().Resolve(),
                summaryText = summary,
                extraContext = new List<string>
                {
                    "speakup_reply_count=" + plan.ExchangeCount,
                    "speakup_start_tick=" + accumulator.FirstTick,
                    "speakup_end_tick=" + accumulator.LastTick,
                },
            };

            // Respect Pawn Diary's normal integration budget and pair/eventKey dedup policy.
            PawnDiaryApi.SubmitEvent(request);
        }

        private static bool ParticipantsStillTogether(Pawn subject, Pawn partner)
        {
            if (subject == null || partner == null || subject.Destroyed || partner.Destroyed
                || subject.Dead || partner.Dead || !subject.Spawned || !partner.Spawned)
            {
                return false;
            }

            Map map = subject.Map;
            return map != null && ReferenceEquals(map, partner.Map);
        }

        private static int CurrentTick()
        {
            return Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
        }

        private static int ReadInt(FieldInfo field, object instance)
        {
            object value = field.GetValue(instance);
            return value is int ? (int)value : 0;
        }

        private static int ReadInt(PropertyInfo property, object instance)
        {
            object value = property.GetValue(instance, null);
            return value is int ? (int)value : 0;
        }

        private static void ClearActiveStatement()
        {
            activeStatement = null;
            activeAccumulator = null;
            activeEmitter = null;
        }

        private static void WarnTier2Disabled(string reason)
        {
            if (disabledWarningLogged)
            {
                return;
            }

            disabledWarningLogged = true;
            Log.Warning(SpeakUpBridgeMod.LogPrefix
                + " Tier 2 disabled; SpeakUp internals changed or were unavailable (" + reason
                + "). Tier 1 interaction groups remain active.");
        }

        private static void LogHookFailureOnce(string hook, Exception exception)
        {
            Log.ErrorOnce(
                SpeakUpBridgeMod.LogPrefix + " " + hook + " failed and was skipped: " + exception,
                ("PawnDiarySpeakUp." + hook).GetHashCode());
        }

        private sealed class TalkAccumulator
        {
            public TalkAccumulator(object talk, Pawn subject, Pawn partner, int firstTick)
            {
                Talk = talk;
                Subject = subject;
                Partner = partner;
                FirstTick = firstTick;
                LastTick = firstTick;
            }

            public object Talk { get; }
            public Pawn Subject { get; }
            public Pawn Partner { get; }
            public int FirstTick { get; }
            public int LastTick { get; set; }
            public int LastReplyCount { get; set; }
            public int DeliveredStatements { get; set; }
            public List<string> RenderedLines { get; } = new List<string>();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
