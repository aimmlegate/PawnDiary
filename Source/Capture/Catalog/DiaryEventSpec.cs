// One DiaryEventSpec subclass per DiaryEventType value. A Spec is the "reducer" in Redux terms: it
// knows how to decide what to do with one kind of event payload. Today the Spec only delegates to
// the matching payload's pure Decide() — but the Spec class is also the future home for per-source
// metadata the catalog may want (enabled flag, weight, prompt-template key, RNG filters). Keeping
// the dispatch point as a polymorphic method on Spec means future tooling (dev tab, XML-driven
// enable/disable, weighted sampling across sources) has one obvious place to hook in.
namespace PawnDiary.Capture
{
    /// <summary>
    /// Base contract for one event source's decision logic. The catalog maps DiaryEventType → Spec;
    /// DiaryGameComponent looks up the Spec and calls Decide(). Subclasses are typed to their payload
    /// via a cast inside Decide() — C# cannot express "this Spec accepts only ThoughtEventData" in
    /// the type system without making DiaryEventSpec generic, and a generic base would break the
    /// catalog's single Dictionary.
    /// </summary>
    public abstract class DiaryEventSpec
    {
        /// <summary>Which DiaryEventType this Spec handles. Must match the catalog key it is
        /// registered under.</summary>
        public abstract DiaryEventType EventType { get; }

        /// <summary>
        /// Pure decision step. Reads only the payload + the snapshot context. The caller
        /// (DiaryGameComponent) then performs whatever impure action the decision requests. Never
        /// touches DefDatabase, Settings, or the tick manager directly.
        /// </summary>
        public abstract CaptureDecision Decide(DiaryEventData data, CaptureContext ctx);
    }
}
