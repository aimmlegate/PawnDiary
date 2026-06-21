// Pawn-name highlight collection for the Diary tab.
//
// This is the impure half of name highlighting: it reads live RimWorld pawn state and turns known
// pawn names into plain DiaryNameHighlight facts. The pure string rewrite lives in
// DiaryNameHighlighter so it can be tested without Verse/Unity.
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Partial implementation of the pawn Diary inspector tab.
    /// </summary>
    public partial class ITab_Pawn_Diary
    {
        private static readonly Dictionary<string, PropertyInfo> PawnBoolPropertyCache = new Dictionary<string, PropertyInfo>();
        private Pawn cachedNameHighlightsPawn;
        private int cachedNameHighlightsTick = -1;
        private List<DiaryNameHighlight> cachedNameHighlights = new List<DiaryNameHighlight>();

        private List<DiaryNameHighlight> NameHighlightsFor(Pawn selectedPawn)
        {
            int tick = Find.TickManager == null ? -1 : Find.TickManager.TicksGame;
            if (cachedNameHighlightsPawn == selectedPawn
                && cachedNameHighlightsTick == tick
                && cachedNameHighlights != null)
            {
                return cachedNameHighlights;
            }

            cachedNameHighlightsPawn = selectedPawn;
            cachedNameHighlightsTick = tick;
            cachedNameHighlights = BuildPawnNameHighlights(selectedPawn);
            return cachedNameHighlights;
        }

        /// <summary>
        /// Builds display-only name highlights for live humanlike pawns that could be mentioned in
        /// diary prose. Duplicate names with conflicting colors intentionally fall back to bold-only.
        /// </summary>
        private static List<DiaryNameHighlight> BuildPawnNameHighlights(Pawn selectedPawn)
        {
            Dictionary<string, DiaryNameHighlight> byName = new Dictionary<string, DiaryNameHighlight>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenPawnIds = new HashSet<string>();

            AddPawnNameHighlight(byName, seenPawnIds, selectedPawn);

            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    if (map?.mapPawns?.AllPawns == null)
                    {
                        continue;
                    }

                    List<Pawn> pawns = map.mapPawns.AllPawns;
                    for (int j = 0; j < pawns.Count; j++)
                    {
                        AddPawnNameHighlight(byName, seenPawnIds, pawns[j]);
                    }
                }
            }

            if (Find.WorldPawns?.AllPawnsAlive != null)
            {
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                {
                    AddPawnNameHighlight(byName, seenPawnIds, pawn);
                }
            }

            return new List<DiaryNameHighlight>(byName.Values);
        }

        private static void AddPawnNameHighlight(
            Dictionary<string, DiaryNameHighlight> byName,
            HashSet<string> seenPawnIds,
            Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
            {
                return;
            }

            string pawnId = pawn.GetUniqueLoadID();
            if (!string.IsNullOrEmpty(pawnId) && !seenPawnIds.Add(pawnId))
            {
                return;
            }

            string colorHex = PawnNameHighlightColorHex(pawn);
            AddPawnName(byName, pawn.LabelShort, colorHex);
            AddPawnName(byName, pawn.LabelShortCap, colorHex);
        }

        private static void AddPawnName(
            Dictionary<string, DiaryNameHighlight> byName,
            string name,
            string colorHex)
        {
            name = name == null ? string.Empty : name.Trim();
            if (name.Length < 2)
            {
                return;
            }

            DiaryNameHighlight existing;
            if (byName.TryGetValue(name, out existing))
            {
                if (!string.Equals(existing.colorHex, colorHex ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    existing.colorHex = string.Empty;
                }

                return;
            }

            byName[name] = new DiaryNameHighlight
            {
                name = name,
                colorHex = colorHex ?? string.Empty
            };
        }

        private static string PawnNameHighlightColorHex(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            if (PawnBoolProperty(pawn, "IsSlaveOfColony") || PawnBoolProperty(pawn, "IsSlave"))
            {
                return ColorHex(UiStyle.PawnNameSlaveColor);
            }

            if (PawnBoolProperty(pawn, "IsPrisonerOfColony") || PawnBoolProperty(pawn, "IsPrisoner"))
            {
                return ColorHex(UiStyle.PawnNamePrisonerColor);
            }

            Color favoriteColor;
            if (pawn.IsColonist && TryFavoriteColor(pawn, out favoriteColor))
            {
                return ColorHex(favoriteColor);
            }

            if (pawn.Faction != null && Faction.OfPlayer != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return ColorHex(UiStyle.PawnNameEnemyColor);
            }

            return ColorHex(UiStyle.PawnNameNeutralColor);
        }

        private static bool TryFavoriteColor(Pawn pawn, out Color color)
        {
            if (pawn?.story?.favoriteColor != null)
            {
                color = pawn.story.favoriteColor.color;
                return true;
            }

            color = Color.white;
            return false;
        }

        private static bool PawnBoolProperty(Pawn pawn, string propertyName)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            PropertyInfo property;
            if (!PawnBoolPropertyCache.TryGetValue(propertyName, out property))
            {
                property = typeof(Pawn).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                PawnBoolPropertyCache[propertyName] = property;
            }

            if (property == null || property.PropertyType != typeof(bool))
            {
                return false;
            }

            try
            {
                return (bool)property.GetValue(pawn, null);
            }
            catch
            {
                return false;
            }
        }

        private static string ColorHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(ReadableHighlightColor(color));
        }

        private static Color ReadableHighlightColor(Color color)
        {
            color.a = 1f;
            float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            const float minLuminance = 0.54f;
            if (luminance >= minLuminance)
            {
                return color;
            }

            float lift = minLuminance - luminance;
            color.r = Mathf.Clamp01(color.r + lift);
            color.g = Mathf.Clamp01(color.g + lift);
            color.b = Mathf.Clamp01(color.b + lift);
            return color;
        }
    }
}
