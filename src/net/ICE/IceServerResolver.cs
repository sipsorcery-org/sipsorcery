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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class IceServerResolver
{
    private static readonly ILogger logger = Log.Logger;

    private FrozenDictionary<STUNUri, IceServer> _iceServers = FrozenDictionary<STUNUri, IceServer>.Empty;

    public FrozenDictionary<STUNUri, IceServer> IceServers => Volatile.Read(ref _iceServers);

    public IceServerResolver()
    { }

    /// <summary>
    /// Initialises the ICE servers if any were provided in the initial configuration.
    /// ICE servers are STUN and TURN servers and are used to gather "server reflexive"
    /// and "relay" candidates. If the transport policy is "relay only" then only TURN 
    /// servers will be added to the list of ICE servers being checked.
    /// </summary>
    /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
    public void InitialiseIceServers(
        IEnumerable<RTCIceServer> iceServers,
        RTCIceTransportPolicy policy)
    {
        if (iceServers is { })
        {
            var iceServerID = IceServer.MINIMUM_ICE_SERVER_ID;

            var iceServersDictionary = new Dictionary<STUNUri, IceServer>();

            Span<Range> ranges = stackalloc Range[IceServer.MAXIMUM_ICE_SERVER_ID + 1];

            foreach (var cfg in iceServers)
            {
                var urls = cfg.urls.AsSpan();
                var rangesCount = urls.Split(ranges, ',', StringSplitOptions.RemoveEmptyEntries);

                for (var r = 0; r < rangesCount && iceServerID <= IceServer.MAXIMUM_ICE_SERVER_ID; r++)
                {
                    var rawUrl = urls[ranges[r]].Trim();

                    if (!STUNUri.TryParse(rawUrl, out var stunUri))
                    {
                        logger.LogIceServerUrlParseError(rawUrl.ToString());
                        continue;
                    }

                    // Filter out TLS or policy excluded entries
                    if (stunUri.Scheme is STUNSchemesEnum.stuns ||
                        (stunUri.Scheme is STUNSchemesEnum.turns && stunUri.Transport == STUNProtocolsEnum.udp) ||
                        (policy == RTCIceTransportPolicy.relay && stunUri.Scheme == STUNSchemesEnum.stun))
                    {
                        logger.LogIcePolicyStunWarning(stunUri);
                        continue;
                    }

                    // Avoid deplicates.
                    if (iceServersDictionary.ContainsKey(stunUri))
                    {
                        continue;
                    }

                    var server = new IceServer(stunUri, iceServerID++, cfg.username, cfg.credential)
                    {
                        SslClientAuthenticationOptions = cfg.SslClientAuthenticationOptions,
                    };

                    // immediate bind if it’s already an IP
                    if (IPAddress.TryParse(stunUri.Host, out var ip))
                    {
                        server.ServerEndPoint = new IPEndPoint(ip, stunUri.Port);
                        logger.LogIceServerEndPointSet(stunUri, server.ServerEndPoint);
                    }

                    iceServersDictionary[stunUri] = server;

                    if (server.ServerEndPoint is null)
                    {
                        // Kick off DNS in background, passing the key so we can update the map.
                        ScheduleDnsLookup(stunUri, server);
                    }

                    if (iceServerID > IceServer.MAXIMUM_ICE_SERVER_ID)
                    {
                        logger.LogMaxServers();
                        break;
                    }
                }
            }

            _iceServers = iceServersDictionary.ToFrozenDictionary();
        }

        if (_iceServers.Count == 0)
        {
            logger.LogIceServerNotAcquired();
        }
    }

    private void ScheduleDnsLookup(STUNUri key, IceServer server)
    {
        if (server.DnsLookupSentAt != DateTime.MinValue)
        {
            return;
        }

        if (_iceServers.TryGetValue(key, out var iceServer))
        {
            iceServer.DnsLookupSentAt = DateTime.UtcNow;
        }

        logger.LogIceServerDnsLookup(key);

        server.DnsResolutionTask = Task.Run(async () =>
        {
            try
            {
                var ep = await STUNDns.Resolve(key).WaitAsync(TimeSpan.FromSeconds(IceServer.DNS_LOOKUP_TIMEOUT_SECONDS)).ConfigureAwait(false);

                Debug.Assert(ep is { });
                server.ServerEndPoint = ep;
                logger.LogIceServerResolved(key, ep);
            }
            catch (TimeoutException)
            {
                server.Error = SocketError.TimedOut;
                logger.LogIceServerConnectionTimeout(key, 0); // RequestsSent not tracked here
            }
            catch (Exception ex)
            {
                server.Error = SocketError.HostNotFound;
                logger.LogIceServerResolutionFailed(key, ex);
            }

            var iceServers = Volatile.Read(ref _iceServers);
            var iceServersDictionary = iceServers.Count > 1 ? new Dictionary<STUNUri, IceServer>(_iceServers) : new Dictionary<STUNUri, IceServer>(_iceServers);
            iceServersDictionary[key] = server;
            Volatile.Write(ref _iceServers, iceServersDictionary.ToFrozenDictionary());
        });
    }

    /// <summary>
    /// Wait until all ICE servers have resolved or timed out. Optional timeout.
    /// </summary>
    public Task WaitForAllIceServersAsync(TimeSpan? timeout = null)
    {
        var iceServers = Volatile.Read(ref _iceServers);
        var dnsResolutionTasks = new List<Task>(iceServers.Count);
        foreach (var server in iceServers.Values)
        {
            if (server.DnsResolutionTask is { })
            {
                dnsResolutionTasks.Add(server.DnsResolutionTask);
            }
        }

        return dnsResolutionTasks.Count == 0
            ? Task.CompletedTask
            : WaitForAllIceServersCoreAsync(dnsResolutionTasks.ToArray(), timeout);

        static async Task WaitForAllIceServersCoreAsync(Task[] dnsResolutionTasks, TimeSpan? timeout)
        {
            // propagate any resolution exception
            try
            {
                await Task.WhenAny(dnsResolutionTasks).WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Timed out waiting {timeout.GetValueOrDefault()} for ICE server DNS resolutions", ex);
            }
        }
    }
}

