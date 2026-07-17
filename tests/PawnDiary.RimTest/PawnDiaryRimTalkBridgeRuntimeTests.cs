// Optional-mod runtime test for the real RimTalk shared-memory provider. The core RimTest assembly
// deliberately has no compile-time RimTalk/bridge reference: reflection keeps this suite loadable when
// either optional package is absent, while an active bridge run still enters its real PromptContext API.
//
// New to C#/RimWorld? See AGENTS.md ("Optional-mod hooks") and docs/lore/build.md.
using System;
using System.Collections.Generic;
using System.Reflection;
using PawnDiary.Integration;
using RimTestRedux;
using Verse;

namespace PawnDiary.RimTests
{
    /// <summary>
    /// Proves that the loaded RimTalk bridge reads completed pair memories without creating a duplicate
    /// or recursively submitting another Pawn Diary entry.
    /// </summary>
    [TestSuite]
    public static class PawnDiaryRimTalkBridgeRuntimeTests
    {
        private const string BridgePackageId = "aimmlegate.pawndiary.rimtalkbridge";
        private const string RimTalkPackageId = "cj.rimtalk";
        private const string TestSourceId = "pawndiary.rimtest.rimtalk-memory";
        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Two completed caller-authored memories enter the bridge's real shared-memory block. Reading
        /// that block is side-effect free: the pair retains exactly the two submitted events.
        /// </summary>
        [Test]
        public static void LoadedBridgeInjectsGrowthAndBirthMemoriesWithoutRecursion()
        {
            if (!ModsConfig.IsActive(BridgePackageId) || !ModsConfig.IsActive(RimTalkPackageId))
            {
                Log.Message("[PawnDiary RimTest RimTalk bridge] not applicable (bridge or RimTalk inactive).");
                return;
            }

            Type modType = ResolveType(
                "PawnDiaryRimTalkBridge.PawnDiaryRimTalkBridgeMod, PawnDiaryRimTalkBridge");
            Type injectorType = ResolveType(
                "PawnDiaryRimTalkBridge.SharedMemoryInjector, PawnDiaryRimTalkBridge");
            Type promptContextType = ResolveType("RimTalk.Prompt.PromptContext, RimTalk");
            FieldInfo settingsField = RequireField(modType, "Settings");
            object bridgeSettings = settingsField.GetValue(null);
            PawnDiaryRimTestScope.Require(bridgeSettings != null,
                "The active RimTalk bridge did not expose its initialized settings object.");

            FieldInfo integrationLevelField = RequireField(bridgeSettings.GetType(), "integrationLevel");
            FieldInfo injectSharedMemoryField = RequireField(bridgeSettings.GetType(), "injectSharedMemory");
            FieldInfo sharedMemoryCountField = RequireField(bridgeSettings.GetType(), "sharedMemoryCount");
            MethodInfo resetMethod = RequireMethod(injectorType, "ResetForNewGame");
            MethodInfo sharedForMethod = RequireMethod(injectorType, "SharedFor");
            MethodInfo processQueueMethod = RequireMethod(injectorType, "ProcessQueue");

            PawnDiaryRimTestScope scope = PawnDiaryRimTestScope.Begin();
            Pawn first = null;
            Pawn second = null;
            PawnDiarySettings coreSettings = PawnDiaryMod.Settings;
            bool originalExternal = coreSettings.allowExternalIntegrations;
            int originalLevel = (int)integrationLevelField.GetValue(bridgeSettings);
            bool originalInject = (bool)injectSharedMemoryField.GetValue(bridgeSettings);
            int originalCount = (int)sharedMemoryCountField.GetValue(bridgeSettings);
            try
            {
                coreSettings.allowExternalIntegrations = true;
                integrationLevelField.SetValue(bridgeSettings, 1);
                injectSharedMemoryField.SetValue(bridgeSettings, true);
                sharedMemoryCountField.SetValue(bridgeSettings, 3);
                resetMethod.Invoke(null, null);

                first = scope.CreateAdultColonist();
                second = scope.CreateAdultColonist();
                scope.SpawnAsLiveColonist(first);
                scope.SpawnAsLiveColonist(second);

                DiaryEventSubmissionResult growth = SubmitMemory(
                    first,
                    second,
                    "pawndiary_rimtest_biotech_growth_memory",
                    "A growth moment remembered",
                    "They remembered choosing new interests together.");
                DiaryEventSubmissionResult birth = SubmitMemory(
                    first,
                    second,
                    "pawndiary_rimtest_biotech_birth_memory",
                    "A family birth remembered",
                    "They remembered welcoming a child into the family.");
                PawnDiaryRimTestScope.Require(growth.recorded && birth.recorded,
                    "The bridge fixture could not create its two completed source memories.");

                int beforeCount = ContextEntryCount(first);
                object promptContext = Activator.CreateInstance(promptContextType);
                SetProperty(promptContextType, promptContext, "CurrentPawn", first);
                SetProperty(promptContextType, promptContext, "AllPawns", new List<Pawn> { first, second });
                SetProperty(promptContextType, promptContext, "IsPreview", false);

                // The provider intentionally queues a main-thread cache fill on the first read.
                sharedForMethod.Invoke(null, new[] { promptContext });
                processQueueMethod.Invoke(null, new object[] { Find.TickManager?.TicksGame ?? 0 });
                string block = sharedForMethod.Invoke(null, new[] { promptContext }) as string;

                PawnDiaryRimTestScope.Require(!string.IsNullOrWhiteSpace(block),
                    "The active RimTalk bridge returned no shared-memory block.");
                PawnDiaryRimTestScope.Require(
                    block.IndexOf("A growth moment remembered", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The shared-memory block omitted the completed growth memory.");
                PawnDiaryRimTestScope.Require(
                    block.IndexOf("A family birth remembered", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The shared-memory block omitted the completed birth memory.");
                PawnDiaryRimTestScope.Require(ContextEntryCount(first) == beforeCount,
                    "Reading RimTalk shared memory recursively created or removed a Pawn Diary entry.");
            }
            finally
            {
                resetMethod.Invoke(null, null);
                integrationLevelField.SetValue(bridgeSettings, originalLevel);
                injectSharedMemoryField.SetValue(bridgeSettings, originalInject);
                sharedMemoryCountField.SetValue(bridgeSettings, originalCount);
                coreSettings.allowExternalIntegrations = originalExternal;
                scope.TearDown();
            }
        }

        private static DiaryEventSubmissionResult SubmitMemory(
            Pawn subject,
            Pawn partner,
            string eventKey,
            string title,
            string text)
        {
            return PawnDiaryApi.SubmitDirectEntry(new ExternalDirectEntryRequest
            {
                sourceId = TestSourceId,
                eventKey = eventKey,
                subject = subject,
                partner = partner,
                text = text,
                partnerText = text,
                title = title,
                partnerTitle = title,
                summaryText = text,
                eventLabel = title,
                forceRecord = true,
                dedupKey = eventKey + "|" + subject.GetUniqueLoadID() + "|" + partner.GetUniqueLoadID()
            });
        }

        private static int ContextEntryCount(Pawn pawn)
        {
            DiaryContextSnapshot snapshot = PawnDiaryApi.GetContextSnapshot(pawn, 32);
            return snapshot?.entries?.Count ?? 0;
        }

        private static Type ResolveType(string qualifiedName)
        {
            Type type = Type.GetType(qualifiedName, false);
            if (type == null)
            {
                throw new AssertionException("Could not resolve optional runtime type '" + qualifiedName + "'.");
            }

            return type;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type?.GetField(name, StaticAny | BindingFlags.Instance);
            if (field == null)
            {
                throw new AssertionException("Could not resolve field '" + name + "' on " + type?.FullName + ".");
            }

            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            MethodInfo method = type?.GetMethod(name, StaticAny);
            if (method == null)
            {
                throw new AssertionException("Could not resolve method '" + name + "' on " + type?.FullName + ".");
            }

            return method;
        }

        private static void SetProperty(Type type, object instance, string name, object value)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite)
            {
                throw new AssertionException("Could not write RimTalk PromptContext." + name + ".");
            }

            property.SetValue(instance, value, null);
        }
    }
}
