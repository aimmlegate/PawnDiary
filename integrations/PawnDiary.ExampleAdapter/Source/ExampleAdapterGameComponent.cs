// The example adapter's single GameComponent. RimWorld auto-instantiates every GameComponent
// subclass that has a (Game) constructor, so this needs no Harmony and no registration: being in a
// loaded assembly is enough.
//
// This file intentionally contains no direct PawnDiaryApi calls. All integration work lives in
// PawnDiaryExampleApi.cs, which is the copyable example file for adapter authors. The component only
// provides the safe game-load moment where that API facade can register its hooks.
//
// New to C#/RimWorld? See AGENTS.md.
using Verse;

namespace PawnDiaryExampleAdapter
{
    /// <summary>
    /// Session owner for the example adapter. Auto-instantiated by RimWorld at game load and used
    /// only to register the copyable API facade's hooks once.
    /// </summary>
    public class ExampleAdapterGameComponent : GameComponent
    {
        /// <summary>
        /// Constructs the session component and registers the example adapter's API hooks.
        /// </summary>
        /// <param name="game">The current RimWorld game instance supplied by the engine.</param>
        public ExampleAdapterGameComponent(Game game)
        {
            PawnDiaryExampleApi.RegisterHooksOnce();
        }

        /// <summary>
        /// Does no periodic work. Real adapters should react to their target mod's events instead of
        /// polling from this tick hook.
        /// </summary>
        public override void GameComponentTick()
        {
        }
    }
}
