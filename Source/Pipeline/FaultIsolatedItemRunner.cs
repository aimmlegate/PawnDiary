// Runs independent items without allowing one item's adapter failure to suppress later siblings.
// The helper is System-only so fan-out exception containment can be tested without RimWorld.
using System;
using System.Collections.Generic;

namespace PawnDiary
{
    /// <summary>Executes independent items in order and reports callback or enumeration failures.</summary>
    internal static class FaultIsolatedItemRunner
    {
        /// <summary>
        /// Runs every item and returns how many callbacks reported success. A throwing callback is
        /// reported and later items still run; a throwing reporter is also contained. If the enumerable
        /// itself fails, already completed successes are retained because the caller may use that count
        /// to settle durable work which committed before enumeration stopped.
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
            IEnumerator<T> enumerator;
            try
            {
                enumerator = items.GetEnumerator();
            }
            catch (Exception exception)
            {
                ReportFailure(default(T), exception, onFailure);
                return successful;
            }

            try
            {
                while (true)
                {
                    T item;
                    try
                    {
                        if (!enumerator.MoveNext())
                        {
                            break;
                        }
                        item = enumerator.Current;
                    }
                    catch (Exception exception)
                    {
                        // Once MoveNext/Current fails there is no safe way to discover later siblings.
                        // Return the completed count so callers can still settle committed earlier work.
                        ReportFailure(default(T), exception, onFailure);
                        break;
                    }

                    try
                    {
                        if (runItem(item))
                        {
                            successful++;
                        }
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(item, exception, onFailure);
                    }
                }
            }
            finally
            {
                try
                {
                    enumerator.Dispose();
                }
                catch (Exception exception)
                {
                    // Disposal is diagnostic cleanup. It cannot undo callbacks that already completed.
                    ReportFailure(default(T), exception, onFailure);
                }
            }

            return successful;
        }

        private static void ReportFailure<T>(
            T item,
            Exception exception,
            Action<T, Exception> onFailure)
        {
            try
            {
                onFailure?.Invoke(item, exception);
            }
            catch
            {
                // Failure reporting is diagnostic only. It must not suppress later siblings or erase
                // the completed-success count when the enumerable itself can no longer continue.
            }
        }
    }
}
