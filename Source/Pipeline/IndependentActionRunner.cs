// Runs a sequence of independent lifecycle actions without letting one failure suppress later work.
// Startup patch registration and pre-save maintenance both use this System-only helper, so their
// exception-isolation contract can be tested without RimWorld.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Executes every supplied action and reports each failure to the caller.</summary>
    internal static class IndependentActionRunner
    {
        /// <summary>
        /// Runs all actions in order. A null or throwing action is reported and later actions still run.
        /// </summary>
        public static void RunAll(
            IReadOnlyList<Action> actions,
            Action<int, Exception> onFailure)
        {
            if (actions == null)
            {
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                try
                {
                    Action action = actions[i];
                    if (action == null)
                    {
                        throw new InvalidOperationException("Independent action was null.");
                    }

                    action();
                }
                catch (Exception exception)
                {
                    try
                    {
                        onFailure?.Invoke(i, exception);
                    }
                    catch
                    {
                        // Failure reporting is optional diagnostic work. It must not become a new
                        // reason to skip the remaining independent lifecycle actions.
                    }
                }
            }
        }
    }
}
