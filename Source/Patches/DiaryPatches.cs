// Harmony patches — our hooks into vanilla RimWorld. We don't own the game's code, so we "patch"
// vanilla methods: Postfix hooks record PlayLog.Add social interactions,
// MentalStateHandler.TryStartMentalState social fights / mental breaks, TaleRecorder
// notable-history events, Pawn.Kill death details, Pawn.SetFaction colony arrivals,
// FogGrid area reveals, monolith investigation/activation, and GameConditionManager.RegisterCondition
// mood-affecting game conditions after RimWorld handles them. A Prefix hook redirects Social-tab
// play-log clicks into the matching Diary entry when one exists. AccessTools.Field reads private
// vanilla fields via reflection.
// New to this? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PawnDiary
{
    // Keeps the pawn who just caused a fog reveal available for FogGrid.NotifyAreaRevealed. Vanilla's
    // reveal result tells us what was uncovered (including ancient mech danger), but not who opened
    // the door. Notify_PawnEnteringDoor has that pawn, so this tiny cache bridges the two callbacks.
    internal static class AreaRevealDiscovererCache
    {
        private const int CacheLifetimeTicks = 300;
        private static readonly Dictionary<int, CachedDiscoverer> ByMap = new Dictionary<int, CachedDiscoverer>();

        public static void Note(Pawn pawn, Map map)
        {
            if (pawn == null || map == null || !pawn.IsColonist)
            {
                return;
            }

            ByMap[map.uniqueID] = new CachedDiscoverer(pawn, Find.TickManager.TicksGame);
        }

        public static Pawn LatestFor(Map map)
        {
            if (map == null)
            {
                return null;
            }

            CachedDiscoverer cached;
            if (!ByMap.TryGetValue(map.uniqueID, out cached))
            {
                return null;
            }

            if (Find.TickManager.TicksGame - cached.Tick > CacheLifetimeTicks)
            {
                ByMap.Remove(map.uniqueID);
                return null;
            }

            return cached.Pawn;
        }

        private struct CachedDiscoverer
        {
            public readonly Pawn Pawn;
            public readonly int Tick;

            public CachedDiscoverer(Pawn pawn, int tick)
            {
                Pawn = pawn;
                Tick = tick;
            }
        }
    }

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
            if (__instance == null || newFaction != Faction.OfPlayer || !__instance.IsColonist)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordColonistArrival(__instance,
                ArrivalContextCache.ConsumeOrBuild(__instance, "set_faction"));
        }
    }

    // Fires when vanilla sends the masterwork/legendary craft letter. We filter by quality in
    // DiaryGameComponent too, but patching this narrow method avoids watching every produced item.
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.SendCraftNotification))]
    public static class CraftQualityNotificationPatch
    {
        /// <summary>
        /// Harmony Postfix for QualityUtility.SendCraftNotification. Forwards the crafted item
        /// and worker so masterwork/legendary production can become a solo diary event.
        /// </summary>
        public static void Postfix(Thing thing, Pawn worker)
        {
            DiaryGameComponent.Current?.RecordCraftedQuality(thing, worker);
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
            if (pawn == null || !pawn.IsColonist)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordHediffAppeared(pawn, hediff);
        }
    }

    // Fires when a pawn finishes installing an ideology relic in a reliquary. The target is a
    // compiler-generated local function inside MakeNewToils, whose name (<MakeNewToils>b__5_5) can
    // shift between RimWorld versions. We deliberately do NOT register this via [HarmonyPatch] /
    // PatchAll: if the name no longer resolves, AccessTools.Method returns null and PatchAll would
    // THROW, aborting every other patch. Instead DiaryModStartup calls TryRegister, which null-checks
    // the method and logs a warning, so a future rename only disables relic entries — nothing else.
    public static class RelicInstallCompletionPatch
    {
        /// <summary>
        /// Patches the private final-action method inside JobDriver_InstallRelic.MakeNewToils, if it
        /// can still be found. Safe to call even when the method name has changed: it logs and skips.
        /// </summary>
        public static void TryRegister(Harmony harmony)
        {
            MethodBase target = AccessTools.Method(typeof(JobDriver_InstallRelic), "<MakeNewToils>b__5_5");
            if (target == null)
            {
                Log.Warning("[Pawn Diary] Could not find JobDriver_InstallRelic.<MakeNewToils>b__5_5; "
                    + "relic-install diary entries are disabled (a RimWorld update likely renamed it).");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(RelicInstallCompletionPatch), nameof(Postfix)));
        }

        /// <summary>
        /// Harmony Postfix for the install-relic completion action. Finds the relic from the job
        /// targets or the reliquary container, then records the installer pawn's diary event.
        /// </summary>
        public static void Postfix(JobDriver_InstallRelic __instance)
        {
            if (__instance == null)
            {
                return;
            }

            Thing relic = FindRelic(__instance);
            if (relic == null)
            {
                return;
            }

            DiaryGameComponent.Current?.RecordRelicInstalled(__instance.pawn, relic);
        }

        private static Thing FindRelic(JobDriver_InstallRelic driver)
        {
            Thing[] targets = new Thing[]
            {
                driver.job.GetTarget(TargetIndex.A).Thing,
                driver.job.GetTarget(TargetIndex.B).Thing,
                driver.job.GetTarget(TargetIndex.C).Thing
            };

            CompRelicContainer container = null;
            for (int i = 0; i < targets.Length; i++)
            {
                ThingWithComps thingWithComps = targets[i] as ThingWithComps;
                CompRelicContainer found = thingWithComps?.GetComp<CompRelicContainer>();
                if (found != null)
                {
                    container = found;
                    if (found.ContainedThing != null)
                    {
                        return found.ContainedThing;
                    }
                }
            }

            if (container == null)
            {
                return null;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                Thing target = targets[i];
                if (target != null && CompRelicContainer.IsRelic(target))
                {
                    return target;
                }
            }

            return null;
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

    // Fires just before a pawn entering a door can cause nearby fog to open. We cache the pawn so the
    // subsequent area-revealed callback can write the diary entry from the discoverer's POV.
    [HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Notify_PawnEnteringDoor))]
    public static class FogGridPawnEnteringDoorPatch
    {
        /// <summary>
        /// Harmony Prefix for FogGrid.Notify_PawnEnteringDoor. Remembers the entering pawn briefly.
        /// </summary>
        public static void Prefix(FogGrid __instance, Building_Door __0, Pawn __1)
        {
            AreaRevealDiscovererCache.Note(__1, __0?.Map);
        }
    }

    // Fires after RimWorld reveals an area from fog. The FloodUnfogResult tells us whether an
    // ancient mech threat was exposed; DiaryGameComponent also scans nearby newly-visible things for
    // CompLetterOnRevealed, covering similar special discovery letters.
    [HarmonyPatch(typeof(FogGrid), "NotifyAreaRevealed")]
    public static class FogGridAreaRevealedPatch
    {
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(FogGrid), "map");

        /// <summary>
        /// Harmony Postfix for FogGrid.NotifyAreaRevealed. Records notable pawn-caused discoveries.
        /// </summary>
        public static void Postfix(FogGrid __instance, IntVec3 __0, FloodUnfogResult __1)
        {
            Map map = MapField?.GetValue(__instance) as Map;
            Pawn discoverer = AreaRevealDiscovererCache.LatestFor(map);
            DiaryGameComponent.Current?.RecordAreaRevealed(discoverer, map, __0, __1);
        }
    }

    // Fires when a pawn studies/investigates a void or fallen monolith. The method carries the pawn,
    // so this can create the entry from exactly the discoverer's point of view.
    [HarmonyPatch(typeof(Building_VoidMonolith), nameof(Building_VoidMonolith.Investigate))]
    public static class VoidMonolithInvestigatedPatch
    {
        /// <summary>
        /// Harmony Postfix for Building_VoidMonolith.Investigate.
        /// </summary>
        public static void Postfix(Building_VoidMonolith __instance, Pawn __0)
        {
            DiaryGameComponent.Current?.RecordMonolithInvestigated(__0, __instance);
        }
    }

    // Fires when a pawn activates the monolith. Activation is intentionally separate from
    // investigation because it is a stronger story beat and deserves its own atmosphere.
    [HarmonyPatch(typeof(Building_VoidMonolith), nameof(Building_VoidMonolith.Activate))]
    public static class VoidMonolithActivatedPatch
    {
        /// <summary>
        /// Harmony Postfix for Building_VoidMonolith.Activate.
        /// </summary>
        public static void Postfix(Building_VoidMonolith __instance, Pawn __0)
        {
            DiaryGameComponent.Current?.RecordMonolithActivated(__0, __instance);
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
            PlayLogEntry_Interaction interactionEntry = entry as PlayLogEntry_Interaction;
            if (interactionEntry == null)
            {
                return;
            }

            InteractionDef interactionDef = IntDefField?.GetValue(interactionEntry) as InteractionDef;
            Pawn initiator = InitiatorField?.GetValue(interactionEntry) as Pawn;
            Pawn recipient = RecipientField?.GetValue(interactionEntry) as Pawn;
            string initiatorGameText = GameTextFromPov(interactionEntry, initiator);
            string recipientGameText = GameTextFromPov(interactionEntry, recipient);

            DiaryGameComponent.Current?.RecordInteraction(initiator, recipient, interactionDef,
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
