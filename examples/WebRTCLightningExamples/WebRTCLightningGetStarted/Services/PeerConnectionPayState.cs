//-----------------------------------------------------------------------------
// Filename: PeerConnectionPayState.cs
//
// Description: Maintains the payment state of each WebRTC peer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace demo;

public class PeerConnectionPayState
{
    private readonly ConcurrentDictionary<string, bool> _peerConnectionPaidState = new();

    public PeerConnectionPayState()
    { }

    public void TryAddPeer(string peerID)
    {
        _peerConnectionPaidState.TryAdd(peerID, false);
    }

    public void TryRemovePeer(string peerID)
    {
        _peerConnectionPaidState.TryRemove(peerID, out _);
    }

    public bool TrySetPaid(string peerID)
    {
        return _peerConnectionPaidState.TryUpdate(peerID, true, false);
    }

    public bool TryGetIsPaid(string peerID)
    {
        if(_peerConnectionPaidState.TryGetValue(peerID, out var isPaid))
        {
            return isPaid;
        }

        return false;
    }
}
