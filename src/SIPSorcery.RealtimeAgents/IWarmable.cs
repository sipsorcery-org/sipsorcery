//-----------------------------------------------------------------------------
// Filename: IWarmable.cs
//
// Description: Optional for any engine (a chat client, a synthesiser, a
// recogniser, an avatar): pay first-use costs (weights page-in, context
// allocation, GPU session start) before the first call, not during it. The
// session warms what implements this when it starts; a host that shares
// engines warms them once at start-up. Replaces IReplyGenerator.WarmUpAsync,
// which went with IReplyGenerator.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Nov 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.RealtimeAgents;

/// <summary>An engine with one-time start-up costs it can pay up front.</summary>
public interface IWarmable
{
    /// <summary>Pays the start-up costs. Safe to call more than once; later calls return at once.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}
