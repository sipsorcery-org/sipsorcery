using System.Collections.Concurrent;

namespace demo;

public class PeerConnectionPayState
{
    private static readonly ConcurrentDictionary<string, bool> _peerConnectionPaidState = new();

    private static readonly PeerConnectionPayState _peerConnectionPayState = new();
    public static PeerConnectionPayState Get => _peerConnectionPayState;

    private PeerConnectionPayState()
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
