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

        // Tier B bookkeeping: pawnId -> hash of the persona text we last turned into an override. Lets
        // us skip pawns whose persona has not changed. In-memory only (not saved): after a reload the
        // first pass simply re-applies, which is harmless because SetPsychotypeOverride is idempotent.
        private static readonly Dictionary<string, int> AppliedPersonaHash = new Dictionary<string, int>();

        // Whether Tier B was active on the previous pass, so we can detect the moment it turns off and
        // clear every override we placed.
        private static bool tierBWasActive;
        private static readonly Dictionary<string, int> AppliedRimTalkHash = new Dictionary<string, int>();
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
        /// Tier B pass. MAIN THREAD ONLY (calls PawnDiaryApi and .Translate()). Applies/updates the
        /// persona-led voice override while the toggle is on, and clears all overrides the moment it
        /// turns off. No-op work when nothing changed.
        /// </summary>
        public static void RunPass()
        {
            PawnDiaryRimTalkBridgeSettings settings = PawnDiaryRimTalkBridgeMod.Settings;
            if (PawnDiaryRimTalkBridgeMod.LevelAtLeast(1) && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.PawnDiaryToRimTalk)
            {
                if (tierBWasActive)
                {
                    ResetAllOverrides();
                }
                tierBWasActive = false;
                foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
                {
                    ExportFor(pawn, settings.transformPersonaWithLlm);
                }
                return;
            }

            // Pawn Diary no longer owns RimTalk: promptly release any talk weights that silent-focus
            // temporarily forced, even when the opposite sync direction is still active.
            RestoreAllChattiness();

            bool active = PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                && settings != null
                && settings.personaSyncDirection == PersonaSyncDirection.RimTalkToPawnDiary
                && PawnDiaryApi.IsExternalApiEnabled;

            if (!active)
            {
                // Turned off (or level/master switch dropped): remove every override we placed. Core
                // refuses to clear another source's override, so resetting untouched pawns is a safe no-op.
                if (tierBWasActive)
                {
                    ResetAllOverrides();
                }

                tierBWasActive = false;
                return;
            }

            tierBWasActive = true;

            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
            {
                RefreshDiaryPersonaCacheFor(pawn);
                ImportFor(pawn, settings.transformPersonaWithLlm);
            }
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
            RestoreAllChattiness();
            foreach (Pawn pawn in TouchedPawns())
            {
                ResetBridgeOverrides(pawn);
            }

            AppliedPersonaHash.Clear();
            AppliedRimTalkHash.Clear();
            InFlight.Clear();
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
        public static void ResetForNewGame()
        {
            // Clear overrides BEFORE wiping the bookkeeping: TouchedPawns() reads AppliedPersonaHash to
            // resolve pawns we tracked. Order matters here. This also performs the one-time migration
            // sweep of stale writing-style overrides an older bridge version placed under Tier B.
            foreach (Pawn pawn in TouchedPawns())
            {
                ResetBridgeOverrides(pawn);
            }

            AppliedPersonaHash.Clear();
            AppliedRimTalkHash.Clear();
            InFlight.Clear();
            lock (DiaryPersonaGate)
            {
                DiaryPersonaCache.Clear();
            }
            tierBWasActive = false;
        }

        // Clears BOTH bridge-owned overrides for a pawn: the current psychotype override and any stale
        // writing-style override an older bridge version placed under Tier B. Pawn Diary refuses to clear
        // another source's override, so both calls are safe no-ops on pawns the bridge never touched.
        private static void ResetBridgeOverrides(Pawn pawn)
        {
            PawnDiaryApi.ResetPsychotypeOverride(pawn, BridgeIds.ModId);
            PawnDiaryApi.ResetWritingStyleOverride(pawn, BridgeIds.ModId);
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
            string persona = SafeGetPersonality(pawn);

            if (string.IsNullOrWhiteSpace(persona))
            {
                // Persona cleared: drop any override we had placed for this pawn.
                if (AppliedPersonaHash.ContainsKey(id))
                {
                    ResetBridgeOverrides(pawn);
                    AppliedPersonaHash.Remove(id);
                }

                return;
            }

            // GetHashCode is stable within a process, which is all we need for in-session change
            // detection (we never persist it), so it is fine for this comparison.
            // Include transform mode so flipping the checkbox re-synchronizes even when source text
            // itself did not change.
            int hash = unchecked(persona.GetHashCode() * 397 + (transform ? 1 : 0));
            int previous;
            if (AppliedPersonaHash.TryGetValue(id, out previous) && previous == hash)
            {
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
                    pawn, persona, false, hash, Pure.PersonaPromptModifier.None))
            {
                return;
            }
            string rule = "PawnDiaryRimTalkBridge.Persona.LensRule".Translate(firstSentence);
            if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule))
            {
                AppliedPersonaHash[id] = hash;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExportFor(Pawn pawn, bool transform)
        {
            if (pawn == null || !PawnDiaryApi.IsDiaryEligible(pawn)) return;
            Pure.PersonaPromptModifier modifier;
            string psychotypeDefName;
            string source = RefreshDiaryPersonaCacheFor(pawn, out modifier, out psychotypeDefName);
            ApplyChattinessPolicy(pawn, modifier, psychotypeDefName);
            if (string.IsNullOrWhiteSpace(source)) return;
            Pure.PersonaPromptModifier transformModifier = Pure.PersonaPromptPolicy.TransformModifier(modifier);
            int hash = unchecked((source.GetHashCode() * 397 + (transform ? 1 : 0)) * 397
                + (int)transformModifier);
            string id = pawn.GetUniqueLoadID();
            if (AppliedRimTalkHash.TryGetValue(id, out int oldHash) && oldHash == hash) return;
            if (transform && StartOrPollTransform(pawn, source, true, hash, transformModifier)) return;
            SafeSetPersonality(pawn, source);
            AppliedRimTalkHash[id] = hash;
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

        private static bool StartOrPollTransform(Pawn pawn, string source, bool export, int sourceHash,
            Pure.PersonaPromptModifier modifier)
        {
            string id = pawn.GetUniqueLoadID();
            if (InFlight.TryGetValue(id, out TransformJob job))
            {
                if (job.export != export || job.sourceHash != sourceHash)
                {
                    InFlight.Remove(id);
                    return StartOrPollTransform(pawn, source, export, sourceHash, modifier);
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
                    SafeSetPersonality(pawn, cleaned);
                    AppliedRimTalkHash[id] = job.sourceHash;
                }
                else if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, cleaned))
                {
                    AppliedPersonaHash[id] = job.sourceHash;
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
            InFlight[id] = new TransformJob { handle = handle, export = export, sourceHash = sourceHash };
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SafeSetPersonality(Pawn pawn, string persona)
        {
            try { PersonaService.SetPersonality(pawn, Pure.PersonaTransferText.Clean(persona)); }
            catch { }
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
            AppliedPersonaHash.Remove(id);
            InFlight.Remove(id);
            ImportFor(pawn, true);
        }

        internal static void RegenerateExport(Pawn pawn)
        {
            if (!CanRegenerateExport(pawn)) return;
            string id = pawn.GetUniqueLoadID();
            AppliedRimTalkHash.Remove(id);
            InFlight.Remove(id);
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
                component.RememberOriginalTalkWeight(id, SafeGetTalkInitiationWeight(pawn));
            }

            float target = Pure.PersonaPromptPolicy.ForcesZeroChattiness(modifier)
                ? 0f
                : PersonaChattinessPolicyDef.Current.ChattinessFor(id, psychotypeDefName);
            // Repeat on every periodic pass so the authority choice remains true even if RimTalk's
            // editor is used while Pawn Diary controls the persona. silent-focus always wins at zero.
            SafeSetTalkInitiationWeight(pawn, target);
        }

        private static void RestoreAllChattiness()
        {
            RimTalkBridgeGameComponent component = Current.Game?.GetComponent<RimTalkBridgeGameComponent>();
            if (component == null) return;
            foreach (Pawn pawn in TouchedPawns())
            {
                RestoreChattiness(pawn, component);
            }
        }

        private static void RestoreChattiness(Pawn pawn, RimTalkBridgeGameComponent component)
        {
            if (pawn == null || component == null) return;
            string id = pawn.GetUniqueLoadID();
            float original;
            if (!component.TryGetOriginalTalkWeight(id, out original)) return;
            SafeSetTalkInitiationWeight(pawn, original);
            component.ForgetOriginalTalkWeight(id);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float SafeGetTalkInitiationWeight(Pawn pawn)
        {
            try { return PersonaService.GetTalkInitiationWeight(pawn); }
            catch { return 1f; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SafeSetTalkInitiationWeight(Pawn pawn, float weight)
        {
            try { PersonaService.SetTalkInitiationWeight(pawn, weight); }
            catch { }
        }

        private sealed class TransformJob
        {
            public int handle;
            public bool export;
            public int sourceHash;
        }

        /// <summary>
        /// Tier A provider. Called by Pawn Diary on the main thread while building a pawn summary.
        /// Returns "chat_persona=&lt;first sentence&gt;", or null when there is nothing to add.
        /// Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProvidePersonaLine(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                || PawnDiaryRimTalkBridgeMod.Settings.personaSyncDirection != PersonaSyncDirection.RimTalkToPawnDiary)
            {
                return null;
            }

            string persona = SafeGetPersonality(pawn);
            if (string.IsNullOrWhiteSpace(persona))
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
        private static string SafeGetPersonality(Pawn pawn)
        {
            try
            {
                return PersonaService.GetPersonality(pawn);
            }
            catch
            {
                return null;
            }
        }
    }
}
