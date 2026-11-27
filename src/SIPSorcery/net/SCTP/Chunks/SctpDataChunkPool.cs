//-----------------------------------------------------------------------------
// Filename: SctpDataChunkPool.cs
//
// Description: SctpDataChunk Pool for SctpDataSender
//
// Remarks:
// Used to avoid allocations of SctpDataChunk's and its UserData byte[]
//
// Author(s):
// Ullrich Praetz (https://github.com/friflo)
// 
// History:
// 31 Oct 2026	Ullrich Praetz, Germany
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SIPSorcery.Net;

internal class SctpDataChunkPool
{
    private readonly ConcurrentQueue<SctpDataChunk> Queue = new();
    private readonly Func<SctpDataChunk>            Factory;
    private readonly int                            MaxCapacity;
    private          int                            CurrentCount;

    internal SctpDataChunkPool(Func<SctpDataChunk> factory, int maxCapacity = 1024)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        MaxCapacity = maxCapacity;
    }

    internal SctpDataChunk Get()
    {
        if (Queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref CurrentCount);
            return item;
        }

        return Factory();
    }

    internal void Return(SctpDataChunk item)
    {
        if (item == null) {
            return;
        }
        if (Volatile.Read(ref CurrentCount) < MaxCapacity)
        {
            Queue.Enqueue(item);
            Interlocked.Increment(ref CurrentCount);
        }
    }
}