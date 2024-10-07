using System;
using System.Threading;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// A thread-safe struct that represents a one-time event.
    /// </summary>
    struct Once
    {
        int occured;

        /// <summary>
        /// Gets a value indicating whether the event has occurred or not.
        /// </summary>
        public bool HasOccurred => Interlocked.CompareExchange(ref this.occured, 0, 0) != 0;

        /// <summary>
        /// Tries to mark the event as occurred and returns <c>true</c> if successful.
        /// Returns <c>false</c> if the even has already been marked.</c>
        /// </summary>
        public bool TryMarkOccurred() => Interlocked.CompareExchange(ref this.occured, 1, comparand: 0) == 0;

        /// <summary>
        /// Marks the event as occurred and throws <see cref="InvalidOperationException"/> if it has already occurred.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the event has already occurred.</exception>
        public void MarkOccurred()
        {
            if (!this.TryMarkOccurred())
            {
                throw new InvalidOperationException("Can only be called once");
            }
        }
    }
}
