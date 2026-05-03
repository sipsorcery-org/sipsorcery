//-----------------------------------------------------------------------------
// Filename: MdnsResolver.cs
//
// Description: Multicast DNS (RFC 6762) hostname resolver used by
// RtpIceChannel to look up the .local hostnames Chrome (and other WebRTC
// stacks) publish as ICE candidates for privacy reasons.
//
// .NET's Dns.GetHostAddressesAsync does not reliably do mDNS on most
// platforms -- on Windows it depends on whether the OS-level mDNS resolver
// is enabled and whether the multicast packet round-trips through the
// firewall in time. The previous fallback in RtpIceChannel.ResolveMdnsName
// surfaced this as
//
//   System.Net.Sockets.SocketException (11001): No such host is known.
//
// every time a Chrome peer was used, breaking browser-vs-SIPSorcery
// WebRTC interop on most Windows installs.
//
// This class wraps Makaretu.Dns.Multicast (RFC 6762 mDNS over UDP/5353
// multicast) into a small static helper that does an actual mDNS query
// for the requested hostname and returns whatever A / AAAA answers
// arrive within a bounded timeout. The MulticastService instance is
// cached for the lifetime of the process to avoid the per-query cost of
// rejoining the multicast group on every NIC.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com) + Claude Opus 4.7.
//
// History:
// 03 May 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Resolves multicast-DNS (.local) hostnames via Makaretu.Dns.Multicast.
    /// Used by <see cref="RtpIceChannel"/> as the default fallback when the
    /// caller has not supplied its own
    /// <see cref="RtpIceChannel.MdnsGetAddresses"/> /
    /// <see cref="RtpIceChannel.MdnsResolve"/> hook.
    /// </summary>
    internal static class MdnsResolver
    {
        /// <summary>
        /// Default time to wait for any answer to arrive after the query
        /// is sent. mDNS responses on a healthy LAN typically arrive in
        /// under 100 ms; the longer timeout covers congested wifi and
        /// virtual NIC weirdness.
        /// </summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(2);

        private static readonly ILogger logger = Log.Logger;

        // Single shared MulticastService instance per process. Starting
        // and stopping the service per query would re-bind the multicast
        // group on every NIC every time -- wasteful and also racey on
        // Windows where the rebind can take a few hundred ms.
        private static readonly object s_lock = new object();
        private static MulticastService s_mdns;
        private static bool s_startFailed;

        private static MulticastService GetService()
        {
            if (s_mdns != null || s_startFailed) return s_mdns;
            lock (s_lock)
            {
                if (s_mdns != null || s_startFailed) return s_mdns;
                try
                {
                    var mdns = new MulticastService();
                    mdns.Start();
                    s_mdns = mdns;
                }
                catch (Exception ex)
                {
                    s_startFailed = true;
                    logger.LogWarning(ex, "Failed to start mDNS MulticastService; .local hostname resolution will not work.");
                }
                return s_mdns;
            }
        }

        /// <summary>
        /// Send an mDNS query for the supplied hostname (typically
        /// "&lt;guid&gt;.local") and return whatever A and AAAA answers
        /// arrive within <see cref="DefaultTimeout"/>. Returns an empty
        /// array on timeout.
        /// </summary>
        public static async Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(hostname))
            {
                return Array.Empty<IPAddress>();
            }

            var mdns = GetService();
            if (mdns == null)
            {
                return Array.Empty<IPAddress>();
            }

            var addresses = new List<IPAddress>();
            var addressesLock = new object();
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var trimmedHostname = hostname.TrimEnd('.');

            EventHandler<MessageEventArgs> handler = (s, e) =>
            {
                try
                {
                    foreach (var rr in e.Message.Answers)
                    {
                        var name = rr.Name?.ToString()?.TrimEnd('.');
                        if (!string.Equals(name, trimmedHostname, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        IPAddress addr = null;
                        if (rr is ARecord a) addr = a.Address;
                        else if (rr is AAAARecord aaaa) addr = aaaa.Address;

                        if (addr != null)
                        {
                            lock (addressesLock)
                            {
                                if (!addresses.Contains(addr))
                                {
                                    addresses.Add(addr);
                                }
                            }
                        }
                    }

                    lock (addressesLock)
                    {
                        if (addresses.Count > 0)
                        {
                            answered.TrySetResult(true);
                        }
                    }
                }
                catch
                {
                    // Swallow handler exceptions -- the timeout below is the
                    // backstop and we don't want one bad RR to take down the
                    // shared multicast service.
                }
            };

            mdns.AnswerReceived += handler;
            try
            {
                // Issue queries for both A and AAAA. ANY would be tidier but
                // some peers respond only to specific types.
                try { mdns.SendQuery(hostname, type: DnsType.A); } catch { /* socket race */ }
                try { mdns.SendQuery(hostname, type: DnsType.AAAA); } catch { /* socket race */ }

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(DefaultTimeout);
                    using (cts.Token.Register(() => answered.TrySetResult(false)))
                    {
                        await answered.Task.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                mdns.AnswerReceived -= handler;
            }

            lock (addressesLock)
            {
                return addresses.ToArray();
            }
        }
    }
}
