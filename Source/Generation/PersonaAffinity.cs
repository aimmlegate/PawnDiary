// Maps a pawn's traits and backstory to the coarse "theme" keywords carried by writing styles, so the
// initial style roll can lean toward prose that fits who the pawn is. This is the ONE place
// that knows which RimWorld trait/backstory keywords suggest which writing theme.
//
// How it fits together:
//   - Writing styles declare <themes> in 1.6/Defs/DiaryPersonaDefs.xml (e.g. "grim", "warm", "void").
//   - The two maps below translate a pawn's trait defNames and backstory spawnCategories into the
//     same theme vocabulary.
//   - ThemeBonusFor counts how many of the pawn's themes a given style shares, and the style
//     selector (DiaryPersonas.WeightedStartingPersona) turns that count into extra roll weight.
//   - Creepjoiners get an extra boost toward void-tagged styles so their starting prose leans
//     uncanny without making those styles common for ordinary colonists.
//
// Everything is null-safe in the same spirit as DlcContext: traits/backstory can be absent for
// some pawn kinds, and any trait/category we don't recognize simply contributes nothing (it never
// errors). The maps are intentionally coarse and easy to retune; the theme vocabulary is fixed and
// shared with the XML tags. New to C#/RimWorld? See AGENTS.md.
//
// Trait keys: RimWorld has two kinds of trait. "Singular" traits (Bloodlust, Kind, ...) have one
// degree, so we key them by bare defName. "Spectrum" traits hold several degrees under one defName
// (e.g. NaturalMood is sanguine/optimist/pessimist/depressive at degrees 2/1/-1/-2), where the
// degree flips the meaning — so those are keyed "defName:degree". For each of a pawn's traits we
// try both key forms; whichever the map has wins.
//
// NOTE: trait/backstory keywords here stay in English on purpose — they are RimWorld *defNames*,
// *degree numbers*, and *spawnCategories* (stable internal ids), not player-facing text, so they
// are not localized.
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static class PersonaAffinity
    {
        // Extra roll weight added per pawn-theme a style matches. Layered on top of the flat base
        // weight in DiaryPersonas, so a style that hits two of the pawn's themes is strongly
        // favored without ever making the others impossible. Tunable.
        private const float ThemeBonus = 3f;

        // Extra roll weight for Anomaly creepjoiners when a style is tagged with the internal
        // "void" theme. This is intentionally separate from grim/anxious so normal colonists do not
        // over-select the darker voices just because they share a common trait theme.
        private const float CreepjoinerVoidBonus = 12f;

        // Trait -> writing themes. Singular traits are keyed by bare defName; spectrum-trait degrees
        // are keyed "defName:degree" (see file header). Unlisted traits/degrees contribute nothing.
        private static readonly Dictionary<string, string[]> TraitThemes = new Dictionary<string, string[]>
        {
            // Singular traits (one degree)
            { "Bloodlust", new[] { "grim" } },
            { "Psychopath", new[] { "grim", "analytical" } },
            { "Cannibal", new[] { "grim" } },
            { "Tough", new[] { "grim" } },
            { "Brawler", new[] { "grim" } },
            { "Masochist", new[] { "grim" } },
            { "Pyromaniac", new[] { "whimsical", "grim" } },
            { "Kind", new[] { "warm" } },
            { "Abrasive", new[] { "hostile" } },
            { "Greedy", new[] { "hostile", "analytical" } },
            { "Jealous", new[] { "hostile" } },
            { "TooSmart", new[] { "analytical" } },
            { "Transhumanist", new[] { "analytical" } },
            { "Ascetic", new[] { "noble" } },
            { "BodyPurist", new[] { "noble" } },
            { "TorturedArtist", new[] { "dramatic" } },
            { "Nudist", new[] { "dramatic" } },
            { "CreepyBreathing", new[] { "whimsical" } },
            { "Wimp", new[] { "anxious" } },

            // Spectrum traits: "defName:degree"
            // NaturalMood: 2 sanguine, 1 optimist, -1 pessimist, -2 depressive
            { "NaturalMood:2", new[] { "warm", "social" } },
            { "NaturalMood:1", new[] { "warm" } },
            { "NaturalMood:-1", new[] { "grim", "hostile" } },
            { "NaturalMood:-2", new[] { "grim", "dramatic" } },
            // Nerves: 2 iron-willed, 1 steadfast, -1 nervous, -2 volatile
            { "Nerves:2", new[] { "grim" } },
            { "Nerves:1", new[] { "grim" } },
            { "Nerves:-1", new[] { "anxious" } },
            { "Nerves:-2", new[] { "hostile", "dramatic" } },
            // Neurotic: 1 neurotic, 2 very neurotic
            { "Neurotic:1", new[] { "anxious" } },
            { "Neurotic:2", new[] { "anxious" } },
            // Beauty: 2 beautiful, 1 pretty
            { "Beauty:2", new[] { "social" } },
            { "Beauty:1", new[] { "social" } },
            // ShootingAccuracy: -1 trigger-happy
            { "ShootingAccuracy:-1", new[] { "grim", "hostile" } },
        };

        // Backstory spawnCategory -> writing themes. A pawn's childhood and adulthood backstories
        // each carry a list of spawnCategories (e.g. "Tribal", "Pirate", "Civil"); these are coarse
        // origin buckets. Unlisted categories contribute nothing.
        private static readonly Dictionary<string, string[]> BackstoryThemes = new Dictionary<string, string[]>
        {
            { "Raider", new[] { "grim", "hostile" } },
            { "Pirate", new[] { "grim", "hostile" } },
            { "Tribal", new[] { "whimsical" } },
            { "Slave", new[] { "hostile", "grim" } },
            { "Scholar", new[] { "analytical" } },
            { "Genius", new[] { "analytical" } },
            { "Artist", new[] { "dramatic" } },
            { "Spacer", new[] { "analytical" } },
            { "Offworld", new[] { "analytical" } },
            { "Civil", new[] { "warm" } },
            { "Medieval", new[] { "noble" } },
        };

        /// <summary>
        /// Returns the extra roll weight a style earns for matching the pawn's traits/backstory:
        /// <c>ThemeBonus * (number of distinct themes shared between the pawn and the style)</c>.
        /// Void-tagged styles also receive a large bonus for creepjoiners. Returns 0 for a null
        /// pawn/style, an untagged style, or a pawn with no matching themes or creepjoiner bias.
        /// </summary>
        public static float ThemeBonusFor(DiaryPersonaDef persona, Pawn pawn)
        {
            if (persona?.themes == null || persona.themes.Count == 0 || pawn == null)
            {
                return 0f;
            }

            HashSet<string> pawnThemes = ThemesForPawn(pawn);
            int matches = 0;
            if (pawnThemes.Count > 0)
            {
                for (int i = 0; i < persona.themes.Count; i++)
                {
                    if (pawnThemes.Contains(persona.themes[i]))
                    {
                        matches++;
                    }
                }
            }

            float bonus = ThemeBonus * matches;
            if (DlcContext.IsCreepJoiner(pawn) && HasTheme(persona, "void"))
            {
                bonus += CreepjoinerVoidBonus;
            }

            return bonus;
        }

        // Collects the distinct themes implied by a pawn's traits and backstory categories.
        private static HashSet<string> ThemesForPawn(Pawn pawn)
        {
            HashSet<string> themes = new HashSet<string>();

            // Traits. story/traits are null for some pawn kinds (e.g. mechanoids), so guard.
            List<Trait> traits = pawn.story?.traits?.allTraits;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    string defName = trait?.def?.defName;
                    if (defName == null)
                    {
                        continue;
                    }

                    // Try the spectrum form first ("defName:degree"), then the singular bare defName.
                    AddThemes(themes, TraitThemes, defName + ":" + trait.Degree);
                    AddThemes(themes, TraitThemes, defName);
                }
            }

            // Backstory categories from childhood and adulthood.
            AddBackstoryThemes(themes, pawn.story?.Childhood);
            AddBackstoryThemes(themes, pawn.story?.Adulthood);

            return themes;
        }

        private static void AddBackstoryThemes(HashSet<string> themes, BackstoryDef backstory)
        {
            List<string> categories = backstory?.spawnCategories;
            if (categories == null)
            {
                return;
            }

            for (int i = 0; i < categories.Count; i++)
            {
                AddThemes(themes, BackstoryThemes, categories[i]);
            }
        }

        // Looks up a key in a theme map and adds every mapped theme to the set (no-op if unmapped).
        private static void AddThemes(HashSet<string> themes, Dictionary<string, string[]> map, string key)
        {
            if (key == null || !map.TryGetValue(key, out string[] mapped) || mapped == null)
            {
                return;
            }

            for (int i = 0; i < mapped.Length; i++)
            {
                themes.Add(mapped[i]);
            }
        }

        private static bool HasTheme(DiaryPersonaDef persona, string theme)
        {
            if (persona?.themes == null || theme == null)
            {
                return false;
            }

            for (int i = 0; i < persona.themes.Count; i++)
            {
                if (persona.themes[i] == theme)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
