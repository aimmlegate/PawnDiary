// The example adapter's single GameComponent. RimWorld auto-instantiates every GameComponent
// subclass that has a (Game) constructor, so this needs no Harmony and no registration: being in a
// loaded assembly is enough.
//
// ROLE 1 — canonical integration example:
//   Registers the two process-global hooks an integration normally uses, exactly like a real
//   adapter would:
//     • RegisterEntryStatusListener — fired by PawnDiaryApi on the main thread after a saved POV's
//       status changes. We record a one-line description into ExplorerState.ListenerEvents so the
//       Hooks tab can prove the listener fires.
//     • RegisterPawnContextProvider — fired during prompt context collection. We emit a
//       "example_traits=..." line (the pawn's top two trait labels) and bump a counter so the Hooks
//       tab can prove the provider is invoked.
//
// ROLE 2 — owns the explorer's shared session state (ExplorerState) for the lifetime of the game.
//
// The OLD daily-event timer is gone: the API Explorer window and the [DebugAction] quick actions
// replace it as the canonical "smallest possible submit". A real adapter copies the request shape
// from PawnDiaryExampleDebugActions.SubmitExampleEvent and swaps the dev-action trigger for its own.
//
// Notes for adapter authors copying this file:
//   • Only the PawnDiary.Integration namespace is used — never core internals.
//   • All text the adapter sends is localized by the ADAPTER via its own Keyed strings; Pawn Diary
//     cannot translate another mod's content.
//   • RegisterEntryStatusListener / RegisterPawnContextProvider are process-global and main-thread;
//     a Game(Game) constructor runs on the main thread at game load, which is the safe place to
//     register.
//   • PawnDiaryApi never throws into the caller; the listener/provider callbacks run on the main
//     thread and must not throw either (Pawn Diary swallows but logs).
// New to C#/RimWorld? See AGENTS.md.
using System.Collections.Generic;
using System.Text;
using PawnDiary.Integration;
using RimWorld;
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Session owner of <see cref="ExplorerState"/> and the canonical registration point for the
    /// example adapter's two process-global API hooks. Auto-instantiated by RimWorld at game load.
    /// </summary>
    public class ExampleAdapterGameComponent : GameComponent
    {
        // Our packageId — Pawn Diary's one-time log messages name this adapter.
        private const string SourceId = "aimmlegate.pawndiary.adapter.example";

        // Stable ids for the two process-global hooks. Re-registering the same id replaces; these
        // are constants so a reload of the same game re-registers the same hooks cleanly.
        private const string ListenerId = SourceId + ".status_listener";
        private const string ProviderId = SourceId + ".trait_context";

        // Single-flight guards: a Game(Game) ctor can run more than once across a play session
        // (e.g. return-to-menu → new map), so we register once and bump on subsequent constructions.
        private static bool hooksRegistered;

        public ExampleAdapterGameComponent(Game game)
        {
            // No SavedGame loading needed; nothing to expose via Scribe. The component exists to
            // (a) hold a reference so RimWorld keeps us alive for the session, and (b) register
            // hooks at the safe main-thread game-load moment.
            RegisterHooksOnce();
        }

        public override void GameComponentTick()
        {
            // Intentionally empty. The old daily-event timer lived here; the explorer window and the
            // [DebugAction] quick actions replace it. A real adapter driven by its target mod's
            // events usually needs no tick at all.
        }

        // --------------------------------------------------------------------------------------------
        // Registration
        // --------------------------------------------------------------------------------------------

        private static void RegisterHooksOnce()
        {
            if (hooksRegistered)
            {
                return;
            }

            if (PawnDiaryApi.ApiVersion < 4)
            {
                // Provider = v4, Listener = v10. Bail on a too-old core; the window's readiness tab
                // surfaces ApiVersion so this is visible, not silent.
                return;
            }

            PawnDiaryApi.RegisterPawnContextProvider(ProviderId, TraitContextLine);
            if (PawnDiaryApi.ApiVersion >= 10)
            {
                PawnDiaryApi.RegisterEntryStatusListener(ListenerId, OnEntryStatus);
            }

            hooksRegistered = true;
        }

        // --------------------------------------------------------------------------------------------
        // Provider: contribute one "example_traits=..." line to every pawn summary
        // --------------------------------------------------------------------------------------------

        private static string TraitContextLine(Pawn pawn)
        {
            ExplorerState.providerInvocations++;

            List<Trait> traits = pawn?.story?.traits?.allTraits;
            if (traits == null || traits.Count == 0)
            {
                return null;
            }

            StringBuilder labels = new StringBuilder();
            int kept = 0;
            for (int i = 0; i < traits.Count && kept < 2; i++)
            {
                Trait trait = traits[i];
                if (trait == null)
                {
                    continue;
                }

                string label = trait.LabelCap.ToString();
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (kept > 0)
                {
                    labels.Append(", ");
                }

                labels.Append(label);
                kept++;
            }

            return labels.Length == 0 ? null : "example_traits=" + labels;
        }

        // --------------------------------------------------------------------------------------------
        // Listener: record entry-status changes so the Hooks tab can prove the listener fires
        // --------------------------------------------------------------------------------------------

        private static void OnEntryStatus(DiaryEntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            // Build a short one-liner for the ring buffer. The full snapshot is available; we keep
            // only the discriminator (date + group + status + title) since the Hooks tab is for
            // "did it fire?" not deep inspection.
            string description = (snapshot.date ?? "?") + "  " + (snapshot.groupLabel ?? "?")
                + "  status=" + (snapshot.status ?? "?")
                + "  title=" + (string.IsNullOrEmpty(snapshot.title) ? "(none)" : snapshot.title);

            ExplorerState.RecordListenerEvent(description);
        }
    }
}
