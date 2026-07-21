// Shared texture cache for the Diary tab's action-button icons.
//
// The glyphs are CoreUI Icons (Free set, MIT) rasterized to solid-white PNGs so RimWorld's GUI.color
// tinting drives their on-screen color (see Textures/UI/DiaryButtons/CREDITS.txt). Each icon is loaded
// lazily on first draw with a defensive fallback to the vanilla built-in it replaced, mirroring the
// load-with-fallback pattern in Source/Patches/DiaryInspectCommandPatch.cs. Loading lazily (from the
// UI draw thread, after content is ready) rather than in a static constructor sidesteps any
// static-init ordering question with RimWorld's own TexButton cache.
//
// New to C#/RimWorld? (JS/TS analogy) ContentFinder<Texture2D>.Get("path", reportFailure) is like a
// synchronous asset loader: the path is relative to any active mod's Textures/ folder, and passing
// false for reportFailure returns null instead of logging when the asset is missing — which is why
// each getter falls back to a guaranteed-present vanilla texture.
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnDiary
{
    /// <summary>
    /// Lazily-loaded icon textures for the Diary tab's action buttons. All are solid-white PNGs meant
    /// to be tinted via <see cref="GUI.color"/> at draw time.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class DiaryButtonTextures
    {
        // Textures/ subpath shared by every button icon.
        private const string IconFolder = "UI/DiaryButtons/";

        private static Texture2D filter;
        private static Texture2D favorite;
        private static Texture2D copy;
        private static Texture2D regenerate;
        private static Texture2D writingStyle;
        private static Texture2D seasonSpring;
        private static Texture2D seasonSummer;
        private static Texture2D seasonFall;
        private static Texture2D seasonWinter;

        /// <summary>Funnel glyph for the filter-panel show/hide toggle (cil-filter).</summary>
        public static Texture2D Filter => filter ?? (filter = Load("Filter", TexButton.ToggleLog));

        /// <summary>Star glyph for the per-entry favorite toggle (cil-star).</summary>
        public static Texture2D Favorite => favorite ?? (favorite = Load("Favorite", TexButton.Add));

        /// <summary>Stacked-pages glyph for the "Copy entry" action (cil-copy).</summary>
        public static Texture2D Copy => copy ?? (copy = Load("Copy", TexButton.Copy));

        /// <summary>Circular-arrow glyph for the "Regenerate entry" action (cil-reload).</summary>
        public static Texture2D Regenerate => regenerate ?? (regenerate = Load("Regenerate", TexButton.Reload));

        /// <summary>Portrait-on-a-card glyph for the writing-style / persona editor (cil-contact).</summary>
        public static Texture2D WritingStyle => writingStyle ?? (writingStyle = Load("WritingStyle", TexButton.Rename));

        /// <summary>Flower glyph for a spring quadrum divider (cil-flower).</summary>
        public static Texture2D SeasonSpring => seasonSpring ?? (seasonSpring = LoadOptional("SeasonSpring"));

        /// <summary>Sun glyph for a summer quadrum divider (cil-sun).</summary>
        public static Texture2D SeasonSummer => seasonSummer ?? (seasonSummer = LoadOptional("SeasonSummer"));

        /// <summary>Leaf glyph for an autumn/fall quadrum divider (cil-leaf).</summary>
        public static Texture2D SeasonFall => seasonFall ?? (seasonFall = LoadOptional("SeasonFall"));

        /// <summary>Snowflake glyph for a winter quadrum divider (cil-snowflake).</summary>
        public static Texture2D SeasonWinter => seasonWinter ?? (seasonWinter = LoadOptional("SeasonWinter"));

        /// <summary>
        /// The small season glyph paired with a quadrum/season divider, or null for a season with no
        /// icon (the divider then draws its label alone). Permanent-summer/winter maps fall back to the
        /// matching seasonal glyph.
        /// </summary>
        public static Texture2D SeasonIcon(Season season)
        {
            switch (season)
            {
                case Season.Spring:
                    return SeasonSpring;
                case Season.Summer:
                case Season.PermanentSummer:
                    return SeasonSummer;
                case Season.Fall:
                    return SeasonFall;
                case Season.Winter:
                case Season.PermanentWinter:
                    return SeasonWinter;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Loads one button texture by short name, falling back to a guaranteed-present vanilla texture
        /// if the mod asset is missing (renamed folder, botched deploy) so the tab never draws a
        /// null/pink icon or throws mid-frame.
        /// </summary>
        private static Texture2D Load(string name, Texture2D fallback)
        {
            return ContentFinder<Texture2D>.Get(IconFolder + name, false) ?? fallback;
        }

        /// <summary>
        /// Loads a decorative texture that has no vanilla equivalent, returning null when the asset is
        /// missing so the caller can simply draw nothing instead of a pink placeholder.
        /// </summary>
        private static Texture2D LoadOptional(string name)
        {
            return ContentFinder<Texture2D>.Get(IconFolder + name, false);
        }
    }
}
