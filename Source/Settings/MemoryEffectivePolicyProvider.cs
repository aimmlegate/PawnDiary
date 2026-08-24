// MemoryEffectivePolicyProvider.cs — process-wide publication slot for the committed M5 policy.
//
// Settings commit on the main thread, while a transport worker may need to read a frozen gate. A
// single atomic reference publication supplies an immutable snapshot; no consumer reads the mutable
// PawnDiarySettings object. The slot is reset/bootstrap-published for every loaded app session.
using System;

namespace PawnDiary
{
    /// <summary>Publishes and reads one complete immutable memory policy snapshot.</summary>
    internal static class MemoryEffectivePolicyProvider
    {
        private static readonly object Sync = new object();
        private static MemoryPolicySnapshot current = MemoryPolicyNormalizer.Normalize(
            MemoryPolicyNormalizer.CurrentSettingsSchemaVersion,
            new MemorySettingsPolicyFieldsV1(),
            new MemorySettingsBounds());
        private static long publicationRevision = 1;

        /// <summary>Returns the exact immutable snapshot currently visible to every consumer.</summary>
        public static MemoryPolicySnapshot Current
        {
            get
            {
                lock (Sync) return current;
            }
        }

        /// <summary>Positive process-local revision for caches; never persisted or used as a mutation fence.</summary>
        public static long PublicationRevision
        {
            get
            {
                lock (Sync) return publicationRevision;
            }
        }

        /// <summary>
        /// Publishes one persisted snapshot. Byte-equivalent policy keeps the revision; a saturated
        /// process-local clock fails closed and retains the prior complete publication.
        /// </summary>
        public static bool Publish(MemoryPolicySnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.fingerprint)) return false;
            lock (Sync)
            {
                if (current != null
                    && string.Equals(current.fingerprint, snapshot.fingerprint,
                        StringComparison.Ordinal)
                    && current.settingsSchemaVersion == snapshot.settingsSchemaVersion)
                {
                    current = snapshot;
                    return true;
                }
                if (publicationRevision == long.MaxValue) return false;
                current = snapshot;
                publicationRevision++;
                return true;
            }
        }

        /// <summary>True when the complete candidate can be published without wrapping the clock.</summary>
        public static bool CanPublish(MemoryPolicySnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.fingerprint)) return false;
            lock (Sync)
            {
                bool equivalent = current != null
                    && string.Equals(current.fingerprint, snapshot.fingerprint,
                        StringComparison.Ordinal)
                    && current.settingsSchemaVersion == snapshot.settingsSchemaVersion;
                return equivalent || publicationRevision < long.MaxValue;
            }
        }

        /// <summary>Resets transient publication identity and publishes normalized loaded settings.</summary>
        public static void Reset(
            int settingsSchemaVersion,
            MemorySettingsPolicyFieldsV1 fields,
            MemorySettingsBounds bounds)
        {
            MemoryPolicySnapshot snapshot = MemoryPolicyNormalizer.Normalize(
                settingsSchemaVersion, fields, bounds);
            lock (Sync)
            {
                current = snapshot;
                publicationRevision = 1;
            }
        }
    }
}
