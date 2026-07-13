// Persona sync — the "who is this pawn" half of the bridge. The player chooses one authority:
// import RimTalk into Pawn Diary, or publish Pawn Diary's psychotype into RimTalk. Writing-style
// prose never crosses the bridge: a small allowlist of Def identities may select a transform policy,
// and silent-focus additionally forces RimTalk chattiness to zero. An optional
// one-shot LLM rewrite is available in either direction. Template authors can also opt into the
// cached {{ pawn.diary_persona }} variable; it is deliberately never auto-injected.
//
// RimTalk-type isolation: the methods that call RimTalk.Data.PersonaService are [NoInlining] and are
// only reached after the mod's RimTalkActive guard (see RIMTALK_BRIDGE_PLAN Step 0).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.Data;
using RimTalk.API;
using RimWorld;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Synchronizes personas in the selected direction and publishes the opt-in cached template variable.
    /// </summary>
    internal static class PersonaSync
    {
        // How much of a (possibly multi-paragraph) persona we keep. First sentence, capped. Defensive
        // limit rather than tunable policy — core caps provider/override text again anyway.
        private const int PersonaSentenceCap = 200;

        // Structured prompt-schema key. Stays English on purpose (carve-out): it is a machine key the
        // diary prompt reads, not prose. The VALUE after it is game/persona text and is not translated.
        private const string PersonaSchemaKey = "chat_persona=";

        // Direction state is transient; the component owns persistent source keys, transformed output,
        // and reversible RimTalk backups. A first-tick flag keeps API calls out of FinalizeInit.
        private static bool importWasActive;
        private static bool exportWasActive;
        private static bool newGameResetPending;
        private static readonly Dictionary<string, TransformJob> InFlight = new Dictionary<string, TransformJob>();
        private static readonly object DiaryPersonaGate = new object();
        private static readonly Dictionary<string, string> DiaryPersonaCache = new Dictionary<string, string>();

        /// <summary>
        /// Registers the Tier A "chat_persona=" context provider. Process-global and idempotent; call
        /// once from the mod constructor, only when RimTalk is active.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RegisterContextProvider()
        {
            PawnDiaryApi.RegisterPawnContextProvider(BridgeIds.PersonaProviderId, ProvidePersonaLine);
            ContextHookRegistry.RegisterPawnVariable(
                BridgeIds.DiaryPersonaVariableName,
                BridgeIds.ModId,
                ProvideDiaryPersonaVariable,
                "PawnDiaryRimTalkBridge.Prompt.DiaryPersonaVariableDesc".Translate(),
                100);
        }

        /// <summary>
        /// Lets Pawn Diary's own psychotype editor expose the import transform's Regenerate button.
        /// Registration is process-global; callbacks re-check current direction/settings each time.
        /// </summary>
        public static void RegisterExternalGenerator()
        {
            PawnDiaryApi.RegisterExternalPsychotypeGenerator(new ExternalPsychotypeGenerator
            {
                sourceId = BridgeIds.ModId,
                canReroll = CanRegenerateImport,
                isBusy = IsTransformBusy,
                reroll = RegenerateImport
            });
        }

        /// <summary>
        /// Tier B pass. MAIN THREAD ONLY (calls PawnDiaryApi and .Translate()). Maintains the selected
        /// source-owned direction and releases that authority when the direction or master switch turns off.
        /// </summary>
        public static void RunPass()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component == null || !PawnDiaryApi.IsReady)
            {
                return;
            }

            if (newGameResetPending)
            {
                // One-time migration: older bridge builds wrote writing-style and later psychotype
                // overrides without the component's ownership dictionaries. Clear those untracked rows;
                // a current saved import target is retained and reapplied below without another LLM call.
                foreach (Pawn pawn in TouchedPawns())
                {
                    PawnDiaryApi.ResetWritingStyleOverride(pawn, BridgeIds.ModId);
                    if (!component.HasImportTarget(pawn.GetUniqueLoadID()))
                    {
                        PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
                    }
                }

                newGameResetPending = false;
            }

            bool exportActive = PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk
                && PawnDiaryApi.IsExternalApiEnabled;
            bool importActive = PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.RimTalkToPawnDiary
                && PawnDiaryApi.IsExternalApiEnabled;

            if (exportActive)
            {
                if (importWasActive || component.HasImportTargets)
                {
                    ReleaseAllImportOverrides(component);
                }

                importWasActive = false;
                exportWasActive = true;
                foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
                {
                    ExportFor(pawn, settings.transformPersonaWithLlm);
                }
                return;
            }

            if (importActive)
            {
                if (exportWasActive || component.HasExportOwnership)
                {
                    RestoreAllExportState(component);
                }

                exportWasActive = false;
                importWasActive = true;
                foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
                {
                    RefreshDiaryPersonaCacheFor(pawn);
                    ImportFor(pawn, settings.transformPersonaWithLlm);
                }
                return;
            }

            // Level/direction/master switch off: release every source-owned field and every reversible
            // RimTalk field. Cleanup API calls deliberately remain legal while the master switch is off.
            if (importWasActive || component.HasImportTargets)
            {
                ReleaseAllImportOverrides(component);
            }
            if (exportWasActive || component.HasExportOwnership)
            {
                RestoreAllExportState(component);
            }
            importWasActive = false;
            exportWasActive = false;
        }

        /// <summary>
        /// Clears the bridge's writing-style overrides from every pawn we may have touched, and forgets
        /// our bookkeeping. Called on toggle-off and on new-game reset. Walks a broad pawn set — not
        /// just spawned free colonists — so an override placed on a pawn that later went downed, joined
        /// a caravan, entered cryptosleep, or left the map is still cleared. PawnDiary refuses to clear
        /// another source's override, so touching untouched pawns is a safe no-op.
        /// </summary>
        public static void ResetAllOverrides()
        {
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component != null)
            {
                ReleaseAllImportOverrides(component);
                RestoreAllExportState(component);
            }
            CancelAllTransforms();
            lock (DiaryPersonaGate)
            {
                DiaryPersonaCache.Clear();
            }
        }

        /// <summary>
        /// Clears overrides AND bookkeeping on load. Overrides placed in a previous colony are owned by
        /// PawnDiary's per-pawn saved state; clearing them here (against the broad pawn set) prevents a
        /// stale bridge voice from surviving a colony switch when Tier B was on at save time.
        /// </summary>
        public static void PrepareForNewGame()
        {
            // Core resets the whole completion session when a new Game is constructed, so these old
            // handles are already cancelled. Do not call the main-thread API from FinalizeInit.
            InFlight.Clear();
            lock (DiaryPersonaGate)
            {
                DiaryPersonaCache.Clear();
            }
            importWasActive = false;
            exportWasActive = false;
            newGameResetPending = true;
        }

        // Clears BOTH bridge-owned overrides for a pawn: the current psychotype override and any stale
        // writing-style override an older bridge version placed under Tier B. Pawn Diary refuses to clear
        // another source's override, so both calls are safe no-ops on pawns the bridge never touched.
        private static bool ResetBridgeOverrides(Pawn pawn)
        {
            bool released = PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            PawnDiaryApi.ResetWritingStyleOverride(pawn, BridgeIds.ModId);
            return released;
        }

        private static void ReleaseImportFor(Pawn pawn, string pawnId,
            RimTalkBridgeGameComponent component)
        {
            CancelTransformFor(pawnId);
            if (ResetBridgeOverrides(pawn)) component.ForgetImportTarget(pawnId);
        }

        private static void ReleaseAllImportOverrides(RimTalkBridgeGameComponent component)
        {
            CancelAllTransforms();
            bool allReleased = true;
            foreach (Pawn pawn in TouchedPawns())
            {
                string id = pawn.GetUniqueLoadID();
                if (!component.HasImportTarget(id)) continue;
                if (ResetBridgeOverrides(pawn)) component.ForgetImportTarget(id);
                else allReleased = false;
            }

            if (allReleased) component.ClearImportTargets();
        }

        private static bool EnsureExportPersona(Pawn pawn, string target,
            RimTalkBridgeGameComponent component)
        {
            if (pawn == null || component == null) return false;
            string id = pawn.GetUniqueLoadID();
            string current;
            if (!TryGetPersonality(pawn, out current)) return false;

            string original;
            if (!component.TryGetOriginalPersona(id, out original))
            {
                component.RememberOriginalPersona(id, current);
            }

            string cleanedTarget = Pure.PersonaTransferText.Clean(target);
            return string.Equals(current ?? string.Empty, cleanedTarget, StringComparison.Ordinal)
                || TrySetPersonality(pawn, cleanedTarget);
        }

        private static bool RestoreExportFor(Pawn pawn, RimTalkBridgeGameComponent component)
        {
            if (pawn == null || component == null) return false;
            string id = pawn.GetUniqueLoadID();
            bool personaRestored = RestorePersona(pawn, component);
            bool chattinessRestored = RestoreChattiness(pawn, component);
            if (personaRestored && chattinessRestored) component.ForgetExportTarget(id);
            return personaRestored && chattinessRestored;
        }

        private static void RestoreAllExportState(RimTalkBridgeGameComponent component)
        {
            CancelAllTransforms();
            bool allRestored = true;
            foreach (Pawn pawn in TouchedPawns())
            {
                if (!RestoreExportFor(pawn, component)) allRestored = false;
            }

            if (allRestored)
            {
                component.ClearExportTargets();
                component.ClearOriginalPersonaBackups();
                component.ClearOriginalTalkWeightBackups();
            }
        }

        private static bool RestorePersona(Pawn pawn, RimTalkBridgeGameComponent component)
        {
            string id = pawn.GetUniqueLoadID();
            string original;
            if (!component.TryGetOriginalPersona(id, out original)) return true;
            if (!TrySetPersonality(pawn, original)) return false;
            component.ForgetOriginalPersona(id);
            return true;
        }

        private static void CancelTransformFor(string pawnId)
        {
            if (string.IsNullOrWhiteSpace(pawnId)) return;
            TransformJob job;
            if (!InFlight.TryGetValue(pawnId, out job)) return;
            PawnDiaryApi.CancelLlmCompletion(job.handle);
            InFlight.Remove(pawnId);
        }

        private static void CancelAllTransforms()
        {
            List<TransformJob> jobs = new List<TransformJob>(InFlight.Values);
            InFlight.Clear();
            for (int i = 0; i < jobs.Count; i++)
            {
                PawnDiaryApi.CancelLlmCompletion(jobs[i].handle);
            }
        }

        // Every pawn the bridge may have given a Tier B override: spawned free colonists, caravans and
        // traveling transport pods, plus world pawns (covers downed/caravan/cryptosleep/departed).
        // Deduplicated by load id so a pawn present in two sources is reset once.
        private static IEnumerable<Pawn> TouchedPawns()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                {
                    yield return pawn;
                }
            }
            if (Find.WorldPawns != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    if (pawn != null && seen.Add(pawn.GetUniqueLoadID()))
                    {
                        yield return pawn;
                    }
                }
            }
        }

        /// <summary>Applies or refreshes one pawn's Tier B override. Main thread. Names RimTalk types.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ImportFor(Pawn pawn, bool transform)
        {
            if (pawn == null)
            {
                return;
            }

            string id = pawn.GetUniqueLoadID();
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component == null) return;

            if (!PawnDiaryApi.IsDiaryEligible(pawn))
            {
                ReleaseImportFor(pawn, id, component);
                return;
            }

            string persona;
            if (!TryGetPersonality(pawn, out persona)) return;

            if (string.IsNullOrWhiteSpace(persona))
            {
                ReleaseImportFor(pawn, id, component);
                return;
            }

            string sourceKey = Pure.PersonaSyncKey.ForImport(persona, transform);
            string storedTarget;
            if (component.TryGetImportTarget(id, sourceKey, out storedTarget))
            {
                PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, storedTarget);
                return;
            }

            string firstSentence = ContextFormat.FirstSentenceCap(persona, PersonaSentenceCap);
            if (firstSentence.Length == 0)
            {
                return;
            }

            // Tier B repointed: the persona feeds Pawn Diary's PSYCHOTYPE (outlook) override, not the
            // writing style. RimTalk supplies who the pawn is; Pawn Diary keeps how they write.
            if (transform && StartOrPollTransform(
                    pawn, persona, false, sourceKey, Pure.PersonaPromptModifier.None, component))
            {
                return;
            }
            string rule = "PawnDiaryRimTalkBridge.Persona.LensRule".Translate(firstSentence).Resolve();
            component.RememberImportTarget(id, sourceKey, rule);
            PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExportFor(Pawn pawn, bool transform)
        {
            if (pawn == null) return;
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component == null) return;
            string id = pawn.GetUniqueLoadID();
            if (!PawnDiaryApi.IsDiaryEligible(pawn))
            {
                RestoreExportFor(pawn, component);
                return;
            }

            Pure.PersonaPromptModifier modifier;
            string psychotypeDefName;
            string source = RefreshDiaryPersonaCacheFor(pawn, out modifier, out psychotypeDefName);
            ApplyChattinessPolicy(pawn, modifier, psychotypeDefName);
            Pure.PersonaPromptModifier transformModifier = Pure.PersonaPromptPolicy.TransformModifier(modifier);
            string sourceKey = Pure.PersonaSyncKey.ForExport(source, transform, transformModifier);
            string storedTarget;
            if (component.TryGetExportTarget(id, sourceKey, out storedTarget))
            {
                EnsureExportPersona(pawn, storedTarget, component);
                return;
            }

            // Neutral/disabled psychotype has an empty source. It deliberately clears RimTalk's current
            // persona while Pawn Diary owns the field, then the original is restored when authority ends.
            if (transform && !string.IsNullOrWhiteSpace(source)
                && StartOrPollTransform(pawn, source, true, sourceKey, transformModifier, component))
            {
                return;
            }

            component.RememberExportTarget(id, sourceKey, source);
            EnsureExportPersona(pawn, source, component);
        }

        // Main-thread cache refresh. RimTalk may evaluate Scriban variables on a worker thread, where
        // PawnDiaryApi reads are rejected, so the template provider below only reads this snapshot.
        private static string RefreshDiaryPersonaCacheFor(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryApi.IsDiaryEligible(pawn))
            {
                return string.Empty;
            }

            Pure.PersonaPromptModifier ignored;
            string ignoredPsychotype;
            return RefreshDiaryPersonaCacheFor(pawn, out ignored, out ignoredPsychotype);
        }

        private static string RefreshDiaryPersonaCacheFor(Pawn pawn, out Pure.PersonaPromptModifier modifier,
            out string psychotypeDefName)
        {
            modifier = Pure.PersonaPromptModifier.None;
            psychotypeDefName = string.Empty;
            if (pawn == null || !PawnDiaryApi.IsDiaryEligible(pawn))
            {
                return string.Empty;
            }

            DiaryPsychotypeSnapshot outlook = PawnDiaryApi.GetPsychotype(pawn);
            DiaryWritingStyleSnapshot style = PawnDiaryApi.GetWritingStyle(pawn);
            modifier = Pure.PersonaPromptPolicy.Resolve(style?.styleDefName, ActiveHediffDefNames(pawn));
            psychotypeDefName = outlook?.psychotypeDefName ?? string.Empty;
            string text = Pure.PersonaTransferText.FromPsychotype(outlook?.rule);
            lock (DiaryPersonaGate)
            {
                DiaryPersonaCache[pawn.GetUniqueLoadID()] = text;
            }
            return text;
        }

        // Snapshot only Def names. The five exact string matches are DLC-safe and the pure mapper owns
        // precedence. No writing-style rule text is read or sent to RimTalk/the LLM.
        private static List<string> ActiveHediffDefNames(Pawn pawn)
        {
            List<string> names = new List<string>();
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return names;
            for (int i = 0; i < hediffs.Count; i++)
            {
                string defName = hediffs[i]?.def?.defName;
                if (!string.IsNullOrWhiteSpace(defName)) names.Add(defName);
            }
            return names;
        }

        /// <summary>Opt-in {{ pawn.diary_persona }} provider. Never auto-injected into a prompt.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProvideDiaryPersonaVariable(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                || !PawnDiaryApi.IsExternalApiEnabled)
            {
                return string.Empty;
            }

            lock (DiaryPersonaGate)
            {
                return DiaryPersonaCache.TryGetValue(pawn.GetUniqueLoadID(), out string text)
                    ? text ?? string.Empty
                    : string.Empty;
            }
        }

        private static bool StartOrPollTransform(Pawn pawn, string source, bool export, string sourceKey,
            Pure.PersonaPromptModifier modifier, RimTalkBridgeGameComponent component)
        {
            string id = pawn.GetUniqueLoadID();
            if (InFlight.TryGetValue(id, out TransformJob job))
            {
                if (job.export != export || !string.Equals(job.sourceKey, sourceKey, StringComparison.Ordinal))
                {
                    PawnDiaryApi.CancelLlmCompletion(job.handle);
                    InFlight.Remove(id);
                    return StartOrPollTransform(pawn, source, export, sourceKey, modifier, component);
                }
                LlmCompletionResult result = PawnDiaryApi.GetLlmCompletionResult(job.handle);
                if (result.status == LlmCompletionStatus.Pending) return true;
                InFlight.Remove(id);
                if (result.status != LlmCompletionStatus.Succeeded || string.IsNullOrWhiteSpace(result.text)) return false;
                string cleaned = Pure.PersonaTransferText.Clean(result.text);
                if (cleaned.Length == 0)
                {
                    return false;
                }
                if (job.export)
                {
                    component.RememberExportTarget(id, job.sourceKey, cleaned);
                    EnsureExportPersona(pawn, cleaned, component);
                }
                else
                {
                    component.RememberImportTarget(id, job.sourceKey, cleaned);
                    PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, cleaned);
                }
                return true;
            }
            string promptKey = export
                ? "PawnDiaryRimTalkBridge.Persona.ExportTransformPrompt"
                : "PawnDiaryRimTalkBridge.Persona.ImportTransformPrompt";
            string systemPrompt = promptKey.Translate();
            string modifierInstruction = ModifierInstruction(modifier);
            if (!string.IsNullOrWhiteSpace(modifierInstruction))
            {
                systemPrompt += "\n" + modifierInstruction;
            }
            int handle = PawnDiaryApi.RequestLlmCompletion(new ExternalLlmCompletionRequest
            {
                sourceId = BridgeIds.ModId,
                laneIndex = -1,
                systemPrompt = systemPrompt,
                userText = source,
                maxTokens = 300
            });
            if (handle <= 0) return false;
            InFlight[id] = new TransformJob { handle = handle, export = export, sourceKey = sourceKey };
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TrySetPersonality(Pawn pawn, string persona)
        {
            try
            {
                PersonaService.SetPersonality(pawn, Pure.PersonaTransferText.Clean(persona));
                return true;
            }
            catch { return false; }
        }

        private static string ModifierInstruction(Pure.PersonaPromptModifier modifier)
        {
            string key;
            switch (modifier)
            {
                case Pure.PersonaPromptModifier.MindCrumbled:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.MindCrumbled"; break;
                // Silent-focus changes participation only. It must not inject a speech-style prompt.
                case Pure.PersonaPromptModifier.SilentFocus:
                case Pure.PersonaPromptModifier.None:
                    return string.Empty;
                case Pure.PersonaPromptModifier.PainNeedle:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.PainNeedle"; break;
                case Pure.PersonaPromptModifier.BlankBliss:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.BlankBliss"; break;
                case Pure.PersonaPromptModifier.BrightFog:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.BrightFog"; break;
                case Pure.PersonaPromptModifier.ChildPlainOrder:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.ChildPlainOrder"; break;
                case Pure.PersonaPromptModifier.ChildBigFeeling:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.ChildBigFeeling"; break;
                case Pure.PersonaPromptModifier.ChildLiteralWatch:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.ChildLiteralWatch"; break;
                case Pure.PersonaPromptModifier.ChildQuestion:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.ChildQuestion"; break;
                case Pure.PersonaPromptModifier.ChildBrave:
                    key = "PawnDiaryRimTalkBridge.Persona.Modifier.ChildBrave"; break;
                default:
                    return string.Empty;
            }
            return key.Translate();
        }

        /// <summary>True while this pawn has a persona transform request in flight.</summary>
        internal static bool IsTransformBusy(Pawn pawn)
        {
            return pawn != null && InFlight.ContainsKey(pawn.GetUniqueLoadID());
        }

        internal static bool CanRegenerateImport(Pawn pawn)
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return pawn != null && PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.RimTalkToPawnDiary
                && settings.transformPersonaWithLlm && PawnDiaryApi.IsExternalApiEnabled;
        }

        internal static bool CanRegenerateExport(Pawn pawn)
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            return pawn != null && PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk
                && settings.transformPersonaWithLlm && PawnDiaryApi.IsExternalApiEnabled;
        }

        internal static void RegenerateImport(Pawn pawn)
        {
            if (!CanRegenerateImport(pawn)) return;
            string id = pawn.GetUniqueLoadID();
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            component?.ForgetImportTarget(id);
            CancelTransformFor(id);
            ImportFor(pawn, true);
        }

        internal static void RegenerateExport(Pawn pawn)
        {
            if (!CanRegenerateExport(pawn)) return;
            string id = pawn.GetUniqueLoadID();
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            component?.ForgetExportTarget(id);
            CancelTransformFor(id);
            ExportFor(pawn, true);
        }

        private static void ApplyChattinessPolicy(Pawn pawn, Pure.PersonaPromptModifier modifier,
            string psychotypeDefName)
        {
            if (pawn == null) return;
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component == null) return;

            string id = pawn.GetUniqueLoadID();
            float original;
            if (!component.TryGetOriginalTalkWeight(id, out original))
            {
                if (!TryGetTalkInitiationWeight(pawn, out original)) return;
                component.RememberOriginalTalkWeight(id, original);
            }

            float target = Pure.PersonaPromptPolicy.ForcesZeroChattiness(modifier)
                ? 0f
                : PersonaChattinessPolicyDef.Current.ChattinessFor(id, psychotypeDefName);
            // Repeat on every periodic pass so the authority choice remains true even if RimTalk's
            // editor is used while Pawn Diary controls the persona. silent-focus always wins at zero.
            TrySetTalkInitiationWeight(pawn, target);
        }

        private static bool RestoreChattiness(Pawn pawn, RimTalkBridgeGameComponent component)
        {
            if (pawn == null || component == null) return false;
            string id = pawn.GetUniqueLoadID();
            float original;
            if (!component.TryGetOriginalTalkWeight(id, out original)) return true;
            if (float.IsNaN(original) || float.IsInfinity(original))
            {
                // A hand-edited/corrupt save cannot provide a meaningful value to restore. Drop only
                // that invalid backup instead of passing NaN/Infinity into RimTalk every sync pass.
                component.ForgetOriginalTalkWeight(id);
                return true;
            }
            if (!TrySetTalkInitiationWeight(pawn, original)) return false;
            component.ForgetOriginalTalkWeight(id);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGetTalkInitiationWeight(Pawn pawn, out float weight)
        {
            try
            {
                weight = PersonaService.GetTalkInitiationWeight(pawn);
                return !float.IsNaN(weight) && !float.IsInfinity(weight);
            }
            catch
            {
                weight = 0f;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TrySetTalkInitiationWeight(Pawn pawn, float weight)
        {
            try
            {
                PersonaService.SetTalkInitiationWeight(pawn, weight);
                return true;
            }
            catch { return false; }
        }

        private sealed class TransformJob
        {
            public int handle;
            public bool export;
            public string sourceKey;
        }

        /// <summary>
        /// Tier A provider. Called by Pawn Diary on the main thread while building a pawn summary.
        /// Returns "chat_persona=&lt;first sentence&gt;", or null when there is nothing to add.
        /// Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProvidePersonaLine(Pawn pawn)
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (pawn == null || !PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) || settings == null
                || settings.personaSyncDirection != PersonaSyncDirection.RimTalkToPawnDiary)
            {
                return null;
            }

            string persona;
            if (!TryGetPersonality(pawn, out persona) || string.IsNullOrWhiteSpace(persona))
            {
                return null;
            }

            string firstSentence = ContextFormat.FirstSentenceCap(persona, PersonaSentenceCap);
            if (firstSentence.Length == 0)
            {
                return null;
            }

            return PersonaSchemaKey + firstSentence;
        }

        /// <summary>Reads the RimTalk persona defensively; RimTalk data reads can throw on odd pawns.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool TryGetPersonality(Pawn pawn, out string personality)
        {
            try
            {
                personality = PersonaService.GetPersonality(pawn) ?? string.Empty;
                return true;
            }
            catch
            {
                personality = string.Empty;
                return false;
            }
        }
    }
}
