// Shared pawn-portrait renderer for diary windows.
//
// RimWorld's portrait cache can fail for unresolved, dead, or partially initialized world pawns.
// Both the Diary Reader and Memory Library therefore use the same guarded renderer and the same
// subdued book placeholder instead of duplicating slightly different failure behavior.
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>Draws one south-facing pawn portrait, or a safe placeholder when unavailable.</summary>
    internal static class DiaryPawnPortrait
    {
        private const string PlaceholderTexturePath = "UI/Commands/PawnDiaryOpen";
        private static Texture2D placeholderTexture;

        /// <summary>Draws one cached pawn portrait without allowing render failures into the UI.</summary>
        public static void Draw(Rect rect, Pawn pawn)
        {
            Texture texture = null;
            if (pawn != null)
            {
                try
                {
                    texture = PortraitsCache.Get(
                        pawn,
                        new Vector2(rect.width, rect.height),
                        Rot4.South);
                }
                catch
                {
                    // Some archived world pawns no longer have renderable graphics. Their frozen
                    // Library label remains useful, so portrait failure must stay presentation-only.
                    texture = null;
                }
            }

            Color oldColor = GUI.color;
            if (texture == null)
            {
                if (placeholderTexture == null)
                {
                    placeholderTexture = ContentFinder<Texture2D>.Get(
                        PlaceholderTexturePath,
                        false) ?? TexButton.IconBook;
                }

                texture = placeholderTexture;
                GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * 0.55f);
            }

            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            GUI.color = oldColor;
        }
    }
}
