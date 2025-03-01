//-----------------------------------------------------------------------------
// Filename: WebRtcConnectionManagerFactory.cs
//
// Description: Factory class for generating new WebRTCConnectionManager instances.
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

public interface IWebRtcConnectionManagerFactory
{
    IWebRtcConnectionManager CreateWebRTCConnectionManager(string peerID);
}

public class WebRtcConnectionManagerFactory : IWebRtcConnectionManagerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;

    public WebRtcConnectionManagerFactory(
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    public IWebRtcConnectionManager CreateWebRTCConnectionManager(string peerID)
    {
        var lighntinPaymentService = _serviceProvider.GetRequiredService<ILightningPaymentService>();
        var annotatedBitmapGenerator = _serviceProvider.GetRequiredService<IAnnotatedBitmapGenerator>();

        return new WebRtcConnectionManager(
            _loggerFactory.CreateLogger<WebRtcConnectionManager>(),
            lighntinPaymentService,
            annotatedBitmapGenerator);
    }
}
