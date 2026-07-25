// Quality Wave H6: hooks RimWorld's art generation so a sculpture about a colony deed can produce one
// quiet diary page. Both hooks are public, stable CompArt methods, so attribute discovery is safe.
//
// WHY TWO HOOKS (verified against the 1.6 assemblies by scanning every caller):
//   * `CompQuality.SetQuality`, `Frame.CompleteConstruction`, and `JobDriver_BuildCubeSculpture` call
//     `InitializeArt`, which is where the tale is actually attached — but it never sees the artist.
//   * `GenRecipe.PostProcessProduct` and `Frame.CompleteConstruction` then call `JustCreatedBy(pawn)`,
//     which knows the artist but does no art work of its own.
//   * `InitializeArt` always runs FIRST. That ordering is exactly what the locked writer order wants:
//     the first hook claims the deed for the pawn it is ABOUT, and the second hook only gets a turn
//     when nobody in the tale could write, which is precisely when the artist fallback applies.
// Firing twice is harmless because ownership is exactly-once per deed.
//
// Everything decided here is delegated: the writer order and the sampling live in the pure
// Source/Pipeline/ArtImmortalizationPolicy.cs, and ownership lives in
// DiaryGameComponent.ArtOwnership.cs. New to C#/RimWorld? See AGENTS.md ("Harmony patches").
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PawnDiary.Capture;
using PawnDiary.Ingestion;
using RimWorld;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Fires after the artwork's tale is attached. At this point the deed is known but the artist is
    /// not, so only the deed's own subject can be chosen as writer.
    /// </summary>
    [HarmonyPatch(typeof(CompArt), nameof(CompArt.InitializeArt), new[] { typeof(ArtGenerationContext) })]
    internal static class CompArtInitializeArtPatch
    {
        public static void Postfix(CompArt __instance)
        {
            DiaryPatchSafety.Run("Art immortalization (art generated)", __instance,
                comp => DiaryArtImmortalization.TryRecord(comp, null));
        }
    }

    /// <summary>
    /// Fires once the artist is known. Runs after <see cref="CompArtInitializeArtPatch"/>, so it only
    /// ever produces a page when the deed's own subject could not write it.
    /// </summary>
    [HarmonyPatch(typeof(CompArt), nameof(CompArt.JustCreatedBy))]
    internal static class CompArtJustCreatedByPatch
    {
        public static void Postfix(CompArt __instance, Pawn pawn)
        {
            DiaryPatchSafety.Run("Art immortalization (artist known)", (__instance, pawn),
                state => DiaryArtImmortalization.TryRecord(state.Item1, state.Item2));
        }
    }

    /// <summary>
    /// The shared body behind both art hooks: resolve the deed, confirm it is unclaimed, sample, pick
    /// a writer, and submit. Every exit is a silent no-op — art generation must never break because
    /// the diary declined to write about it.
    /// </summary>
    internal static class DiaryArtImmortalization
    {
        // RimWorld exposes TaleReference.IsNullTale but not the Tale itself, and we need the tale's
        // stable def name + numeric ID to build an exact ownership key. AccessTools caches the lookup;
        // a null handle (renamed field in a future version) makes the whole feature no-op rather than
        // fall back to matching translated art prose, which would eventually double-write a deed.
        private static readonly FieldInfo TaleField = AccessTools.Field(typeof(TaleReference), "tale");

        public static void TryRecord(CompArt comp, Pawn sculptor)
        {
            if (!DiaryGameComponent.GamePlaying || comp == null || TaleField == null)
            {
                return;
            }

            DiaryTuningDef tuning = DiaryTuning.Current;
            if (tuning == null || !tuning.artImmortalizationEnabled)
            {
                return;
            }

            Thing artwork = comp.parent;
            TaleReference taleRef = comp.TaleRef;
            if (artwork == null || taleRef == null || taleRef.IsNullTale)
            {
                return;
            }

            Tale tale = TaleField.GetValue(taleRef) as Tale;
            string taleDefName = tale?.def?.defName;
            string taleIdentity = ArtImmortalizationPolicy.TaleIdentity(taleDefName, tale?.id ?? -1);
            if (!ArtImmortalizationPolicy.IsVerifiedTaleIdentity(taleIdentity))
            {
                // Taleless art, or a deed whose identity cannot be pinned down. Fail closed.
                return;
            }

            // Sample before the ownership scan: the roll is a few dozen arithmetic ops and rejects
            // most artworks, while the scan walks the hot store and the archive.
            string artworkId = artwork.GetUniqueLoadID();
            if (!ArtImmortalizationPolicy.ShouldWrite(
                ArtImmortalizationPolicy.DeterministicRoll(artworkId, taleIdentity),
                tuning.artImmortalizationChance))
            {
                return;
            }

            DiaryGameComponent component = DiaryGameComponent.Instance;
            if (component == null || component.HasArtImmortalizationFor(taleIdentity))
            {
                return;
            }

            DiaryInteractionGroupDef group = InteractionGroups.ClassifyDefName(
                GroupDomain.Interaction, ArtImmortalizedEventData.ArtImmortalizedDefName);
            if (group == null)
            {
                return;
            }

            Pawn subject = tale.DominantPawn;
            ArtWriterCandidate chosen = ArtImmortalizationPolicy.SelectWriter(
                BuildCandidates(component, tale, subject, sculptor));
            if (chosen == null)
            {
                return;
            }

            Pawn writer = ResolveCandidate(chosen.pawnId, subject, sculptor);
            if (writer == null)
            {
                return;
            }

            DiaryEvents.Submit(new ArtImmortalizedSignal(
                writer,
                artwork.def?.defName ?? string.Empty,
                artworkId,
                DiaryLineCleaner.CleanLine(comp.Title),
                sculptor == null ? string.Empty : DiaryLineCleaner.CleanLine(sculptor.LabelShortCap),
                taleDefName,
                taleIdentity,
                subject == null ? string.Empty : DiaryLineCleaner.CleanLine(subject.LabelShortCap),
                group,
                Find.TickManager?.TicksGame ?? 0));
        }

        /// <summary>
        /// Collects every pawn that could write this page: the deed's own subject, the colonists the
        /// deed concerns, and the artist. Duplicates are merged so one pawn contributes a single row
        /// carrying all of their flags at once.
        /// </summary>
        private static List<ArtWriterCandidate> BuildCandidates(
            DiaryGameComponent component, Tale tale, Pawn subject, Pawn sculptor)
        {
            Dictionary<string, ArtWriterCandidate> byId = new Dictionary<string, ArtWriterCandidate>();
            AddCandidate(byId, component, subject, dominant: true, concerned: true, sculptor: false);

            // Ask the tale which live colonists it concerns, rather than trying to read its own
            // participant fields: Tale.Concerns is public and every Tale subclass implements it.
            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    List<Pawn> colonists = maps[i]?.mapPawns?.FreeColonists;
                    if (colonists == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < colonists.Count; j++)
                    {
                        Pawn pawn = colonists[j];
                        if (pawn != null && tale.Concerns(pawn))
                        {
                            AddCandidate(byId, component, pawn, dominant: false, concerned: true, sculptor: false);
                        }
                    }
                }
            }

            AddCandidate(byId, component, sculptor, dominant: false, concerned: false, sculptor: true);
            return new List<ArtWriterCandidate>(byId.Values);
        }

        private static void AddCandidate(
            Dictionary<string, ArtWriterCandidate> byId,
            DiaryGameComponent component,
            Pawn pawn,
            bool dominant,
            bool concerned,
            bool sculptor)
        {
            if (pawn == null)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (string.IsNullOrEmpty(pawnId))
            {
                return;
            }

            ArtWriterCandidate candidate;
            if (!byId.TryGetValue(pawnId, out candidate))
            {
                candidate = new ArtWriterCandidate
                {
                    pawnId = pawnId,
                    loadId = pawn.thingIDNumber,
                    eligible = DiaryGameComponent.IsDiaryEligible(pawn),
                    hasColonyDiary = component.HasColonyDiaryFor(pawnId)
                };
                byId[pawnId] = candidate;
            }

            // Flags accumulate: the artist may also be the pawn the deed is about.
            candidate.dominant |= dominant;
            candidate.concerned |= concerned;
            candidate.sculptor |= sculptor;
        }

        /// <summary>
        /// Maps the chosen candidate ID back to its live pawn. Only three pawns can ever be chosen —
        /// the deed's subject, a concerned colonist, or the artist — so this avoids a second colony scan.
        /// </summary>
        private static Pawn ResolveCandidate(string pawnId, Pawn subject, Pawn sculptor)
        {
            if (subject != null && string.Equals(subject.GetUniqueLoadID(), pawnId, System.StringComparison.Ordinal))
            {
                return subject;
            }

            if (sculptor != null && string.Equals(sculptor.GetUniqueLoadID(), pawnId, System.StringComparison.Ordinal))
            {
                return sculptor;
            }

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    List<Pawn> colonists = maps[i]?.mapPawns?.FreeColonists;
                    if (colonists == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < colonists.Count; j++)
                    {
                        Pawn pawn = colonists[j];
                        if (pawn != null
                            && string.Equals(pawn.GetUniqueLoadID(), pawnId, System.StringComparison.Ordinal))
                        {
                            return pawn;
                        }
                    }
                }
            }

            return null;
        }
    }
}
