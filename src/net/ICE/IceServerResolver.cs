//-----------------------------------------------------------------------------
// Filename: IceServerResolver.cs
//
// Description: Resolves A list of STUN/TURN servers into a convenient form.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 24 May 2025  Aaron Clauson	Refactored from RtpIceChannel.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class IceServerResolver
{
    private static readonly ILogger logger = Log.Logger;

    private ConcurrentDictionary<STUNUri, IceServer> _iceServers = new();

    public IReadOnlyDictionary<STUNUri, IceServer> IceServers => new ReadOnlyDictionary<STUNUri, IceServer>(_iceServers);

    public IceServerResolver()
    {  }

    /// <summary>
    /// Initialises the ICE servers if any were provided in the initial configuration.
    /// ICE servers are STUN and TURN servers and are used to gather "server reflexive"
    /// and "relay" candidates. If the transport policy is "relay only" then only TURN 
    /// servers will be added to the list of ICE servers being checked.
    /// </summary>
    /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
    public void InitialiseIceServers(
        List<RTCIceServer> iceServers,
        RTCIceTransportPolicy policy)
    {
        if(iceServers == null || iceServers.Count == 0)
        {
            logger.LogDebug("{caller} no ICE servers provided.", nameof(IceServerResolver));
            return;
        }

        int iceServerID = IceServer.MINIMUM_ICE_SERVER_ID;
        _iceServers.Clear();

        foreach (var cfg in iceServers)
        {
            foreach (var rawUrl in cfg.urls.Split(new[] { ',' }, IceServer.MAXIMUM_ICE_SERVER_ID + 1, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!STUNUri.TryParse(rawUrl.Trim(), out var stunUri))
                {
                    logger.LogWarning("{caller} could not parse ICE server URL {url}", nameof(IceServerResolver), rawUrl);
                    continue;
                }

                // Filter out TLS or policy excluded entries
                if (stunUri.Scheme is STUNSchemesEnum.stuns or STUNSchemesEnum.turns ||
                    (policy == RTCIceTransportPolicy.relay && stunUri.Scheme == STUNSchemesEnum.stun))
                {
                    logger.LogWarning("{caller} ignoring ICE server {stunUri} (scheme {scheme})", nameof(IceServerResolver), stunUri, stunUri.Scheme);
                    continue;
                }

                // Avoid deplicates.
                if (_iceServers.ContainsKey(stunUri))
                {
                    continue;
                }

                var server = new IceServer(stunUri, iceServerID++, cfg.username, cfg.credential);

                // immediate bind if it’s already an IP
                if (IPAddress.TryParse(stunUri.Host, out var ip))
                {
                    server.ServerEndPoint = new IPEndPoint(ip, stunUri.Port);
                    logger.LogDebug("{caller} bound {Uri} -> {EndPoint}", nameof(IceServerResolver), stunUri, server.ServerEndPoint);
                }

                _iceServers[stunUri] = server;

                if (server.ServerEndPoint == null)
                {
                    // Kick off DNS in background, passing the key so we can update the map.
                    ScheduleDnsLookup(stunUri, server);
                }

                if (iceServerID > IceServer.MAXIMUM_ICE_SERVER_ID)
                {
                    logger.LogWarning("{caller} reached max ICE server count", nameof(IceServerResolver));
                    break;
                }
            }
        }
    }

    private void ScheduleDnsLookup(STUNUri key, IceServer server)
    {
        if (server.DnsLookupSentAt != DateTime.MinValue)
        {
            return;
        }

        if (_iceServers.ContainsKey(key))
        {
            _iceServers[key].DnsLookupSentAt = DateTime.UtcNow;
        }

        logger.LogDebug("{caller} starting DNS lookup for ICE server {Uri}", nameof(IceServerResolver), key);

        server.DnsResolutionTask = Task.Run(async () =>
        {
            try
            {
                var resolveTask = STUNDns.Resolve(key);
                var timeout = Task.Delay(TimeSpan.FromSeconds(IceServer.DNS_LOOKUP_TIMEOUT_SECONDS));
                var winner = await Task.WhenAny(resolveTask, timeout).ConfigureAwait(false);

                if (winner == resolveTask)
                {
                    var ep = await resolveTask.ConfigureAwait(false);
                    server.ServerEndPoint = ep;
                    logger.LogDebug("{caller} resolved {Uri} -> {EndPoint}", nameof(IceServerResolver), key, ep);
                }
                else
                {
                    server.Error = SocketError.TimedOut;
                    logger.LogWarning("{caller} DNS lookup timed out for {Uri}", nameof(IceServerResolver), key);
                }
            }
            catch (Exception ex)
            {
                server.Error = SocketError.HostNotFound;
                logger.LogWarning(ex, "{caller} DNS resolution failed for {Uri}", nameof(IceServerResolver), key);
            }

            _iceServers[key] = server;
        });
    }

    /// <summary>
    /// Wait until all ICE servers have resolved or timed out. Optional timeout.
    /// </summary>
    public async Task WaitForAllIceServersAsync(TimeSpan? timeout = null)
    {
        var tasks = _iceServers.Values
            .Select(s => s.DnsResolutionTask ?? Task.CompletedTask)
            .ToArray();

        var all = Task.WhenAll(tasks);
        if (timeout.HasValue)
        {
            if (await Task.WhenAny(all, Task.Delay(timeout.Value)).ConfigureAwait(false) != all)
            {
                throw new TimeoutException(
                  $"Timed out waiting {timeout.Value} for ICE server DNS resolutions");
            }
        }

        // propagate any resolution exception
        await all.ConfigureAwait(false);
    }
}

