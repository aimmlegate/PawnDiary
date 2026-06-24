// Harmony patches — our hooks into vanilla RimWorld. We don't own the game's code, so we "patch"
// vanilla methods: Postfix hooks record PlayLog.Add social interactions,
// MentalStateHandler.TryStartMentalState social fights / mental breaks, TaleRecorder
// notable-history events, InspirationHandler.TryStartInspiration pawn inspirations,
// Pawn.Kill death details, Pawn.SetFaction colony arrivals, Pawn_HealthTracker.AddHediff
// health signals, and GameConditionManager.RegisterCondition mood-affecting game conditions.
// A Prefix hook redirects Social-tab
// play-log clicks into the matching Diary entry when one exists. AccessTools.Field reads private
// vanilla fields via reflection.
// New to this? See AGENTS.md ("Harmony patches").
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary
{
    // Fires at the exact moment a pawn is killed. TaleRecorder later tells us that a colonist death
    // should become a diary event, but the Tale no longer carries the killing DamageInfo or culprit
    // hediff. This prefix caches those facts while they are still available.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class PawnKillPatch
    {
        /// <summary>
        /// Harmony Prefix for Pawn.Kill. Captures death cause details for colonists before vanilla
        /// mutates death/corpse state and records the corresponding Tale.
        /// </summary>
        public static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            DeathContextCache.Capture(__instance, dinfo, exactCulprit);
        }

        /// <summary>
        /// Harmony Postfix for Pawn.Kill. Records a neutral final death entry when vanilla did not
        /// emit a death Tale for this kill path, such as some condition/need deaths.
        /// </summary>
        public static void Postfix(Pawn __instance)
        {
            DiaryGameComponent.Current?.RecordDeathFallback(__instance);
        }
    }

    // Fires whenever a pawn changes faction. Many vanilla and DLC join paths eventually converge
    // here: prisoner recruitment, wanderer/quest joins, creepjoiners, and modded arrivals.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class PawnSetFactionPatch
    {
        /// <summary>
        /// Harmony Prefix for Pawn.SetFaction. Captures the pre-change faction and recruiter before
        /// vanilla mutates the pawn into a colonist.
        /// </summary>
        public static void Prefix(Pawn __instance, Faction newFaction, Pawn recruiter)
        {
            ArrivalContextCache.Capture(__instance, newFaction, recruiter);
        }

        /// <summary>
        /// Harmony Postfix for Pawn.SetFaction. Records the arrival only after vanilla confirms the
        /// pawn is now a player colonist.
        /// </summary>
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            // Scenario setup flips starting pawns to the player faction during generation, before the
            // game is playing; skip those so we don't record arrivals (and read TicksAbs) too early.
            // Founding colonists are recorded by the first-playing-tick scan instead.
            if (__instance == null || newFaction != Faction.OfPlayer || !__instance.IsColonist
                || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordColonistArrival(__instance,
                ArrivalContextCache.ConsumeOrBuild(__instance, "set_faction"));
        }
    }

    // Fires whenever a hediff is added to any pawn (injuries, diseases, addictions, implants...).
    // We forward colonist additions so a major new affliction can feed that pawn's end-of-day
    // reflection. The "is this major enough?" filter and per-day accumulation live in
    // DiaryGameComponent (DaySummary.cs), keeping this hook thin and the threshold XML-tunable.
    // AddHediff has overloads, so the argument-type array selects the canonical Hediff overload
    // every add path routes through. hediff.pawn is already assigned by the time the postfix runs.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class HealthTrackerAddHediffPatch
    {
        /// <summary>
        /// Harmony Postfix for Pawn_HealthTracker.AddHediff. Forwards a colonist's newly-added
        /// hediff to the diary so the day-summary builder can decide whether it is worth remembering.
        /// </summary>
        public static void Postfix(Hediff hediff)
        {
            Pawn pawn = hediff?.pawn;
            // RimWorld rolls old-age injuries onto starting pawns during generation, before the game
            // is playing; skip those so RecordHediffAppeared does not read TicksAbs (via CurrentDayIndex)
            // before the world clock exists. Starting hediffs are baselined by the first scan instead.
            if (pawn == null || !pawn.IsColonist || !DiaryGameComponent.GamePlaying)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordHediffAppeared(pawn, hediff);
        }
    }

    // Fires whenever RimWorld accepts a notable "tale" for history/art generation. Tales cover
    // many non-social events, such as wounds, deaths, surgeries, births, recruitment, research,
    // construction/crafting milestones, raids, and disasters.
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    public static class TaleRecorderPatch
    {
        /// <summary>
        /// Harmony Postfix for TaleRecorder.RecordTale. The returned Tale is null when vanilla
        /// chose not to record it, so only successful tales are forwarded to DiaryGameComponent.
        /// </summary>
        public static void Postfix(Tale __result, TaleDef def)
        {
            if (__result == null || def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordTale(__result, def);
        }
    }

    // Fires whenever a pawn enters a mental state: social fights (pairwise) and mental
    // breaks (solo). This is the single choke point for all mental states, so it catches
    // them regardless of how they were triggered.
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class MentalStateStartPatch
    {
        // Reflection accessor for the private MentalStateHandler.pawn field so we can read the subject pawn.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(MentalStateHandler), "pawn");

        static MentalStateStartPatch()
        {
            if (PawnField == null)
            {
                Log.Warning("[PawnDiary] Could not find MentalStateHandler.pawn; mental-state diary events will not be captured.");
            }
        }

        /// <summary>
        /// Harmony Postfix for MentalStateHandler.TryStartMentalState. Forwards successful
        /// mental state transitions to DiaryGameComponent for diary recording.
        /// </summary>
        public static void Postfix(bool __result, MentalStateHandler __instance, MentalStateDef stateDef, string reason, Pawn otherPawn)
        {
            if (!__result || stateDef == null || __instance == null)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMentalState(pawn, stateDef, otherPawn, reason);
        }
    }

    // Fires whenever RimWorld accepts a pawn inspiration, whether it came from the random inspiration
    // system or another vanilla source such as a psycast, ritual, or drug effect.
    [HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
    public static class InspirationStartPatch
    {
        /// <summary>
        /// Harmony Postfix for InspirationHandler.TryStartInspiration. Records only successful
        /// inspiration starts, after vanilla has accepted and applied the inspiration.
        /// </summary>
        public static void Postfix(bool __result, InspirationHandler __instance, InspirationDef def, string reason)
        {
            if (!__result || __instance == null || __instance.pawn == null || def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordInspiration(__instance.pawn, def, reason);
        }
    }

    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    public static class PlayLogAddPatch
    {
        // Reflection accessors for private fields on PlayLogEntry_Interaction — RimWorld doesn't
        // expose these publicly, so we read them via Harmony's AccessTools.
        private static readonly FieldInfo IntDefField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef");
        private static readonly FieldInfo InitiatorField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "initiator");
        private static readonly FieldInfo RecipientField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "recipient");

        /// <summary>
        /// Harmony Postfix for PlayLog.Add. When the added entry is a social interaction,
        /// extracts the interaction type and participants and forwards them to DiaryGameComponent.
        /// </summary>
        public static void Postfix(LogEntry entry)
        {
            if (GeneratedSpeechPlayLog.IsAddingGeneratedSpeechEntry)
            {
                return;
            }

            PlayLogEntry_Interaction interactionEntry = entry as PlayLogEntry_Interaction;
            if (interactionEntry == null)
            {
                return;
            }

            InteractionDef interactionDef = IntDefField?.GetValue(interactionEntry) as InteractionDef;
            Pawn initiator = InitiatorField?.GetValue(interactionEntry) as Pawn;
            Pawn recipient = RecipientField?.GetValue(interactionEntry) as Pawn;
            DiaryGameComponent component = DiaryGameComponent.Current;
            if (component == null || !component.ShouldCaptureInteractionFromPlayLog(initiator, recipient, interactionDef))
            {
                return;
            }

            string initiatorGameText = GameTextFromPov(interactionEntry, initiator);
            string recipientGameText = GameTextFromPov(interactionEntry, recipient);

            component.RecordInteraction(initiator, recipient, interactionDef,
                initiatorGameText, recipientGameText, interactionEntry.LogID);
        }

        /// <summary>
        /// Safely renders the interaction log entry from a pawn's point of view,
        /// falling back to ToString() if the game's POV method throws.
        /// </summary>
        private static string GameTextFromPov(PlayLogEntry_Interaction interactionEntry, Pawn pawn)
        {
            if (interactionEntry == null || pawn == null)
            {
                return string.Empty;
            }

            try
            {
                return interactionEntry.ToGameStringFromPOV(pawn, false);
            }
            catch
            {
                return interactionEntry.ToString();
            }
        }
    }

    [HarmonyPatch]
    public static class PlayLogGeneratedSpeechTextPatch
    {
        /// <summary>
        /// Finds the concrete interaction text renderer for this RimWorld build. In 1.6 the public
        /// base method delegates to PlayLogEntry_Interaction.ToGameStringFromPOV_Worker, so patching
        /// the old public name on the concrete class can fail PatchAll before later hooks register.
        /// </summary>
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV_Worker",
                new[] { typeof(Thing), typeof(bool) })
                ?? AccessTools.Method(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV",
                new[] { typeof(Thing), typeof(bool) });
        }

        /// <summary>
        /// Harmony Prefix for generated direct-speech rows. Normal vanilla interaction rows continue
        /// through RimWorld's grammar; injected rows display the already parsed LLM speech text.
        /// </summary>
        public static bool Prefix(PlayLogEntry_Interaction __instance, ref string __result)
        {
            // Fast path: when this game has no generated speech rows at all, skip the per-row lookup
            // entirely and let every interaction render through vanilla grammar.
            if (!GeneratedSpeechPlayLog.HasGeneratedSpeechRows)
            {
                return true;
            }

            string text;
            if (!GeneratedSpeechPlayLog.TryGetText(__instance, out text))
            {
                return true;
            }

            __result = text;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayLogEntry_Interaction), nameof(PlayLogEntry_Interaction.ClickedFromPOV))]
    public static class PlayLogInteractionClickPatch
    {
        /// <summary>
        /// Harmony Prefix for social interaction log clicks. When this exact PlayLog row has a
        /// finished diary entry for the clicked pawn's POV, open Diary instead of vanilla behavior.
        /// Returning true lets RimWorld continue normally when no diary entry is available yet.
        /// </summary>
        public static bool Prefix(PlayLogEntry_Interaction __instance, Thing pov)
        {
            if (__instance == null)
            {
                return true;
            }

            Pawn pawn = pov as Pawn;
            if (pawn == null)
            {
                return true;
            }

            DiaryEntryView entry = DiaryGameComponent.Current?.GeneratedEntryForPlayLogEntry(pawn, __instance.LogID);
            if (entry == null)
            {
                return true;
            }

            if (!EnsureSelected(pawn))
            {
                return true;
            }

            ITab_Pawn_Diary.RequestScrollToEntry(pawn, entry.EventId);
            InspectTabBase opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Diary));
            if (opened is ITab_Pawn_Diary)
            {
                return false;
            }

            ITab_Pawn_Diary.ClearPendingScrollRequest();
            return true;
        }

        /// <summary>
        /// The inspect pane opens tabs for the current selection, so make sure the POV pawn is
        /// selected. Social-tab clicks usually already satisfy this; the spawned guard avoids
        /// trying to select an off-map pawn from another play-log surface.
        /// </summary>
        private static bool EnsureSelected(Pawn pawn)
        {
            if (pawn == null || Find.Selector == null)
            {
                return false;
            }

            if (Find.Selector.IsSelected(pawn))
            {
                return true;
            }

            if (!pawn.Spawned)
            {
                return false;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, true, false);
            return true;
        }
    }

    // Fires when a mood-affecting GameCondition starts (aurora, eclipse, psychic drone,
    // toxic fallout, etc.). The patch iterates eligible colonists on affected maps and records
    // a solo diary event for each one, so colonists can note how they feel about the event.
    [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.RegisterCondition))]
    public static class GameConditionStartPatch
    {
        /// <summary>
        /// Harmony Postfix for GameConditionManager.RegisterCondition. Detects
        /// mood-affecting game conditions (aurora, eclipse, psychic drone, etc.) and
        /// forwards each affected colonist to DiaryGameComponent.RecordMoodEvent.
        /// </summary>
        public static void Postfix(GameCondition cond)
        {
            if (cond == null || cond.def == null)
            {
                return;
            }

            // TODO: ProblemCauser conditions are too complex to handle correctly — skipped for now.
            if (cond.def.defName == "ProblemCauser")
            {
                return;
            }

            DiaryGameComponent.Current?.RecordMoodEvent(cond);
        }
    }

    // Fires when a pawn gains a direct relation with another pawn (Lover, Spouse, Rival,
    // Cousin, Parent, ...). RecordRomance filters to the four vanilla romance relations and
    // emits a pairwise diary event for the relation change. Pair dedup (canonical pair key in
    // RecordRomance) collapses the mirrored call when RimWorld adds the relation symmetrically on
    // the other pawn's tracker.
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    public static class PawnRelationAddPatch
    {
        // Reflection accessor for the private Pawn_RelationsTracker.pawn field so we can read the
        // subject pawn (the tracker's owner). Mirrors the MentalStateHandler.pawn pattern.
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");

        static PawnRelationAddPatch()
        {
            if (PawnField == null)
            {
                Log.Warning("[PawnDiary] Could not find Pawn_RelationsTracker.pawn; romance diary events will not be captured.");
            }
        }

        /// <summary>
        /// Harmony Postfix for Pawn_RelationsTracker.AddDirectRelation. Forwards the relation
        /// change to DiaryGameComponent.RecordRomance, which filters to romance relations and
        /// records a pairwise diary event when both pawns are eligible.
        /// </summary>
        public static void Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (__instance == null || def == null || otherPawn == null)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRomance(pawn, otherPawn, def);
        }
    }

    // Fires once per raid incident (RaidEnemy / RaidFriendly / RaidBeacon). IncidentWorker.TryExecute
    // is the single public entry point every incident flows through, so we filter to raid subclasses
    // in the postfix (after raiders have spawned) and forward the IncidentParms + IncidentDef. The
    // hook fires exactly once per raid instance, which is the cleanest single chokepoint.
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class RaidExecutePatch
    {
        /// <summary>
        /// Harmony Postfix for IncidentWorker.TryExecute. Forwards successful raid incidents to
        /// DiaryGameComponent.RecordRaid, which fans out to each eligible colonist on the target map.
        /// Non-raid incidents and failed executions are skipped.
        /// </summary>
        public static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            if (!__result || !(__instance is IncidentWorker_Raid) || parms == null || __instance.def == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRaid(parms, __instance.def);
        }
    }

    // Fires when the player accepts a quest (Quest.Accept). This is the diary's entry point for the
    // whole quest lifecycle: offered-but-not-accepted quests are intentionally ignored (per the
    // requirement "only quest that accepted"). Accept fans out to every eligible colonist.
    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept), new[] { typeof(Pawn) })]
    public static class QuestAcceptPatch
    {
        /// <summary>
        /// Harmony Postfix for Quest.Accept. Forwards the freshly accepted quest to
        /// DiaryGameComponent.RecordQuestAccepted, which records a solo entry per eligible colonist.
        /// </summary>
        public static void Postfix(Quest __instance)
        {
            if (__instance == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordQuestAccepted(__instance);
        }
    }

    // UI fallback for quest acceptance. Vanilla accepts quests from MainTabWindow_Quests through a
    // compiler-generated local function; Quest.Accept should still be the canonical hook, but this
    // covers the exact in-game button path if a RimWorld/Harmony edge case skips the direct patch.
    // New to C#/RimWorld? Compiler-generated names are fragile, so this is registered manually with
    // null checks instead of a bare [HarmonyPatch] attribute.
    public static class QuestUiAcceptPatch
    {
        private const string AcceptClosureTypeName = "RimWorld.MainTabWindow_Quests+<>c__DisplayClass83_1";
        private const string AcceptActionMethodName = "<AcceptQuestByInterface>g__AcceptAction|1";
        private const string ParentClosureFieldName = "CS$<>8__locals1";
        private const string QuestWindowFieldName = "<>4__this";
        private const string SelectedQuestFieldName = "selected";

        private static FieldInfo ParentClosureField;
        private static FieldInfo QuestWindowField;
        private static FieldInfo SelectedQuestField;

        /// <summary>
        /// Patches the generated UI accept action when RimWorld still exposes it under the known
        /// compiler name. Safe to skip: the canonical Quest.Accept patch remains registered above.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            Type closureType = AccessTools.TypeByName(AcceptClosureTypeName);
            MethodBase target = AccessTools.Method(closureType, AcceptActionMethodName);
            ParentClosureField = AccessTools.Field(closureType, ParentClosureFieldName);
            Type parentClosureType = ParentClosureField?.FieldType;
            QuestWindowField = AccessTools.Field(parentClosureType, QuestWindowFieldName);
            SelectedQuestField = AccessTools.Field(typeof(MainTabWindow_Quests), SelectedQuestFieldName);

            if (target == null
                || ParentClosureField == null
                || QuestWindowField == null
                || SelectedQuestField == null)
            {
                Log.Warning("[Pawn Diary] Could not find MainTabWindow_Quests quest-accept UI action; "
                    + "quest accepted diary entries will rely on Quest.Accept only.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(QuestUiAcceptPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for the UI accept action. Runs after vanilla acceptance, then forwards the
        /// selected quest. If Quest.Accept already recorded it, RecordQuestAccepted's dedup key drops
        /// this duplicate in the same tick.
        /// </summary>
        public static void Postfix(object __instance)
        {
            object parentClosure = ParentClosureField?.GetValue(__instance);
            object questWindow = QuestWindowField?.GetValue(parentClosure);
            Quest quest = SelectedQuestField?.GetValue(questWindow) as Quest;
            if (quest == null || !quest.EverAccepted)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordQuestAccepted(quest);
        }
    }

    // Fires when a quest ends (Quest.End). Only Success ("completed") and Fail ("failed") outcomes
    // are recorded; Unknown and InvalidPreAcceptance are skipped. Each outcome fans out to every
    // eligible colonist with its own prompt group and emotional register.
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class QuestEndPatch
    {
        /// <summary>
        /// Harmony Postfix for Quest.End. Forwards Success/Fail outcomes to
        /// DiaryGameComponent.RecordQuestEnded, which maps them to the completed/failed signals.
        /// </summary>
        public static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            if (__instance == null)
            {
                return;
            }

            if (outcome != QuestEndOutcome.Success && outcome != QuestEndOutcome.Fail)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordQuestEnded(__instance, outcome);
        }
    }

    // Fires when a pawn gains a temporary thought (Thought_Memory). We only record thoughts
    // that have an expiration (durationDays > 0), filtering out permanent traits and
    // low-magnitude thoughts. The patch catches all temporary mood thoughts at the moment
    // they are added to the pawn's memory collection. We hook the Thought_Memory overload of
    // TryGainMemory because the ThoughtDef overload delegates to it, so this one catches both.
    public static class ThoughtGainPatch
    {
        /// <summary>
        /// Patches MemoryThoughtHandler.TryGainMemory(Thought_Memory, Pawn), if it can still be
        /// found. Safe to call even when the method name has changed: it logs and skips.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            MethodBase target = AccessTools.Method(
                typeof(MemoryThoughtHandler), "TryGainMemory",
                new[] { typeof(Thought_Memory), typeof(Pawn) });
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find MemoryThoughtHandler.TryGainMemory(Thought_Memory, Pawn); "
                    + "thought diary entries are disabled (a RimWorld update likely renamed it).");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ThoughtGainPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for MemoryThoughtHandler.TryGainMemory. Fires after a memory thought is
        /// gained by a pawn. Forwards temporary thoughts (Thought_Memory with expiration) to the
        /// diary. __instance is the owning pawn's handler; __0 is the gained memory.
        /// </summary>
        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory __0)
        {
            if (__instance == null || __0 == null || __0.def == null)
            {
                return;
            }

            Pawn pawn = __instance.pawn;
            if (pawn == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordThought(pawn, __0);
        }
    }
}
