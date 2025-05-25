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
    private const int MAXIMUM_ICE_SERVER_URLS = 10;

    private static readonly ILogger logger = Log.Logger;

    private ConcurrentDictionary<STUNUri, IceServer> _iceServers = new();

    public IReadOnlyDictionary<STUNUri, IceServer> IceServers => new ReadOnlyDictionary<STUNUri, IceServer>(_iceServers);

    /// <summary>
    /// Builds the set of ICE servers (STUN/TURN), and for any whose host
    /// isn’t already an IP, starts an async DNS lookup in the background.
    /// Returns immediately with endpoints possibly still null.
    /// </summary>
    public void InitialiseIceServers(
        List<RTCIceServer> iceServers,
        RTCIceTransportPolicy policy)
    {
        int iceServerID = IceServer.MINIMUM_ICE_SERVER_ID;
        _iceServers.Clear();

        foreach (var cfg in iceServers)
        {
            foreach (var rawUrl in cfg.urls.Split(new [] { ',' }, MAXIMUM_ICE_SERVER_URLS, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!STUNUri.TryParse(rawUrl.Trim(), out var stunUri))
                {
                    logger.LogWarning("Could not parse ICE server URL {url}", rawUrl);
                    continue;
                }

                // Filter out TLS or policy excluded entries
                if (stunUri.Scheme is STUNSchemesEnum.stuns or STUNSchemesEnum.turns ||
                    (policy == RTCIceTransportPolicy.relay && stunUri.Scheme == STUNSchemesEnum.stun))
                {
                    logger.LogWarning("Ignoring ICE server {stunUri} (scheme {scheme})", stunUri, stunUri.Scheme);
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
                    logger.LogDebug("Bound {Uri} -> {EndPoint}", stunUri, server.ServerEndPoint);
                }
                else
                {
                    // Kick off DNS in background, passing the key so we can update the map.
                    ScheduleDnsLookup(stunUri, server);
                }

                _iceServers[stunUri] = server;

                if (iceServerID > IceServer.MAXIMUM_ICE_SERVER_ID)
                {
                    logger.LogWarning("Reached max ICE server count");
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

        server.DnsLookupSentAt = DateTime.UtcNow;
        logger.LogDebug("Starting DNS lookup for {Uri}", key);

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
                    logger.LogDebug("Resolved {Uri} -> {EndPoint}", key, ep);
                }
                else
                {
                    server.Error = SocketError.TimedOut;
                    logger.LogWarning("DNS lookup timed out for {Uri}", key);
                }
            }
            catch (Exception ex)
            {
                server.Error = SocketError.HostNotFound;
                logger.LogWarning(ex, "DNS resolution failed for {Uri}", key);
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

