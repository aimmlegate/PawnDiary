// Focused regression fixtures for ordering, mutable-Def caches, and archive-derived reflection guards.
// These tests never launch or drive RimWorld; RimTest invokes them inside its existing harness.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PawnDiary.Capture;
using RimWorld;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>Exercises persistence and live-edit bugs that require the real mod assembly.</summary>
    [TestSuite]
    public static class PawnDiaryRegressionFixtureTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Delayed historical events remain in stable chronological order.</summary>
        [Test]
        public static void HistoricalRegistrationPreservesOrderedReverseScans()
        {
            DiaryEventRepository repository = new DiaryEventRepository();
            repository.Register(Event("late", 300));
            repository.Register(Event("historical", 100));
            repository.Register(Event("middle-a", 200));
            repository.Register(Event("middle-b", 200));

            string[] order = repository.AllEvents.Select(row => row.eventId).ToArray();
            Require(
                order.SequenceEqual(new[] { "historical", "middle-a", "middle-b", "late" }),
                "Historical registration was not inserted in stable tick order: "
                    + string.Join(", ", order));
        }

        /// <summary>Legacy out-of-order save rows are normalized when the transient index rebuilds.</summary>
        [Test]
        public static void RebuildIndexNormalizesLegacyHistoricalAppendOrder()
        {
            DiaryEventRepository repository = new DiaryEventRepository();
            FieldInfo listField = typeof(DiaryEventRepository).GetField("diaryEvents", PrivateInstance);
            Require(listField != null, "DiaryEventRepository.diaryEvents field was not found.");
            listField.SetValue(repository, new List<DiaryEvent>
            {
                Event("old-first", 100),
                Event("newer", 300),
                Event("delayed-old", 200),
            });

            repository.RebuildIndex();

            Require(
                repository.AllEvents.Select(row => row.eventId)
                    .SequenceEqual(new[] { "old-first", "delayed-old", "newer" }),
                "RebuildIndex did not repair a legacy out-of-order event list.");
            Require(repository.FindEvent("delayed-old") != null,
                "RebuildIndex repaired order but did not rebuild the id index.");
        }

        /// <summary>Advanced matcher writes invalidate classifications cached on the same Def objects.</summary>
        [Test]
        public static void AdvancedMatcherWriteInvalidatesClassificationCache()
        {
            string uniqueName = "PawnDiaryCacheRegression_" + Guid.NewGuid().ToString("N");
            DiaryInteractionGroupDef before =
                InteractionGroups.ClassifyDefName(GroupDomain.Thought, uniqueName);
            DiaryInteractionGroupDef target = InteractionGroups.All.FirstOrDefault(group =>
                group != null
                && group.domain == GroupDomain.Thought
                && !ReferenceEquals(group, before));
            Require(target != null, "Thought groups did not provide a non-fallback cache test target.");

            AdvancedFieldDescriptor descriptor = AdvancedFieldCatalog.All.FirstOrDefault(field =>
                field != null
                && string.Equals(
                    field.key,
                    target.defName + ".matchDefNames",
                    StringComparison.Ordinal));
            Require(descriptor != null,
                "Advanced catalog did not expose matchDefNames for " + target.defName + ".");

            List<string> original = target.matchDefNames;
            List<string> replacement = original == null
                ? new List<string>()
                : new List<string>(original);
            replacement.Add(uniqueName);
            try
            {
                descriptor.WriteDefValue(replacement);
                Require(ReferenceEquals(
                        InteractionGroups.ClassifyDefName(GroupDomain.Thought, uniqueName),
                        target),
                    "Cached thought classification ignored the live matcher replacement.");
            }
            finally
            {
                descriptor.WriteDefValue(original);
            }
        }

        /// <summary>Blank token rows are inert and no-match domains return only explicit catch-alls.</summary>
        [Test]
        public static void InteractionClassificationRejectsBlankTokensAndImplicitFallbacks()
        {
            DiaryInteractionGroupDef blankToken = new DiaryInteractionGroupDef
            {
                domain = GroupDomain.External,
                matchTokens = new List<string> { string.Empty, "   " },
            };
            Require(!blankToken.Matches("Anything"),
                "An empty matchTokens row became a substring catch-all.");

            FieldInfo catalogField = typeof(InteractionGroups).GetField("cachedAll", PrivateStatic);
            Require(catalogField != null, "InteractionGroups.cachedAll was not found.");
            List<DiaryInteractionGroupDef> original =
                (List<DiaryInteractionGroupDef>)catalogField.GetValue(null);
            // Materialize the real catalog before replacing the cache so it can be restored exactly.
            if (original == null)
            {
                original = InteractionGroups.All;
            }

            DiaryInteractionGroupDef unrelated = new DiaryInteractionGroupDef
            {
                defName = "RegressionUnrelated",
                domain = GroupDomain.External,
                matchDefNames = new List<string> { "ClaimedOnly" },
            };
            DiaryInteractionGroupDef catchAll = new DiaryInteractionGroupDef
            {
                defName = "RegressionCatchAll",
                domain = GroupDomain.External,
                catchAll = true,
            };
            try
            {
                catalogField.SetValue(null, new List<DiaryInteractionGroupDef> { unrelated });
                InteractionGroups.InvalidateClassificationCache();
                Require(InteractionGroups.ClassifyDefName(GroupDomain.External, "Unknown") == null,
                    "A no-match domain returned its final unrelated group.");

                catalogField.SetValue(null,
                    new List<DiaryInteractionGroupDef> { unrelated, catchAll });
                InteractionGroups.InvalidateClassificationCache();
                Require(ReferenceEquals(
                        InteractionGroups.ClassifyDefName(GroupDomain.External, "Unknown"),
                        catchAll),
                    "An explicit catch-all was not returned for a no-match domain.");
            }
            finally
            {
                catalogField.SetValue(null, original);
                InteractionGroups.InvalidateClassificationCache();
            }
        }

        /// <summary>A detached legacy belief state repairs a null source list before baselining.</summary>
        [Test]
        public static void BeliefBaselineRepairsNullSourceHistory()
        {
            PawnBeliefState state = new PawnBeliefState
            {
                lastReflectedSourceIds = null,
            };
            state.BaselineReflection(GenDate.TicksPerDay, BeliefPolicySnapshot.CreateDefault());
            Require(state.lastReflectedSourceIds != null && state.lastReflectedSourceIds.Count == 0,
                "BaselineReflection did not repair and clear a null legacy source-id list.");
        }

        /// <summary>Replacing weatherMentionChances on one tuning Def refreshes the derived lookup.</summary>
        [Test]
        public static void WeatherLookupObservesRuleListReplacement()
        {
            MethodInfo lookupMethod =
                typeof(DiaryContextBuilder).GetMethod("WeatherChanceLookup", PrivateStatic);
            Require(lookupMethod != null, "DiaryContextBuilder.WeatherChanceLookup was not found.");

            DiaryTuningDef tuning = new DiaryTuningDef
            {
                weatherMentionChances = new List<WeatherMentionRule>
                {
                    new WeatherMentionRule { weather = "RegressionWeather", chance = 0.1f },
                },
            };
            Dictionary<string, float> first =
                (Dictionary<string, float>)lookupMethod.Invoke(null, new object[] { tuning });
            Require(Math.Abs(first["RegressionWeather"] - 0.1f) < 0.0001f,
                "Initial weather lookup did not contain the seeded chance.");

            tuning.weatherMentionChances = new List<WeatherMentionRule>
            {
                new WeatherMentionRule { weather = "RegressionWeather", chance = 0.9f },
            };
            Dictionary<string, float> second =
                (Dictionary<string, float>)lookupMethod.Invoke(null, new object[] { tuning });
            Require(Math.Abs(second["RegressionWeather"] - 0.9f) < 0.0001f,
                "Weather lookup remained stale after replacing rules on the same tuning Def.");
        }

        /// <summary>Cosmetic weapon and ordinary thought picks do not advance Unity's global RNG.</summary>
        [Test]
        public static void PawnSummaryCosmeticChoicesPreserveUnityRandomState()
        {
            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            UnityEngine.Random.State originalRandomState = UnityEngine.Random.state;
            try
            {
                Pawn pawn = scope.CreateAdultColonist();
                ThingDef pistolDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Pistol");
                Require(pistolDef != null, "The base-game Gun_Pistol Def was not found.");
                ThingWithComps pistol = ThingMaker.MakeThing(pistolDef) as ThingWithComps;
                Require(pistol != null && pawn.inventory?.innerContainer != null,
                    "The weapon/inventory RNG fixture could not be constructed.");
                Require(pawn.inventory.innerContainer.TryAdd(pistol),
                    "The RNG fixture pistol could not be added to the pawn's inventory.");

                const int weaponSeed = 72341;
                UnityEngine.Random.InitState(weaponSeed);
                float expectedAfterWeapon = UnityEngine.Random.value;
                UnityEngine.Random.InitState(weaponSeed);
                string weaponLabel = DiaryContextBuilder.EquippedWeapon(pawn);
                float actualAfterWeapon = UnityEngine.Random.value;
                Require(!string.IsNullOrWhiteSpace(weaponLabel),
                    "EquippedWeapon did not exercise its inventory fallback.");
                Require(Math.Abs(expectedAfterWeapon - actualAfterWeapon) < 0.000001f,
                    "EquippedWeapon advanced Unity's global random stream.");

                ThoughtDef fineMeal = DefDatabase<ThoughtDef>.GetNamedSilentFail("AteFineMeal");
                Require(fineMeal != null, "The base-game AteFineMeal ThoughtDef was not found.");
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(fineMeal);
                MethodInfo collectFacts = typeof(DiaryContextBuilder).GetMethod(
                    "CollectPawnSummaryFacts",
                    PrivateStatic);
                Require(collectFacts != null,
                    "DiaryContextBuilder.CollectPawnSummaryFacts was not found.");

                const int thoughtSeed = 98413;
                UnityEngine.Random.InitState(thoughtSeed);
                float expectedAfterThought = UnityEngine.Random.value;
                UnityEngine.Random.InitState(thoughtSeed);
                collectFacts.Invoke(null, new object[] { pawn });
                float actualAfterThought = UnityEngine.Random.value;
                Require(Math.Abs(expectedAfterThought - actualAfterThought) < 0.000001f,
                    "CollectPawnSummaryFacts advanced Unity's global random stream.");
            }
            finally
            {
                UnityEngine.Random.state = originalRandomState;
                scope.TearDown();
            }
        }

        /// <summary>An archive-only quadrum page rebuilds the transient once-per-quadrum guard.</summary>
        [Test]
        public static void ArchiveOnlyQuadrumReflectionRebuildsReloadGuard()
        {
            DiaryGameComponent component = DiaryGameComponent.Instance;
            Require(component != null, "A loaded game is required for the quadrum guard fixture.");

            FieldInfo archiveField = typeof(DiaryGameComponent).GetField("archive", PrivateInstance);
            FieldInfo guardField =
                typeof(DiaryGameComponent).GetField("writtenQuadrumReflections", PrivateInstance);
            MethodInfo rebuild = typeof(DiaryGameComponent).GetMethod(
                "RebuildWrittenDayReflectionsFromEvents",
                PrivateInstance);
            Require(archiveField != null && guardField != null && rebuild != null,
                "Quadrum reflection archive/guard seams were not found.");

            DiaryArchiveRepository archive =
                (DiaryArchiveRepository)archiveField.GetValue(component);
            HashSet<string> guard = (HashSet<string>)guardField.GetValue(component);
            string eventId = "pd-archive-quadrum-" + Guid.NewGuid().ToString("N");
            string pawnId = "PawnDiary_ArchiveQuadrumRegression";
            int tick = Find.TickManager.TicksGame;
            int day = Find.TickManager.TicksAbs / GenDate.TicksPerDay;
            string expectedKey = pawnId + "|" + (day / GenDate.DaysPerQuadrum);
            HashSet<string> cleanupIds = new HashSet<string> { eventId };

            try
            {
                Require(archive.AddOrKeep(new ArchivedDiaryEntry
                {
                    eventId = eventId,
                    pawnId = pawnId,
                    povRole = "initiator",
                    tick = tick,
                    interactionDefName = DayReflectionEventData.QuadrumDefNameToken,
                }), "The synthetic archive row was rejected.");

                rebuild.Invoke(component, null);
                Require(guard.Contains(expectedKey),
                    "Archive-only quadrum history did not rebuild the reload guard.");
            }
            finally
            {
                archive.RemoveForEventIds(cleanupIds);
                rebuild.Invoke(component, null);
            }
        }

        private static DiaryEvent Event(string id, int tick)
        {
            return new DiaryEvent { eventId = id, tick = tick };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
