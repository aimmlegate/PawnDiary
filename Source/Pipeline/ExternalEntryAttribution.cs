// Pure attribution recovery for entries created or influenced by the public external API.
// The saved DiaryEvent gameContext already carries the adapter marker, so UI and API snapshots can
// derive source metadata without changing save data.
//
// New to C#/RimWorld? See AGENTS.md.
namespace PawnDiary
{
    /// <summary>
    /// Reads external authorship metadata from the stable key/value context saved on diary events.
    /// </summary>
    public static class ExternalEntryAttribution
    {
        /// <summary>
        /// True when a saved game-context string is explicitly marked as coming from the external API.
        /// </summary>
        public static bool IsExternallyAuthored(string gameContext)
        {
            return DiaryContextFields.HasMarker(gameContext, "external=")
                && !string.IsNullOrWhiteSpace(SourceIdForContext(gameContext));
        }

        /// <summary>
        /// Returns the cleaned adapter source id from an external entry context, or empty for native
        /// diary entries. The explicit external marker prevents unrelated native source= fields from
        /// being misreported as adapter authorship.
        /// </summary>
        public static string SourceIdForContext(string gameContext)
        {
            if (!DiaryContextFields.HasMarker(gameContext, "external="))
            {
                return string.Empty;
            }

            return ExternalWritingStyleOverrideText.CleanSourceId(
                DiaryContextFields.Value(gameContext, "source"));
        }
    }
}
