//-----------------------------------------------------------------------------
// Filename: PaidWebRtcConnectionFactory.cs
//
// Description: Factory class for generating new PaidWebRTCConnection instances.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace demo;

public interface IPaidWebRtcConnectionFactory
{
    IPaidWebRtcConnection CreatePaidWebRTCConnection(string peerID);
}

public class PaidWebRtcConnectionFactory : IPaidWebRtcConnectionFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public PaidWebRtcConnectionFactory(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    public IPaidWebRtcConnection CreatePaidWebRTCConnection(string peerID)
    {
        var annotatedBitmapGenerator = _serviceProvider.GetRequiredService<IAnnotatedBitmapGenerator>();
        var frameConfigStateMachine = _serviceProvider.GetRequiredService<IFrameConfigStateMachine>();

        return new PaidWebRtcConnection(
            _loggerFactory.CreateLogger<PaidWebRtcConnection>(),
            annotatedBitmapGenerator,
            frameConfigStateMachine);
    }
}
