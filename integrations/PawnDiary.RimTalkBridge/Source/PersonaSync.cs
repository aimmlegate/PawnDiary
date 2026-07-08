// Persona sync — the "who is this pawn" half of the bridge. Two tiers, both code-only:
//
//   • Tier A (ships at level 1): mutual awareness. Pawn Diary sees a compact "chat_persona=<first
//     sentence of the RimTalk persona>" line via a registered context provider. Nothing is mutated.
//   • Tier B (advanced, default OFF, experimental): persona-led diary voice. The pawn's RimTalk
//     character description is turned into a Pawn Diary PSYCHOTYPE (outlook) OVERRIDE — RimTalk supplies
//     WHO the pawn is; Pawn Diary keeps HOW they write. Reapplied when the persona text changes and
//     cleared when the toggle turns off. (Earlier versions placed a writing-STYLE override here; every
//     bridge reset now also sweeps that stale style override so old saves migrate cleanly.)
//
// Recorded design rule: pawns share identity/memory but keep SEPARATE voices. We only ever READ
// RimTalk's persona; we never write into it (that would mutate another mod's save).
//
// RimTalk-type isolation: the methods that call RimTalk.Data.PersonaService are [NoInlining] and are
// only reached after the mod's RimTalkActive guard (see RIMTALK_BRIDGE_PLAN Step 0).
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PawnDiary.Integration;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Reads RimTalk personas and exposes them to Pawn Diary as context (Tier A) or as an optional
    /// writing-voice override (Tier B). Never writes back into RimTalk.
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

        /// <summary>
        /// Registers the Tier A "chat_persona=" context provider. Process-global and idempotent; call
        /// once from the mod constructor, only when RimTalk is active.
        /// </summary>
        public static void RegisterContextProvider()
        {
            PawnDiaryApi.RegisterPawnContextProvider(BridgeIds.PersonaProviderId, ProvidePersonaLine);
        }

        /// <summary>
        /// Tier B pass. MAIN THREAD ONLY (calls PawnDiaryApi and .Translate()). Applies/updates the
        /// persona-led voice override while the toggle is on, and clears all overrides the moment it
        /// turns off. No-op work when nothing changed.
        /// </summary>
        public static void RunTierBPass()
        {
            bool active = PawnDiaryRimTalkBridgeMod.LevelAtLeast(1)
                && PawnDiaryRimTalkBridgeMod.Settings != null
                && PawnDiaryRimTalkBridgeMod.Settings.personaLedDiaryVoice
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
                ApplyTierBFor(pawn);
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
            foreach (Pawn pawn in TouchedPawns())
            {
                ResetBridgeOverrides(pawn);
            }

            AppliedPersonaHash.Clear();
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
        private static void ApplyTierBFor(Pawn pawn)
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
            int hash = persona.GetHashCode();
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
            string rule = "PawnDiaryRimTalkBridge.Persona.LensRule".Translate(firstSentence);
            if (PawnDiaryApi.SetPsychotypeOverride(pawn, BridgeIds.ModId, rule))
            {
                AppliedPersonaHash[id] = hash;
            }
        }

        /// <summary>
        /// Tier A provider. Called by Pawn Diary on the main thread while building a pawn summary.
        /// Returns "chat_persona=&lt;first sentence&gt;", or null when there is nothing to add.
        /// Names RimTalk types.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProvidePersonaLine(Pawn pawn)
        {
            if (pawn == null || !PawnDiaryRimTalkBridgeMod.LevelAtLeast(1))
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
