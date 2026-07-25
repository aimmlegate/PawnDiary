// Runs independent items without allowing one item's adapter failure to suppress later siblings.
// The helper is System-only so fan-out exception containment can be tested without RimWorld.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Executes independent items in order and reports per-item failures.</summary>
    internal static class FaultIsolatedItemRunner
    {
        /// <summary>
        /// Runs every item and returns how many callbacks reported success. A throwing callback is
        /// reported and later items still run; a throwing reporter is also contained.
        /// </summary>
        public static int Run<T>(
            IEnumerable<T> items,
            Func<T, bool> runItem,
            Action<T, Exception> onFailure)
        {
            if (items == null || runItem == null)
            {
                return 0;
            }

            int successful = 0;
            foreach (T item in items)
            {
                try
                {
                    if (runItem(item))
                    {
                        successful++;
                    }
                }
                catch (Exception exception)
                {
                    try
                    {
                        onFailure?.Invoke(item, exception);
                    }
                    catch
                    {
                        // Failure reporting is diagnostic only. It must not suppress later siblings.
                    }
                }
            }

            return successful;
        }
    }
}
