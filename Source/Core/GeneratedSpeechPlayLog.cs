// Low-level helper for generated direct-speech PlayLog rows. Kept out of DiaryGameComponent so the
// Harmony patches can ask the same registry whether a PlayLogEntry_Interaction is one of ours.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnDiary
{
    internal static class GeneratedSpeechPlayLog
    {
        private sealed class SpeechText
        {
            public string text;
        }

        private static readonly ConditionalWeakTable<PlayLogEntry_Interaction, SpeechText> TextByEntry =
            new ConditionalWeakTable<PlayLogEntry_Interaction, SpeechText>();

        private static readonly ConstructorInfo InteractionCtor = AccessTools.Constructor(
            typeof(PlayLogEntry_Interaction),
            new[] { typeof(InteractionDef), typeof(Pawn), typeof(Pawn), typeof(List<RulePackDef>) });

        private static readonly FieldInfo IntDefField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef");
        private static readonly FieldInfo InitiatorField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "initiator");
        private static readonly FieldInfo RecipientField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "recipient");
        private static readonly FieldInfo ExtraSentencePacksField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "extraSentencePacks");
        // The base LogEntry backing field for LogID, used only by the reflection fallback below to
        // stamp a unique id the parameterless constructor may have skipped.
        private static readonly FieldInfo LogIdField = AccessTools.Field(typeof(LogEntry), "logID");

        private static int addDepth;

        /// <summary>
        /// True while a generated speech row is being added to RimWorld's PlayLog. The normal
        /// PlayLog listener uses this to avoid recording our synthetic row as a new diary event.
        /// </summary>
        public static bool IsAddingGeneratedSpeechEntry
        {
            get { return addDepth > 0; }
        }

        /// <summary>
        /// True when the current game has any generated direct-speech rows, so the display patch must
        /// inspect each rendered interaction. False for games that never created one, letting that
        /// patch skip its per-row lookup entirely. The map is populated synchronously right after a
        /// row is added (before any UI render), so this gate never hides one of our own rows.
        /// </summary>
        public static bool HasGeneratedSpeechRows
        {
            get
            {
                DiaryGameComponent component = DiaryGameComponent.Current;
                return component != null && component.HasGeneratedSpeechPlayLogTexts;
            }
        }

        /// <summary>
        /// Creates a normal PlayLogEntry_Interaction, using RimWorld's constructor when available
        /// and falling back to setting the same private fields our listener already reads.
        /// </summary>
        public static PlayLogEntry_Interaction CreateInteractionEntry(InteractionDef interactionDef, Pawn initiator, Pawn recipient)
        {
            if (interactionDef == null || initiator == null || recipient == null)
            {
                return null;
            }

            if (InteractionCtor != null)
            {
                return (PlayLogEntry_Interaction)InteractionCtor.Invoke(new object[]
                {
                    interactionDef,
                    initiator,
                    recipient,
                    null
                });
            }

            PlayLogEntry_Interaction entry = Activator.CreateInstance(typeof(PlayLogEntry_Interaction), true) as PlayLogEntry_Interaction;
            if (entry == null || IntDefField == null || InitiatorField == null || RecipientField == null)
            {
                return null;
            }

            IntDefField.SetValue(entry, interactionDef);
            InitiatorField.SetValue(entry, initiator);
            RecipientField.SetValue(entry, recipient);
            if (ExtraSentencePacksField != null)
            {
                ExtraSentencePacksField.SetValue(entry, null);
            }

            // The parameterless constructor this fallback uses may leave logID unset, and PlayLog.Add
            // never assigns one. Stamp a fresh unique LogID so the synthetic row can't collide with
            // another entry (the primary constructor path above already assigns one).
            if (LogIdField != null && Find.UniqueIDsManager != null)
            {
                LogIdField.SetValue(entry, Find.UniqueIDsManager.GetNextLogID());
            }

            return entry;
        }

        /// <summary>
        /// Adds the row to the native PlayLog and returns its assigned LogID.
        /// </summary>
        public static bool TryAdd(PlayLogEntry_Interaction entry, string speech, out int playLogEntryId)
        {
            playLogEntryId = -1;
            if (entry == null || string.IsNullOrWhiteSpace(speech) || Find.PlayLog == null)
            {
                return false;
            }

            RememberEntryText(entry, speech);
            addDepth++;
            try
            {
                Find.PlayLog.Add(entry);
                playLogEntryId = entry.LogID;
                return playLogEntryId >= 0;
            }
            catch (Exception ex)
            {
                Log.Warning("[Pawn Diary] Could not add generated speech to the Social log: " + ex.Message);
                return false;
            }
            finally
            {
                addDepth--;
            }
        }

        /// <summary>
        /// Returns generated text for an injected row. During PlayLog.Add the row has no stable LogID
        /// yet, so first check the object table; after save/load the GameComponent map handles it.
        /// </summary>
        public static bool TryGetText(PlayLogEntry_Interaction entry, out string text)
        {
            text = string.Empty;
            if (entry == null)
            {
                return false;
            }

            SpeechText stored;
            if (TextByEntry.TryGetValue(entry, out stored) && !string.IsNullOrWhiteSpace(stored.text))
            {
                text = stored.text;
                return true;
            }

            text = DiaryGameComponent.Current?.GeneratedSpeechTextForPlayLogEntry(entry.LogID) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }

        private static void RememberEntryText(PlayLogEntry_Interaction entry, string speech)
        {
            TextByEntry.Remove(entry);
            TextByEntry.Add(entry, new SpeechText { text = speech.Trim() });
        }
    }
}
