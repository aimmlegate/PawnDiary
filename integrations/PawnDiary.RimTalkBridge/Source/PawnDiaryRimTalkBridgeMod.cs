// RimWorld mod entry point for the separate "PawnDiary: RimTalk bridge" adapter mod.
// It owns the bridge's saved settings (the integration-level dropdown plus advanced tunables),
// draws the settings window, and installs the Harmony listener that watches RimTalk chat.
//
// The bridge is organized in "integration levels" (see design/RIMTALK_BRIDGE_PLAN.md):
//   0 = Off             — nothing flows in either direction.
//   1 = Shared context  — diary memories are injected into RimTalk prompts and the RimTalk
//                         persona is injected into Pawn Diary pawn summaries (default).
//   2 = + Conversations — additionally, important RimTalk conversations become diary entries.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Adapter mod entry point. RimWorld creates this once when the mod list is loaded, before any
    /// game exists, which makes the constructor the right place for process-global registrations.
    /// </summary>
    public class PawnDiaryRimTalkBridgeMod : Mod
    {
        internal const string HarmonyId = "aimmlegate.pawndiary.rimtalkbridge";
        internal const string LogPrefix = "[PawnDiary: RimTalk bridge]";

        /// <summary>Saved bridge settings. Null only before the mod constructor has run.</summary>
        internal static PawnDiaryRimTalkBridgeSettings Settings;

        /// <summary>
        /// True when RimTalk (cj.rimtalk) is in the active mod list. Cached once at startup: the mod
        /// list cannot change while the game is running, and ModsConfig.IsActive walks a list on
        /// every call. Every code path that touches RimTalk types must check this first (see the
        /// "RimTalk-type isolation" rule in design/RIMTALK_BRIDGE_PLAN.md Step 0).
        /// </summary>
        internal static bool RimTalkActive;

        // Text buffers for the numeric settings fields. RimWorld's numeric text fields need a string
        // buffer that survives between frames while the player is typing (half-typed numbers are not
        // valid ints yet). They are UI state only and never saved.
        private string contextEntryCountBuffer;
        private string colonyEventCountBuffer;
        private string sharedMemoryCountBuffer;
        private string conversationQuietTicksBuffer;

        // Scroll state for the settings window. The bridge now has enough rows (level chooser + the
        // advanced toggles and numeric tunables) to overflow the fixed mod-settings rect, especially at
        // larger UI scales or with longer translations, which would clip the bottom controls. The view
        // height self-corrects to the measured content height after the first frame.
        private Vector2 settingsScrollPosition;
        private float settingsViewHeight = 720f;
        private string minRepliesBuffer;
        private string perPawnDailyCapBuffer;
        private string colonyDailyCapBuffer;
        private string pairMinGapTicksBuffer;
        private string transcriptLineCapBuffer;

        public PawnDiaryRimTalkBridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnDiaryRimTalkBridgeSettings>();
            RimTalkActive = ModsConfig.IsActive("cj.rimtalk");

            if (!RimTalkActive)
            {
                // About.xml declares the RimTalk dependency, but RimWorld does not hard-enforce it.
                // Warn once and idle instead of erroring: every bridge feature checks RimTalkActive.
                Log.WarningOnce(
                    LogPrefix + " RimTalk (cj.rimtalk) is not in the active mod list; the bridge stays idle.",
                    "PawnDiaryRimTalkBridge.RimTalkMissing".GetHashCode());
                return;
            }

            // PatchAll reflects over this assembly's [HarmonyPatch] classes and resolves each target.
            // Our listener's TargetMethod returns null when RimTalk has renamed the method it hooks,
            // and a null target makes PatchAll throw — which would take down the whole mod ctor and,
            // with it, the settings this mod also owns. Isolate it so a changed RimTalk degrades to
            // "conversation capture disabled" (TargetMethod already warns) instead of a hard error.
            try
            {
                new Harmony(HarmonyId).PatchAll();
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to install Harmony patches; RimTalk conversation capture is disabled: " + e);
            }

            // Register the bridge's process-global hooks. The mod constructor runs once per process,
            // so it owns registration; the per-game GameComponent owns periodic work and cache resets.
            // Each registration is isolated like PatchAll above: a future PawnDiary or RimTalk rename
            // must degrade to "that one hook disabled" rather than taking down the whole mod ctor
            // (and with it the settings UI this mod also owns).
            try
            {
                DiaryContextInjector.RegisterAll();     // RimTalk prompt section/variable + diary status listener
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the diary-context hooks; Level 1 outbound is disabled: " + e);
            }

            try
            {
                PersonaSync.RegisterContextProvider();  // Tier A "chat_persona=" line into Pawn Diary summaries
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the persona context provider; Tier A is disabled: " + e);
            }

            try
            {
                ColonyContextInjector.RegisterAll();     // Feature 1: {{colony_events}} env variable + section
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the colony-context hooks; {{colony_events}} is disabled: " + e);
            }

            try
            {
                SharedMemoryInjector.RegisterAll();       // Feature 3: {{diary_shared}} context variable + listener
            }
            catch (Exception e)
            {
                Log.Error(LogPrefix + " failed to register the shared-memory hooks; {{diary_shared}} is disabled: " + e);
            }

            Log.Message(LogPrefix + " initialized.");
        }

        /// <summary>
        /// True when the saved integration level is at least <paramref name="level"/>. Null-safe so
        /// callbacks that can fire before the mod constructor (or from other mods) never throw.
        /// </summary>
        /// <param name="level">Level to test: 1 = Shared context, 2 = Conversations.</param>
        internal static bool LevelAtLeast(int level)
        {
            PawnDiaryRimTalkBridgeSettings settings = Settings;
            return settings != null && settings.integrationLevel >= level;
        }

        /// <summary>Returns the settings-list label shown by RimWorld's mod options menu.</summary>
        public override string SettingsCategory()
        {
            return "PawnDiaryRimTalkBridge.Settings.Category".Translate();
        }

        /// <summary>
        /// Saves the settings, then reconciles the optional {{diary_shared}} prompt entry so a change
        /// made from the main menu (no game loaded, so no tick pass runs) takes effect immediately —
        /// including removing the entry when the player turns the feature off. SyncAutoInject is a
        /// cheap no-op when nothing changed.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();

            if (RimTalkActive)
            {
                SharedMemoryInjector.SyncAutoInject();
            }
        }

        /// <summary>Draws the bridge settings: the level chooser, then the advanced block. Wrapped in a
        /// scroll view so the full list stays reachable when it is taller than the settings window.</summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 24px leaves room for the scrollbar so the rightmost content is not hidden under it.
            Rect viewRect = new Rect(0f, 0f, inRect.width - 24f, settingsViewHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect, true);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            if (!RimTalkActive)
            {
                // Everything below still saves fine; the note explains why nothing happens in-game.
                GUI.color = Color.yellow;
                listing.Label("PawnDiaryRimTalkBridge.Settings.RimTalkMissing".Translate());
                GUI.color = Color.white;
                listing.Gap();
            }

            // Integration level: three mutually exclusive radio rows (cumulative levels).
            listing.Label("PawnDiaryRimTalkBridge.Settings.LevelSection".Translate());
            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelOff".Translate(),
                Settings.integrationLevel == 0,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelOffDesc".Translate()))
            {
                Settings.integrationLevel = 0;
            }

            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelSharedContext".Translate(),
                Settings.integrationLevel == 1,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelSharedContextDesc".Translate()))
            {
                Settings.integrationLevel = 1;
            }

            if (listing.RadioButton(
                "PawnDiaryRimTalkBridge.Settings.LevelConversations".Translate(),
                Settings.integrationLevel == 2,
                8f,
                "PawnDiaryRimTalkBridge.Settings.LevelConversationsDesc".Translate()))
            {
                Settings.integrationLevel = 2;
            }

            listing.GapLine();
            listing.Label("PawnDiaryRimTalkBridge.Settings.AdvancedSection".Translate());

            bool includeVoice = Settings.includeDiaryVoiceLine;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.IncludeDiaryVoiceLine".Translate(),
                ref includeVoice,
                "PawnDiaryRimTalkBridge.Settings.IncludeDiaryVoiceLineDesc".Translate());
            Settings.includeDiaryVoiceLine = includeVoice;

            // Feature 3: pair shared memory ({{diary_shared}}). On by default. The auto-inject and count
            // sub-rows only appear while the feature is on (gated, per the plan).
            bool sharedMemory = Settings.injectSharedMemory;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.InjectSharedMemory".Translate(),
                ref sharedMemory,
                "PawnDiaryRimTalkBridge.Settings.InjectSharedMemoryDesc".Translate());
            Settings.injectSharedMemory = sharedMemory;

            if (Settings.injectSharedMemory)
            {
                bool autoInject = Settings.autoInjectSharedMemory;
                listing.CheckboxLabeled(
                    "PawnDiaryRimTalkBridge.Settings.AutoInjectSharedMemory".Translate(),
                    ref autoInject,
                    "PawnDiaryRimTalkBridge.Settings.AutoInjectSharedMemoryDesc".Translate());
                Settings.autoInjectSharedMemory = autoInject;
            }

            // Feature 1: colony situation ({{colony_events}}). Off by default (overlaps RimTalk event mods).
            bool colonyContext = Settings.injectColonyContext;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.InjectColonyContext".Translate(),
                ref colonyContext,
                "PawnDiaryRimTalkBridge.Settings.InjectColonyContextDesc".Translate());
            Settings.injectColonyContext = colonyContext;

            bool personaLed = Settings.personaLedDiaryVoice;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.PersonaLedDiaryVoice".Translate(),
                ref personaLed,
                "PawnDiaryRimTalkBridge.Settings.PersonaLedDiaryVoiceDesc".Translate());
            Settings.personaLedDiaryVoice = personaLed;

            bool useEngine = Settings.useRimTalkEngine;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.UseRimTalkEngine".Translate(),
                ref useEngine,
                "PawnDiaryRimTalkBridge.Settings.UseRimTalkEngineDesc".Translate());
            Settings.useRimTalkEngine = useEngine;

            bool devLogging = Settings.devChatLogging;
            listing.CheckboxLabeled(
                "PawnDiaryRimTalkBridge.Settings.DevChatLogging".Translate(),
                ref devLogging,
                "PawnDiaryRimTalkBridge.Settings.DevChatLoggingDesc".Translate());
            Settings.devChatLogging = devLogging;

            listing.Gap();

            // Numeric tunables. Provisional defaults (plan item U2) — meant for playtest tuning.
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ContextEntryCount".Translate(),
                ref Settings.contextEntryCount, ref contextEntryCountBuffer, 0f, 10f);
            if (Settings.injectSharedMemory)
            {
                listing.TextFieldNumericLabeled(
                    "PawnDiaryRimTalkBridge.Settings.SharedMemoryCount".Translate(),
                    ref Settings.sharedMemoryCount, ref sharedMemoryCountBuffer, 0f, 4f);
            }
            if (Settings.injectColonyContext)
            {
                listing.TextFieldNumericLabeled(
                    "PawnDiaryRimTalkBridge.Settings.ColonyEventCount".Translate(),
                    ref Settings.colonyEventCount, ref colonyEventCountBuffer, 0f, 6f);
            }
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ConversationQuietTicks".Translate(),
                ref Settings.conversationQuietTicks, ref conversationQuietTicksBuffer, 250f, 60000f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.MinRepliesForImportant".Translate(),
                ref Settings.minRepliesForImportant, ref minRepliesBuffer, 1f, 64f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.PerPawnDailyCap".Translate(),
                ref Settings.perPawnDailyCap, ref perPawnDailyCapBuffer, 0f, 99f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.ColonyDailyCap".Translate(),
                ref Settings.colonyDailyCap, ref colonyDailyCapBuffer, 0f, 999f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.PairMinGapTicks".Translate(),
                ref Settings.pairMinGapTicks, ref pairMinGapTicksBuffer, 0f, 600000f);
            listing.TextFieldNumericLabeled(
                "PawnDiaryRimTalkBridge.Settings.TranscriptLineCap".Translate(),
                ref Settings.transcriptLineCap, ref transcriptLineCapBuffer, 0f, 16f);

            // Remember the measured height so next frame's scroll view is sized to the real content
            // (rows are gated, so the total varies with which features are on). +12 keeps a little slack.
            settingsViewHeight = listing.CurHeight + 12f;
            listing.End();
            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// Saved settings for the bridge adapter. Scribe keys are FROZEN once shipped: they live in the
    /// player's mod-settings XML, so renaming one silently resets that setting for every player.
    /// </summary>
    public class PawnDiaryRimTalkBridgeSettings : ModSettings
    {
        /// <summary>0 = Off, 1 = Shared context, 2 = + Conversations. Int (not enum) for save stability.</summary>
        public int integrationLevel = 1;

        /// <summary>Developer chat logging. Migrated: reuses the pre-rework "enabled" Scribe key,
        /// which used to be the old diagnostic bridge's single log-chats checkbox.</summary>
        public bool devChatLogging;

        /// <summary>Include the pawn's diary writing-voice line in the RimTalk prompt section.</summary>
        public bool includeDiaryVoiceLine = true;

        /// <summary>Tier B (experimental, off): derive the diary voice from the RimTalk persona.</summary>
        public bool personaLedDiaryVoice;

        /// <summary>Engine mode (off): let RimTalk's configured LLM write bridge conversation entries.</summary>
        public bool useRimTalkEngine;

        /// <summary>Feature 1 (off by default): inject a curated colony-situation line as {{colony_events}}.
        /// Off because it overlaps RimTalk's own live-event mods; opt in for the curated/atmospheric angle.</summary>
        public bool injectColonyContext;

        /// <summary>Feature 3 (on): inject the memories two talking pawns share as {{diary_shared}}.</summary>
        public bool injectSharedMemory = true;

        /// <summary>Feature 3 (on): also auto-register a RimTalk prompt entry embedding {{diary_shared}}
        /// so it works with no template editing. Cleaned up when the feature is turned off.</summary>
        public bool autoInjectSharedMemory = true;

        /// <summary>How many recent diary entries feed the RimTalk prompt section.</summary>
        public int contextEntryCount = 3;

        /// <summary>Feature 1: max colony-situation lines injected per prompt (0 disables the block).</summary>
        public int colonyEventCount = 3;

        /// <summary>Feature 3: max shared memories injected per talking pair (0 disables the block).</summary>
        public int sharedMemoryCount = 3;

        /// <summary>Ticks of silence after which a RimTalk conversation counts as finished.</summary>
        public int conversationQuietTicks = 2500;

        /// <summary>Minimum chat lines for a conversation to count as important by length alone.</summary>
        public int minRepliesForImportant = 4;

        /// <summary>Max explicit conversation entries per pawn per in-game day.</summary>
        public int perPawnDailyCap = 2;

        /// <summary>Max explicit conversation entries colony-wide per in-game day.</summary>
        public int colonyDailyCap = 6;

        /// <summary>Minimum ticks between explicit entries for the same pawn pair.</summary>
        public int pairMinGapTicks = 30000;

        /// <summary>Max transcript lines quoted in one conversation entry's prompt context.</summary>
        public int transcriptLineCap = 4;

        public override void ExposeData()
        {
            // "enabled" is the pre-rework key: old installs had a single log-chats checkbox, and the
            // closest surviving meaning is developer chat logging. Never reuse "enabled" for anything else.
            Scribe_Values.Look(ref devChatLogging, "enabled", false);
            Scribe_Values.Look(ref integrationLevel, "integrationLevel", 1);
            Scribe_Values.Look(ref includeDiaryVoiceLine, "includeDiaryVoiceLine", true);
            Scribe_Values.Look(ref personaLedDiaryVoice, "personaLedDiaryVoice", false);
            Scribe_Values.Look(ref useRimTalkEngine, "useRimTalkEngine", false);
            Scribe_Values.Look(ref injectColonyContext, "injectColonyContext", false);
            Scribe_Values.Look(ref injectSharedMemory, "injectSharedMemory", true);
            Scribe_Values.Look(ref autoInjectSharedMemory, "autoInjectSharedMemory", true);
            Scribe_Values.Look(ref contextEntryCount, "contextEntryCount", 3);
            Scribe_Values.Look(ref colonyEventCount, "colonyEventCount", 3);
            Scribe_Values.Look(ref sharedMemoryCount, "sharedMemoryCount", 3);
            Scribe_Values.Look(ref conversationQuietTicks, "conversationQuietTicks", 2500);
            Scribe_Values.Look(ref minRepliesForImportant, "minRepliesForImportant", 4);
            Scribe_Values.Look(ref perPawnDailyCap, "perPawnDailyCap", 2);
            Scribe_Values.Look(ref colonyDailyCap, "colonyDailyCap", 6);
            Scribe_Values.Look(ref pairMinGapTicks, "pairMinGapTicks", 30000);
            Scribe_Values.Look(ref transcriptLineCap, "transcriptLineCap", 4);

            // Defensive clamps: hand-edited or corrupted config XML must not wedge the bridge into
            // impossible states (negative caps, level 99, a zero quiet window that flushes every tick).
            integrationLevel = Clamp(integrationLevel, 0, 2);
            contextEntryCount = Clamp(contextEntryCount, 0, 10);
            colonyEventCount = Clamp(colonyEventCount, 0, 6);
            sharedMemoryCount = Clamp(sharedMemoryCount, 0, 4);
            conversationQuietTicks = Clamp(conversationQuietTicks, 250, 60000);
            minRepliesForImportant = Clamp(minRepliesForImportant, 1, 64);
            perPawnDailyCap = Clamp(perPawnDailyCap, 0, 99);
            colonyDailyCap = Clamp(colonyDailyCap, 0, 999);
            pairMinGapTicks = Clamp(pairMinGapTicks, 0, 600000);
            transcriptLineCap = Clamp(transcriptLineCap, 0, 16);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
