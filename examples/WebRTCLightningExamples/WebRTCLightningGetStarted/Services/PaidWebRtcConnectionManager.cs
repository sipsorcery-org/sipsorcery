//-----------------------------------------------------------------------------
// Filename: PaidWebRtcConnectionManager.cs
//
// Description: Singleton service to create new paid WebRTC peer connections.
// Needed so the connection is created on a different scope to the web socket
// so that it survives after the web socket is closed.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 02 May 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System;
using SIPSorcery.Net;

namespace demo;

public class PaidWebRtcConnectionManager
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PaidWebRtcConnectionManager(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Func<RTCConfiguration, Task<RTCPeerConnection>> GetCreateConnectionFunction()
    {
        var scope = _scopeFactory.CreateScope();

        var paidConn = scope.ServiceProvider.GetRequiredService<IPaidWebRtcConnection>();

        paidConn.OnPeerConnectionClosedOrFailed += () => scope.Dispose();

        return paidConn.CreatePeerConnection;
    }
}
