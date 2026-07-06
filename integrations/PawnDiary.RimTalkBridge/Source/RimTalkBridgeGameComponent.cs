// The RimTalk bridge's game-load owner. This file is copied from the example adapter pattern:
// RimWorld auto-instantiates every GameComponent subclass with a (Game) constructor, so the bridge
// gets one safe main-thread moment to register its Pawn Diary API hooks without Harmony or XML.
//
// The old diagnostic RimTalk logger/patch was removed during the bridge reset. Future RimTalk
// behavior should add target-mod hooks beside this component and keep direct PawnDiaryApi calls in
// PawnDiaryRimTalkBridgeApi.cs.
//
// New to C#/RimWorld? See AGENTS.md in the Pawn Diary repo.
using Verse;

namespace PawnDiaryRimTalkBridge
{
    /// <summary>
    /// Session owner for the RimTalk bridge scaffold. RimWorld creates it when a save/game is loaded.
    /// </summary>
    public class RimTalkBridgeGameComponent : GameComponent
    {
        /// <summary>
        /// Constructs the session component and registers the bridge API facade's process-global hooks.
        /// </summary>
        /// <param name="game">The current RimWorld game instance supplied by the engine.</param>
        public RimTalkBridgeGameComponent(Game game)
        {
            PawnDiaryRimTalkBridgeApi.RegisterHooksOnce();
        }

        /// <summary>
        /// Does no periodic work yet. The design plan will add cached RimTalk/Pawn Diary work here.
        /// </summary>
        public override void GameComponentTick()
        {
        }
    }
}
