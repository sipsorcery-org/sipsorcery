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

using System;
using System.Collections.Concurrent;

namespace demo;

public record PayForTimeState(DateTimeOffset PaidUntil, string? LightningInvoiceRHash, int InvoicePurchaseSeconds);

public class PeerConnectionPayState
{
    private readonly ConcurrentDictionary<string, PayForTimeState> _peerConnectionPaidState = new();

    private readonly ConcurrentDictionary<string, string> _invoiceToPeerMap = new();

    public PeerConnectionPayState()
    { }

    public void TryAddPeer(string peerID, DateTimeOffset paidUntil)
    {
        _peerConnectionPaidState.TryAdd(peerID, new(paidUntil, null, 0));
    }

    public void TryAddInvoice(string peerID, string invoiceRHash, int invoicePurchaseSeconds)
    {
        if (_peerConnectionPaidState.ContainsKey(peerID))
        {
            if (_peerConnectionPaidState.TryGetValue(peerID, out var currentState))
            {
                var pendingInvoiceState = currentState with
                {
                    LightningInvoiceRHash = invoiceRHash,
                    InvoicePurchaseSeconds = invoicePurchaseSeconds
                };

                _peerConnectionPaidState.TryUpdate(peerID, pendingInvoiceState, currentState);

                if (!_invoiceToPeerMap.ContainsKey(invoiceRHash))
                {
                    _invoiceToPeerMap.TryAdd(invoiceRHash, peerID);
                }
            }
        }
    }

    public void TryRemovePeer(string peerID)
    {
        _peerConnectionPaidState.TryRemove(peerID, out _);
    }

    public void TryRemoveInvoice(string invoiceRHash)
    {
        _invoiceToPeerMap.TryRemove(invoiceRHash, out _);
    }

    public int TrySetPaid(string invoiceRHash)
    {
        if (_invoiceToPeerMap.TryGetValue(invoiceRHash, out var peerID))
        {
            if (_peerConnectionPaidState.TryGetValue(peerID, out var currentState))
            {
                var paidState = new PayForTimeState(DateTimeOffset.Now.AddSeconds(currentState.InvoicePurchaseSeconds), null, 0);

                if (_peerConnectionPaidState.TryUpdate(peerID, paidState, currentState))
                {
                    return (int)paidState.PaidUntil.Subtract(DateTimeOffset.Now).TotalSeconds;
                }
            }

            _invoiceToPeerMap.TryRemove(invoiceRHash, out _);
        }

        return 0;
    }

    public int GetRemainingPaidSeconds(string peerID)
    {
        if(_peerConnectionPaidState.TryGetValue(peerID, out var currentState))
        {
            if (currentState.PaidUntil == DateTimeOffset.MinValue)
            {
                return 0;
            }
            else
            {
                return (int)currentState.PaidUntil.Subtract(DateTimeOffset.Now).TotalSeconds;
            }
        }

        return 0;
    }
}
