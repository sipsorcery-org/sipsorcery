//-----------------------------------------------------------------------------
// Filename: IceSession.cs
//
// Description: Represents a ICE Session as described in the Interactive
// Connectivity Establishment RFC8445 https://tools.ietf.org/html/rfc8445.
//
// Additionally support for the following standards or proposed standards 
// is included:
// - "Trickle ICE" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-ice-trickle-21.
// - "WebRTC IP Address Handling Requirements" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12
//   SECURITY NOTE: See https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12#section-5.2
//   for recommendations on how a WebRTC application should expose a
//   hosts IP address information. This implementation is using Mode 2.
// - Traversal Using Relays around NAT (TURN): Relay Extensions to 
//   Session Traversal Utilities for NAT (STUN)
//   https://tools.ietf.org/html/rfc5766
//
// Notes:
// The source from Chromium that performs the equivalent of this IceSession class
// (and much more) is:
// https://chromium.googlesource.com/external/webrtc/+/refs/heads/master/p2p/base/p2p_transport_channel.cc
//
// Multicast DNS: Chromium (and possibly other WebRTC stacks) make use of *.local
// DNS hostnames. Support for such hostnames is currently NOT implemented in
// this library as it would mean introducing another dependency for what is
// currently deemed to be a narrow edge case. Windows 10 has recently introduced a level
// of support for these domains so perhaps it will make it into the .Net Core
// plumbing in the not too distant future.
// https://tools.ietf.org/html/rfc6762: Multicast DNS (for ".local" Top Level Domain lookups on macos)
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.UnitTests")]

namespace SIPSorcery.Net
{
    /// <summary>
    /// An ICE session carries out connectivity checks with a remote peer in an
    /// attempt to determine the best destination end point to communicate with the
    /// remote party.
    /// </summary>
    /// <remarks>
    /// Limitations:
    ///  - To reduce complexity only a single checklist is used. This is based on the main 
    ///    webrtc use case where RTP (audio and video) and RTCP are all multiplexed on a 
    ///    single socket pair. Therefore  there only needs to be a single component and single 
    ///    data stream. If an additional use case occurs then multiple checklists could be added.
    /// </remarks>
    public class IceSession
    {
        private static DnsClient.LookupClient _dnsLookupClient;

        /// <summary>
        /// List of state conditions for a check list entry as the connectivity checks are 
        /// carried out.
        /// </summary>
        internal enum ChecklistEntryState
        {
            /// <summary>
            /// A check has not been sent for this pair, but the pair is not Frozen.
            /// </summary>
            Waiting,

            /// <summary>
            /// A check has been sent for this pair, but the transaction is in progress.
            /// </summary>
            InProgress,

            /// <summary>
            /// A check has been sent for this pair, and it produced a successful result.
            /// </summary>
            Succeeded,

            /// <summary>
            /// A check has been sent for this pair, and it failed (a response to the 
            /// check was never received, or a failure response was received).
            /// </summary>
            Failed,

            /// <summary>
            /// A check for this pair has not been sent, and it cannot be sent until the 
            /// pair is unfrozen and moved into the Waiting state.
            /// </summary>
            Frozen
        }

        /// <summary>
        /// Represents the state of the ICE checks for a checklist.
        /// </summary>
        /// <remarks>
        /// As specified in https://tools.ietf.org/html/rfc8445#section-6.1.2.1.
        /// </remarks>
        internal enum ChecklistState
        {
            /// <summary>
            /// The checklist is neither Completed nor Failed yet.
            /// Checklists are initially set to the Running state.
            /// </summary>
            Running,

            /// <summary>
            /// The checklist contains a nominated pair for each
            /// component of the data stream.
            /// </summary>
            Completed,

            /// <summary>
            /// The checklist does not have a valid pair for each component
            /// of the data stream, and all of the candidate pairs in the
            /// checklist are in either the Failed or the Succeeded state.  In
            /// other words, at least one component of the checklist has candidate
            /// pairs that are all in the Failed state, which means the component
            /// has failed, which means the checklist has failed.
            /// </summary>
            Failed
        }

        /// <summary>
        /// A check list entry represents an ICE candidate pair (local candidate + remote candidate)
        /// that is being checked for connectivity. If the overall ICE session does succeed it will
        /// be due to one of these checklist entries successfully completing the ICE checks.
        /// </summary>
        internal class ChecklistEntry : IComparable
        {
            public RTCIceCandidate LocalCandidate;
            public RTCIceCandidate RemoteCandidate;

            /// <summary>
            /// The current state of this checklist entry. Indicates whether a STUN check has been
            /// sent, responded to, timed out etc.
            /// </summary>
            /// <remarks>
            /// See https://tools.ietf.org/html/rfc8445#section-6.1.2.6 for the state
            /// transition diagram for a check list entry.
            /// </remarks>
            public ChecklistEntryState State = ChecklistEntryState.Frozen;

            /// <summary>
            /// The candidate pairs whose local and remote candidates are both the
            /// default candidates for a particular component is called the "default
            /// candidate pair" for that component.  This is the pair that would be
            /// used to transmit data if both agents had not been ICE aware.
            /// </summary>
            public bool Default;

            /// <summary>
            /// Gets set to true when the connectivity checks for the candidate pair are
            /// successful. Valid entries are eligible to be set as nominated.
            /// </summary>
            public bool Valid;

            /// <summary>
            /// Gets set to true if this entry is selected as the single nominated entry to be
            /// used for the session communications. Setting a check list entry as nominated
            /// indicates the ICE checks have been successful and the application can begin
            /// normal communications.
            /// </summary>
            public bool Nominated;

            /// <summary>
            /// The priority for the candidate pair:
            ///  - Let G be the priority for the candidate provided by the controlling agent.
            ///  - Let D be the priority for the candidate provided by the controlled agent.
            /// Pair Priority = 2^32*MIN(G,D) + 2*MAX(G,D) + (G>D?1:0)
            /// </summary>
            /// <remarks>
            /// See https://tools.ietf.org/html/rfc8445#section-6.1.2.3.
            /// </remarks>
            public ulong Priority { get; private set; }

            /// <summary>
            /// Timestamp the last connectivity check (STUN binding request) was sent at.
            /// </summary>
            public DateTime LastCheckSentAt = DateTime.MinValue;

            /// <summary>
            /// The number of checks that have been sent without a response.
            /// </summary>
            public int ChecksSent;

            /// <summary>
            /// The transaction ID that was set in the last STUN request connectivity check.
            /// </summary>
            public string RequestTransactionID;

            /// <summary>
            /// Creates a new entry for the ICE session checklist.
            /// </summary>
            /// <param name="localCandidate">The local candidate for the checklist pair.</param>
            /// <param name="remoteCandidate">The remote candidate for the checklist pair.</param>
            /// <param name="isLocalController">True if we are acting as the controlling agent in the ICE session.</param>
            public ChecklistEntry(RTCIceCandidate localCandidate, RTCIceCandidate remoteCandidate, bool isLocalController)
            {
                LocalCandidate = localCandidate;
                RemoteCandidate = remoteCandidate;

                var controllingCandidate = (isLocalController) ? localCandidate : remoteCandidate;
                var controlledCandidate = (isLocalController) ? remoteCandidate : localCandidate;

                Priority = (2 << 32) * Math.Min(controllingCandidate.priority, controlledCandidate.priority) +
                    (ulong)2 * Math.Max(controllingCandidate.priority, controlledCandidate.priority) +
                    (ulong)((controllingCandidate.priority > controlledCandidate.priority) ? 1 : 0);
            }

            /// <summary>
            /// Compare method to allow the checklist to be sorted in priority order.
            /// </summary>
            public int CompareTo(Object other)
            {
                if (other is ChecklistEntry)
                {
                    //return Priority.CompareTo((other as ChecklistEntry).Priority);
                    return (other as ChecklistEntry).Priority.CompareTo(Priority);
                }
                else
                {
                    throw new ApplicationException("CompareTo is not implemented for ChecklistEntry and arbitrary types.");
                }
            }
        }

        /// <summary>
        /// If ICE servers (STUN or TURN) are being used with the session this class is used to track
        /// the connection state for each server that gets used.
        /// </summary>
        internal class IceServerConnectionState
        {
            /// <summary>
            /// The maximum number of requests to send to an ICE server without getting 
            /// a response.
            /// </summary>
            internal const int MAX_REQUESTS = 6;

            /// <summary>
            /// Time to wait for a DNS lookup of an ICE server to complete.
            /// </summary>
            internal const int DNS_LOOKUP_TIMEOUT_SECONDS = 3;

            /// <summary>
            /// The period at which to refresh a successful STUN binding. If the ICE
            /// server did not get used as the nominated candidate the ICE server 
            /// checks timer will be stopped.
            /// </summary>
            internal const int STUN_BINDING_REQUEST_REFRESH_SECONDS = 180;

            internal STUNUri _uri;
            internal string _username;
            internal string _password;

            /// <summary>
            /// The end point for this STUN or TURN server. Will be set asynchronously once
            /// any required DNS lookup completes.
            /// </summary>
            internal IPEndPoint ServerEndPoint { get; set; }

            /// <summary>
            /// The transaction ID to use in STUN requests. It is used to match responses
            /// with connection checks for this ICE serve entry.
            /// </summary>
            internal string TransactionID { get; private set; }

            /// <summary>
            /// The timestamp that the DNS lookup for this ICE server was sent at.
            /// </summary>
            internal DateTime DnsLookupSentAt { get; set; } = DateTime.MinValue;

            /// <summary>
            /// The number of requests that have been sent to the server without
            /// a response.
            /// </summary>
            internal int OutstandingRequestsSent { get; set; }

            /// <summary>
            /// The timestamp the most recent binding request was sent at.
            /// </summary>
            internal DateTime LastRequestSentAt { get; set; }

            /// <summary>
            /// The timestamp of the most recent response received from the ICE server.
            /// </summary>
            internal DateTime LastResponseReceivedAt { get; set; } = DateTime.MinValue;

            /// <summary>
            /// Records the failure message if there was an error configuring or contacting
            /// the STUN or TURN server.
            /// </summary>
            internal SocketError Error { get; set; } = SocketError.Success;

            /// <summary>
            /// If the initial Binding (for STUN) or Allocate (for TURN) connection check is successful 
            /// this will hold the resultant server reflexive transport address.
            /// </summary>
            internal IPEndPoint ServerReflexiveEndPoint { get; set; }

            /// <summary>
            /// If the ICE server being checked is a TURN one and the Allocate request is successful this
            /// will hold the relay transport address.
            /// </summary>
            internal IPEndPoint RelayEndPoint { get; set; }

            /// <summary>
            /// Default constructor.
            /// </summary>
            /// <param name="uri">The STUN or TURN server URI the connection is being attempted to.</param>
            /// <param name="username">Optional. If authentication is required the username to use.</param>
            /// <param name="password">Optional. If authentication is required the password to use.</param>
            internal IceServerConnectionState(STUNUri uri, string username, string password)
            {
                _uri = uri;
                _username = username;
                _password = password;

                TransactionID = Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH);
            }

            /// <summary>
            /// Gets an ICE candidate for this ICE server once the required server responses have been received.
            /// </summary>
            /// <param name="init">The initialisation parameters for the ICE candidate (mainly local username).</param>
            /// <param name="type">The type of ICE candidate to get, must be srflx or relay.</param>
            /// <param name="relatedAddress">The related host address for the candidate.</param>
            /// <param name="relatedPort">The related host port for the candidate.</param>
            /// <returns>An ICE candidate that can be sent to the remote peer.</returns>
            internal RTCIceCandidate GetCandidate(RTCIceCandidateInit init, RTCIceCandidateType type, IPAddress relatedAddress, ushort relatedPort)
            {
                RTCIceCandidate candidate = new RTCIceCandidate(init);

                if (type == RTCIceCandidateType.srflx && ServerReflexiveEndPoint != null)
                {
                    candidate.SetAddressProperties(RTCIceProtocol.udp, ServerReflexiveEndPoint.Address, (ushort)ServerReflexiveEndPoint.Port,
                        type, relatedAddress, relatedPort);
                    candidate.IceServerUri = _uri;

                    return candidate;
                }
                else if (type == RTCIceCandidateType.relay && RelayEndPoint != null)
                {
                    candidate.SetAddressProperties(RTCIceProtocol.udp, RelayEndPoint.Address, (ushort)RelayEndPoint.Port,
                        type, relatedAddress, relatedPort);
                    candidate.IceServerUri = _uri;

                    return candidate;
                }
                else
                {
                    logger.LogWarning($"Could not get ICE server candidate for {_uri} and type {type}.");
                    return null;
                }
            }
        }

        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;
        private const int MAX_CHECKLIST_ENTRIES = 25;   // Maximum number of entries that can be added to the checklist of candidate pairs.
        private const string MDNS_TLD = ".local";         // Top Level Domain name for multicast lookups as per RFC6762.
        public const string SDP_MID = "0";
        public const int SDP_MLINE_INDEX = 0;

        /// <summary>
        /// ICE transaction spacing interval in milliseconds.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-14.
        /// </remarks>
        private const int Ta = 50;

        /// <summary>
        /// The number of connectivity checks to carry out.
        /// </summary>
        private const int N = 5;

        private static readonly ILogger logger = Log.Logger;

        private RTPChannel _rtpChannel;
        private IPAddress _remoteSignallingAddress;
        private List<RTCIceServer> _iceServers;
        private RTCIceTransportPolicy _policy;
        private ConcurrentDictionary<STUNUri, IceServerConnectionState> _iceServerConnections;

        private IceServerConnectionState _activeIceServer;

        public RTCIceComponent Component { get; private set; }

        public RTCIceGatheringState GatheringState { get; private set; } = RTCIceGatheringState.@new;

        public RTCIceConnectionState ConnectionState { get; private set; } = RTCIceConnectionState.@new;

        /// <summary>
        /// True if we are the "controlling" ICE agent (we initiated the communications) or
        /// false if we are the "controlled" agent.
        /// </summary>
        public bool IsController { get; internal set; }

        /// <summary>
        /// The list of host ICE candidates that have been gathered for this peer.
        /// </summary>
        public List<RTCIceCandidate> Candidates
        {
            get
            {
                return _candidates;
            }
        }

        private List<RTCIceCandidate> _candidates = new List<RTCIceCandidate>();
        private List<RTCIceCandidate> _remoteCandidates = new List<RTCIceCandidate>();

        /// <summary>
        /// A queue of remote ICE candidates that have been added to the session and that
        /// are waiting to be processed to determine if they will create a new checklist entry.
        /// </summary>
        private ConcurrentQueue<RTCIceCandidate> _pendingRemoteCandidates = new ConcurrentQueue<RTCIceCandidate>();

        /// <summary>
        /// The state of the checklist as the ICE checks are carried out.
        /// </summary>
        internal ChecklistState _checklistState = ChecklistState.Running;

        /// <summary>
        /// The checklist of local and remote candidate pairs
        /// </summary>
        internal List<ChecklistEntry> _checklist = new List<ChecklistEntry>();

        /// <summary>
        /// For local candidates this implementation takes a shortcut to reduce complexity. 
        /// The RTP socket will always be bound to one of:
        ///  - IPAddress.IPv6Any [::], 
        ///  - IPAddress.Any 0.0.0.0, or,
        ///  - a specific single IP address. 
        /// As such it's only necessary to create a single checklist entry for each remote 
        /// candidate. 
        /// Real host candidates must still be generated based on all local IP addresses. Those
        /// local candidates need to be transmitted to the remote peer but they don't need to
        /// be used when populating the checklist.
        /// </summary>
        internal readonly RTCIceCandidate _localChecklistCandidate;

        /// <summary>
        /// If the connectivity checks are successful this will hold the nominated
        /// remote candidate.
        /// </summary>
        public RTCIceCandidate NominatedCandidate { get; private set; }

        /// <summary>
        /// If the session has successfully connected this returns the remote end point of
        /// the nominate candidate.
        /// </summary>
        public IPEndPoint ConnectedRemoteEndPoint
        {
            get { return (NominatedCandidate != null) ? NominatedCandidate.DestinationEndPoint : null; }
        }

        /// <summary>
        /// Retransmission timer for STUN transactions, measured in milliseconds.
        /// </summary>
        /// <remarks>
        /// As specified in https://tools.ietf.org/html/rfc8445#section-14.
        /// </remarks>
        internal int RTO
        {
            get
            {
                if (GatheringState == RTCIceGatheringState.gathering)
                {
                    return Math.Max(500, Ta * Candidates.Count(x => x.type == RTCIceCandidateType.srflx || x.type == RTCIceCandidateType.relay));
                }
                else
                {
                    return Math.Max(500, Ta * N * (_checklist.Count(x => x.State == ChecklistEntryState.Waiting) + _checklist.Count(x => x.State == ChecklistEntryState.InProgress)));
                }
            }
        }

        public readonly string LocalIceUser;
        public readonly string LocalIcePassword;
        public string RemoteIceUser { get; private set; }
        public string RemoteIcePassword { get; private set; }

        private bool _closed = false;
        private Timer _processChecklistTimer;
        private Timer _processIceServersTimer;

        public event Action<RTCIceCandidate> OnIceCandidate;
        public event Action<RTCIceConnectionState> OnIceConnectionStateChange;
        public event Action<RTCIceGatheringState> OnIceGatheringStateChange;
        public event Action OnIceCandidateError;

        /// <summary>
        /// Creates a new instance of an ICE session.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel is the object managing the socket
        /// doing the media sending and receiving. Its the same socket the ICE session
        /// will need to initiate all the connectivity checks on.</param>
        /// <param name="component">The component (RTP or RTCP) the channel is being used for. Note
        /// for cases where RTP and RTCP are multiplexed the component is set to RTP.</param>
        /// <param name="remoteSignallingAddress"> Optional. If supplied this address will 
        /// dictate which local interface host ICE candidates will be gathered from.
        /// Restricting the host candidate IP addresses to a single interface is 
        /// as per the recommendation at:
        /// https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12#section-5.2.
        /// If this is not set then the default is to use the Internet facing interface as
        /// returned by the OS routing table.</param>
        /// <param name="iceServers">A list of STUN or TURN servers that can be used by this ICE agent.</param>
        /// <param name="policy">Determines which ICE candidates can be used in this ICE session.</param>
        public IceSession(RTPChannel rtpChannel,
            RTCIceComponent component,
            IPAddress remoteSignallingAddress,
            List<RTCIceServer> iceServers = null,
            RTCIceTransportPolicy policy = RTCIceTransportPolicy.all)
        {
            if (rtpChannel == null)
            {
                throw new ArgumentNullException("rtpChannel");
            }

            if (_dnsLookupClient == null)
            {
                _dnsLookupClient = new DnsClient.LookupClient();
            }

            _rtpChannel = rtpChannel;
            Component = component;
            _remoteSignallingAddress = remoteSignallingAddress;
            _iceServers = iceServers;
            _policy = policy;

            LocalIceUser = Crypto.GetRandomString(ICE_UFRAG_LENGTH);
            LocalIcePassword = Crypto.GetRandomString(ICE_PASSWORD_LENGTH);

            _localChecklistCandidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                sdpMid = SDP_MID,
                sdpMLineIndex = SDP_MLINE_INDEX,
                usernameFragment = LocalIceUser
            });

            _localChecklistCandidate.SetAddressProperties(
                RTCIceProtocol.udp,
                _rtpChannel.RTPLocalEndPoint.Address,
                (ushort)_rtpChannel.RTPLocalEndPoint.Port,
                RTCIceCandidateType.host,
                null,
                0);
        }

        /// <summary>
        /// We've been given the green light to start the ICE candidate gathering process.
        /// This could include contacting external STUN and TURN servers. Events will 
        /// be fired as each ICE is identified and as the gathering state machine changes
        /// state.
        /// </summary>
        public void StartGathering()
        {
            if (GatheringState == RTCIceGatheringState.@new)
            {
                GatheringState = RTCIceGatheringState.gathering;
                OnIceGatheringStateChange?.Invoke(RTCIceGatheringState.gathering);

                _candidates = GetHostCandidates();

                if (_candidates == null || _candidates.Count == 0)
                {
                    logger.LogWarning("ICE session did not discover any host candidates no point continuing.");
                    OnIceCandidateError?.Invoke();
                    OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.failed);
                    OnIceConnectionStateChange(RTCIceConnectionState.failed);
                }
                else
                {
                    logger.LogDebug($"ICE session discovered {_candidates.Count} local candidates.");

                    if (_iceServers != null)
                    {
                        InitialiseIceServers(_iceServers);
                        _processIceServersTimer = new Timer(CheckIceServers, null, 0, Ta);
                    }

                    _processChecklistTimer = new Timer(ProcessChecklist, null, 0, Ta);
                }
            }
        }

        /// <summary>
        /// Set the ICE credentials that have been supplied by the remote peer. Once these
        /// are set the connectivity checks should be able to commence.
        /// </summary>
        /// <param name="username">The remote peer's ICE username.</param>
        /// <param name="password">The remote peer's ICE password.</param>
        public void SetRemoteCredentials(string username, string password)
        {
            logger.LogDebug("ICE session remote credentials set.");

            RemoteIceUser = username;
            RemoteIcePassword = password;

            // Once the remote party's ICE credentials are known connection checking can 
            // commence immediately as candidates trickle in.
            ConnectionState = RTCIceConnectionState.checking;
            OnIceConnectionStateChange?.Invoke(ConnectionState);
        }

        /// <summary>
        /// Closes the ICE session and stops any further connectivity checks.
        /// </summary>
        public void Close()
        {
            if (!_closed)
            {
                _closed = true;
                _processChecklistTimer?.Dispose();
                _processIceServersTimer?.Dispose();
            }
        }

        /// <summary>
        /// Adds a remote ICE candidate to the ICE session.
        /// </summary>
        /// <param name="candidate">An ICE candidate from the remote party.</param>
        public void AddRemoteCandidate(RTCIceCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.address))
            {
                // Note that the way ICE signals the end of the gathering stage is to send
                // an empty candidate or "end-of-candidates" SDP attribute.
                logger.LogWarning($"ICE session omitting empty remote candidate.");
            }
            else if (candidate.component != Component)
            {
                // This occurs if the remote party made an offer and assumed we couldn't multiplex the audio and video streams.
                // It will offer the same ICE candidates separately for the audio and video announcements.
                logger.LogWarning($"ICE session omitting remote candidate with unsupported component: {candidate}.");
            }
            else if (candidate.protocol != RTCIceProtocol.udp)
            {
                // This implementation currently only supports UDP for RTP communications.
                logger.LogWarning($"ICE session omitting remote candidate with unsupported transport protocol: {candidate.protocol}.");
            }
            else if (candidate.address.Trim().ToLower().EndsWith(MDNS_TLD))
            {
                // Supporting MDNS lookups means an additional nuget dependency. Hopefully
                // support is coming to .Net Core soon (AC 12 Jun 2020).
                logger.LogWarning($"ICE session omitting remote candidate with unsupported MDNS hostname: {candidate.address}");
            }
            else if (IPAddress.TryParse(candidate.address, out var addr) &&
                (IPAddress.Any.Equals(addr) || IPAddress.IPv6Any.Equals(addr)))
            {
                logger.LogWarning($"ICE session omitting remote candidate with wildcard IP address: {candidate.address}");
            }
            else if (candidate.port <= 0 || candidate.port > IPEndPoint.MaxPort)
            {
                logger.LogWarning($"ICE session omitting remote candidate with invalid port: {candidate.port}");
            }
            else
            {
                // Have a remote candidate. Connectivity checks can start. Note because we support ICE trickle
                // we may also still be gathering candidates. Connectivity checks and gathering can be done in parallel.

                logger.LogDebug($"ICE session received remote candidate: {candidate}");

                _remoteCandidates.Add(candidate);
                _pendingRemoteCandidates.Enqueue(candidate);
            }
        }

        /// <summary>
        /// Restarts the ICE gathering and connection checks for this ICE session.
        /// </summary>
        public void Restart()
        {
            // Reset the session state.
            _processChecklistTimer?.Dispose();
            _processIceServersTimer?.Dispose();
            _candidates.Clear();
            _checklist.Clear();
            _iceServerConnections.Clear();
            GatheringState = RTCIceGatheringState.@new;
            ConnectionState = RTCIceConnectionState.@new;

            StartGathering();
        }

        /// <summary>
        /// Acquires an ICE candidate for each IP address that this host has except for:
        /// - Loopback addresses must not be included.
        /// - Deprecated IPv4-compatible IPv6 addresses and IPv6 site-local unicast addresses
        ///   must not be included,
        /// - IPv4-mapped IPv6 address should not be included.
        /// - If a non-location tracking IPv6 address is available use it and do not included 
        ///   location tracking enabled IPv6 addresses (i.e. prefer temporary IPv6 addresses over 
        ///   permanent addresses), see RFC6724.
        ///
        /// SECURITY NOTE: https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12#section-5.2
        /// Makes recommendations about how host IP address information should be exposed.
        /// Of particular relevance are:
        /// 
        ///   Mode 1:  Enumerate all addresses: WebRTC MUST use all network
        ///   interfaces to attempt communication with STUN servers, TURN
        ///   servers, or peers.This will converge on the best media
        ///   path, and is ideal when media performance is the highest
        ///   priority, but it discloses the most information.
        ///    
        ///   Mode 2:  Default route + associated local addresses: WebRTC MUST
        ///   follow the kernel routing table rules, which will typically
        ///   cause media packets to take the same route as the
        ///   application's HTTP traffic.  If an enterprise TURN server is
        ///   present, the preferred route MUST be through this TURN
        ///   server.Once an interface has been chosen, the private IPv4
        ///   and IPv6 addresses associated with this interface MUST be
        ///   discovered and provided to the application as host
        ///   candidates.This ensures that direct connections can still
        ///   be established in this mode.
        ///   
        /// This implementation implements Mode 2.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.1</remarks>
        /// <returns>A list of "host" ICE candidates for the local machine.</returns>
        private List<RTCIceCandidate> GetHostCandidates()
        {
            List<RTCIceCandidate> hostCandidates = new List<RTCIceCandidate>();
            RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

            IPAddress signallingDstAddress = _remoteSignallingAddress;

            // RFC8445 states that loopback addresses should not be included in
            // host candidates. If the provided signalling address is a loopback
            // address it means no host candidates will be gathered. To avoid this
            // set the desired interface address to the Internet facing address
            // in the event a loopback address was specified.
            if (signallingDstAddress != null &&
                (IPAddress.IsLoopback(signallingDstAddress) ||
                IPAddress.Any.Equals(signallingDstAddress) ||
                IPAddress.IPv6Any.Equals(signallingDstAddress)))
            {
                // By setting to null means the default Internet facing interface will be used.
                signallingDstAddress = null;
            }

            var rtpBindAddress = _rtpChannel.RTPLocalEndPoint.Address;

            // We get a list of local addresses that can be used with the address the RTP socket is bound on.
            List<IPAddress> localAddresses = null;
            if (IPAddress.IPv6Any.Equals(rtpBindAddress))
            {
                if (_rtpChannel.RtpSocket.DualMode)
                {
                    // IPv6 dual mode listening on [::] means we can use all valid local addresses.
                    localAddresses = NetServices.GetLocalAddressesOnInterface(signallingDstAddress)
                        .Where(x => !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal).ToList();
                }
                else
                {
                    // IPv6 but not dual mode on [::] means can use all valid local IPv6 addresses.
                    localAddresses = NetServices.GetLocalAddressesOnInterface(signallingDstAddress)
                        .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal).ToList();
                }
            }
            else if (IPAddress.Any.Equals(rtpBindAddress))
            {
                // IPv4 on 0.0.0.0 means can use all valid local IPv4 addresses.
                localAddresses = NetServices.GetLocalAddressesOnInterface(signallingDstAddress)
                    .Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x)).ToList();
            }
            else
            {
                // If not bound on a [::] or 0.0.0.0 means we're only listening on a specific IP address
                // and that's the only one that can be used for the host candidate.
                localAddresses = new List<IPAddress> { rtpBindAddress };
            }

            foreach (var localAddress in localAddresses)
            {
                var hostCandidate = new RTCIceCandidate(init);
                hostCandidate.SetAddressProperties(RTCIceProtocol.udp, localAddress, (ushort)_rtpChannel.RTPPort, RTCIceCandidateType.host, null, 0);

                // We currently only support a single multiplexed connection for all data streams and RTCP.
                if (hostCandidate.component == RTCIceComponent.rtp && hostCandidate.sdpMLineIndex == SDP_MLINE_INDEX)
                {
                    hostCandidates.Add(hostCandidate);

                    OnIceCandidate?.Invoke(hostCandidate);
                }
            }

            return hostCandidates;
        }

        /// <summary>
        /// Initialises the ICE servers if any were provided in the initial configuration.
        /// ICE servers are STUN and TURN servers and are used to gather "server reflexive"
        /// and "relay" candidates.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        private void InitialiseIceServers(List<RTCIceServer> iceServers)
        {
            _iceServerConnections = new ConcurrentDictionary<STUNUri, IceServerConnectionState>();

            // Add each of the ICE servers to the list. Ideally only one will be used but add 
            // all in case backups are needed.
            foreach (var iceServer in iceServers)
            {
                string[] urls = iceServer.urls.Split(',');

                foreach (string url in urls)
                {
                    if (!String.IsNullOrWhiteSpace(url))
                    {
                        if (STUNUri.TryParse(url, out var stunUri))
                        {
                            if (stunUri.Scheme == STUNSchemesEnum.stuns || stunUri.Scheme == STUNSchemesEnum.turns)
                            {
                                logger.LogWarning($"ICE session does not currently support TLS for STUN and TURN servers, not checking {stunUri}.");
                            }
                            else if (!_iceServerConnections.ContainsKey(stunUri))
                            {
                                logger.LogDebug($"Adding ICE server for {stunUri}.");

                                var iceServerState = new IceServerConnectionState(stunUri, iceServer.username, iceServer.credential);
                                _iceServerConnections.TryAdd(stunUri, iceServerState);
                            }
                        }
                        else
                        {
                            logger.LogWarning($"ICE session could not parse ICE server URL {url}.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks the list of ICE servers to perform STUN binding or TURN reservation requests.
        /// Only one of the ICE server entries should end up being used. If at least one TURN server
        /// is provided it will take precedence as it can supply Server Reflexive and Relay candidates.
        /// </summary>
        private void CheckIceServers(Object state)
        {
            // The lock is to ensure the timer callback doesn't run multiple instances in parallel. 
            if (Monitor.TryEnter(_iceServerConnections))
            {
                if (_activeIceServer == null || _activeIceServer.Error != SocketError.Success)
                {
                    if (_iceServerConnections.Count(x => x.Value.Error == SocketError.Success) == 0)
                    {
                        logger.LogDebug("ICE session all ICE server connection checks failed, stopping ICE servers timer.");
                        _processIceServersTimer.Dispose();
                    }
                    else
                    {
                        // Select the next server to check.
                        var entry = _iceServerConnections
                        .Where(x => x.Value.Error == SocketError.Success)
                        .OrderByDescending(x => x.Value._uri.Scheme) // TURN serves take priority.
                        .FirstOrDefault();

                        if (!entry.Equals(default(KeyValuePair<STUNUri, IceServerConnectionState>)))
                        {
                            _activeIceServer = entry.Value;
                        }
                        else
                        {
                            logger.LogDebug("ICE session was not able to set an active ICE server, stopping ICE servers timer.");
                            _processIceServersTimer.Dispose();
                        }
                    }
                }

                // Run a state machine on the active ICE server.

                // Something went wrong. An active server could not be set.
                if (_activeIceServer == null)
                {
                    logger.LogDebug("ICE session was not able to acquire an active ICE server, stopping ICE servers timer.");
                    _processIceServersTimer.Dispose();
                }
                // If the ICE server hasn't yet been resolved initiate the DNS check.
                else if (_activeIceServer.ServerEndPoint == null && _activeIceServer.DnsLookupSentAt == DateTime.MinValue)
                {
                    logger.LogDebug($"Attempting to resolve STUN server URI {_activeIceServer._uri}.");

                    _activeIceServer.DnsLookupSentAt = DateTime.Now;

                    // Don't stop and wait for DNS. Let the timer callback complete and check for the DNS
                    // result on the next few timer callbacks.
                    _ = STUNDns.Resolve(_activeIceServer._uri).ContinueWith(y =>
                    {
                        if (y.Result != null)
                        {
                            logger.LogDebug($"ICE server {_activeIceServer._uri} successfully resolved to {y.Result}.");
                            _activeIceServer.ServerEndPoint = y.Result;
                        }
                        else
                        {
                            logger.LogWarning($"Unable to resolve ICE server end point for {_activeIceServer._uri}.");
                            _activeIceServer.Error = SocketError.HostNotFound;
                        }
                    });
                }
                // Waiting for DNS lookup to complete.
                else if (_activeIceServer.ServerEndPoint == null &&
                    DateTime.Now.Subtract(_activeIceServer.DnsLookupSentAt).TotalSeconds < IceServerConnectionState.DNS_LOOKUP_TIMEOUT_SECONDS)
                {
                    // Do nothing.
                }
                // DNS lookup for ICE server host has timed out.
                else if (_activeIceServer.ServerEndPoint == null)
                {
                    logger.LogWarning($"ICE server DNS resolution failed for {_activeIceServer._uri}.");
                    _activeIceServer.Error = SocketError.TimedOut;
                }
                // Maximum number of requests have been sent to the ICE server without a response.
                else if (_activeIceServer.OutstandingRequestsSent >= IceServerConnectionState.MAX_REQUESTS && _activeIceServer.LastResponseReceivedAt == DateTime.MinValue)
                {
                    logger.LogWarning($"Connection attempt to ICE server {_activeIceServer._uri} timed out after {_activeIceServer.OutstandingRequestsSent} requests.");
                    _activeIceServer.Error = SocketError.TimedOut;
                }
                // Send STUN binding request.
                else if (_activeIceServer.ServerReflexiveEndPoint == null && _activeIceServer._uri.Scheme == STUNSchemesEnum.stun)
                {
                    logger.LogDebug($"Sending STUN binding request to ICE server {_activeIceServer._uri}.");
                    _activeIceServer.Error = SendStunBindingRequest(_activeIceServer);
                }
                // Send TURN binding request.
                else if (_activeIceServer.ServerReflexiveEndPoint == null && _activeIceServer._uri.Scheme == STUNSchemesEnum.turn)
                {
                    logger.LogDebug($"Sending TURN allocate request to ICE server {_activeIceServer._uri}.");
                    _activeIceServer.Error = SendTurnAllocateRequest(_activeIceServer);
                }
                // Periodically re-send the STUN binding request to keep the NAT mapping fresh. Note that if
                // the server reflexive candidate wasn't used the as the nominated candidate the periodic timer
                // calling this method will be cancelled.
                else if (_activeIceServer.ServerReflexiveEndPoint != null && _activeIceServer._uri.Scheme == STUNSchemesEnum.stun)
                {
                    int refreshDueIn = IceServerConnectionState.STUN_BINDING_REQUEST_REFRESH_SECONDS -
                        (int)DateTime.Now.Subtract(_activeIceServer.LastResponseReceivedAt).TotalSeconds;

                    if (refreshDueIn <= 0)
                    {
                        _activeIceServer.Error = SendStunBindingRequest(_activeIceServer);
                    }
                    else
                    {
                        // Set the timer to fire the callback when the next refresh request is due.
                        _processIceServersTimer.Change(refreshDueIn * 1000, Timeout.Infinite);
                    }
                }
                // Periodically send a TURN Refresh request to keep the relay from closing. Note that if
                // the relay candidate wasn't used the as the nominated candidate the periodic timer
                // calling this method will be cancelled.
                else if (_activeIceServer.ServerReflexiveEndPoint != null && _activeIceServer._uri.Scheme == STUNSchemesEnum.turn)
                {

                }
                else
                {
                    logger.LogWarning($"The active ICE server reached an unexpected state {_activeIceServer._uri}.");
                }

                Monitor.Exit(_iceServerConnections);
            }
        }

        /// <summary>
        /// Updates the checklist with new candidate pairs.
        /// </summary>
        /// <remarks>
        /// From https://tools.ietf.org/html/rfc8445#section-6.1.2.2:
        /// IPv6 link-local addresses MUST NOT be paired with other than link-local addresses.
        /// </remarks>
        private async Task UpdateChecklist(RTCIceCandidate remoteCandidate)
        {
            // Local server reflexive candidates don't get added to the checklist since they are just local
            // "host" candidates with an extra NAT address mapping. The NAT address mapping is needed for the
            // remote ICE peer but locally a server reflexive candidate is always going to be represented by
            // a "host" candidate.

            bool supportsIPv4 = _rtpChannel.RtpSocket.AddressFamily == AddressFamily.InterNetwork || _rtpChannel.IsDualMode;
            bool supportsIPv6 = _rtpChannel.RtpSocket.AddressFamily == AddressFamily.InterNetworkV6 || _rtpChannel.IsDualMode;

            if (!IPAddress.TryParse(remoteCandidate.address, out var remoteCandidateIPAddr))
            {
                // The candidate string can be a hostname or an IP address.
                var lookupResult = await _dnsLookupClient.QueryAsync(remoteCandidate.address, DnsClient.QueryType.A);

                if (lookupResult.Answers.Count > 0)
                {
                    remoteCandidateIPAddr = lookupResult.Answers.AddressRecords().FirstOrDefault()?.Address;
                    logger.LogDebug($"ICE session resolved remote candidate {remoteCandidate.address} to {remoteCandidateIPAddr}.");
                }
                else
                {
                    logger.LogDebug($"ICE session failed to resolve remote candidate {remoteCandidate.address}.");
                }
            }

            if (remoteCandidateIPAddr != null)
            {
                var remoteEP = new IPEndPoint(remoteCandidateIPAddr, remoteCandidate.port);
                remoteCandidate.SetDestinationEndPoint(remoteEP);

                lock (_checklist)
                {
                    if (remoteCandidateIPAddr.AddressFamily == AddressFamily.InterNetwork && supportsIPv4 ||
                        remoteCandidateIPAddr.AddressFamily == AddressFamily.InterNetworkV6 && supportsIPv6)
                    {
                        ChecklistEntry entry = new ChecklistEntry(_localChecklistCandidate, remoteCandidate, IsController);

                        // Because only ONE checklist is currently supported each candidate pair can be set to
                        // a "waiting" state. If an additional checklist is ever added then only one candidate
                        // pair with the same foundation should be set to waiting across all checklists.
                        // See https://tools.ietf.org/html/rfc8445#section-6.1.2.6 for a somewhat convoluted
                        // explanation and example.
                        entry.State = ChecklistEntryState.Waiting;

                        AddChecklistEntry(entry);
                    }

                    // Finally sort the checklist to put it in priority order and if necessary remove lower 
                    // priority pairs.
                    _checklist.Sort();

                    while (_checklist.Count > MAX_CHECKLIST_ENTRIES)
                    {
                        _checklist.RemoveAt(_checklist.Count - 1);
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to add a checklist entry. If there is already an equivalent entry in the checklist
        /// the entry may not be added or may replace an existing entry.
        /// </summary>
        /// <param name="entry">The new entry to attempt to add to the checklist.</param>
        private void AddChecklistEntry(ChecklistEntry entry)
        {
            // Check if there is already an entry that matches the remote candidate.
            // Note: The implementation in this class relies binding the socket used for all
            // local candidates on a SINGLE address (typically 0.0.0.0 or [::]). Consequently
            // there is no need to check the local candidate when determining duplicates. As long
            // as there is one checklist entry with each remote candidate the connectivity check will
            // work. To put it another way the local candidate information is not used on the 
            // "Nominated" pair.

            var entryRemoteEP = entry.RemoteCandidate.DestinationEndPoint;

            var existingEntry = _checklist.Where(x => x.RemoteCandidate.DestinationEndPoint != null
                && x.RemoteCandidate.DestinationEndPoint.Address.Equals(entryRemoteEP.Address)
                && x.RemoteCandidate.DestinationEndPoint.Port == entryRemoteEP.Port
                && x.RemoteCandidate.protocol == entry.RemoteCandidate.protocol).SingleOrDefault();

            if (existingEntry != null)
            {
                if (entry.Priority > existingEntry.Priority)
                {
                    logger.LogDebug($"Removing lower priority entry and adding candidate pair to checklist for: {entry.RemoteCandidate}");
                    _checklist.Remove(existingEntry);
                    _checklist.Add(entry);
                }
                else
                {
                    logger.LogDebug($"Existing checklist entry has higher priority, NOT adding entry for: {entry.RemoteCandidate}");
                }
            }
            else
            {
                // No existing entry.
                logger.LogDebug($"Adding new candidate pair to checklist for: {entry.RemoteCandidate}");
                _checklist.Add(entry);
            }
        }

        /// <summary>
        /// Processes the checklist and sends any required STUN requests to perform connectivity checks.
        /// </summary>
        /// <remarks>
        /// The scheduling mechanism for ICE is specified in https://tools.ietf.org/html/rfc8445#section-6.1.4.
        /// </remarks>
        private void ProcessChecklist(Object stateInfo)
        {
            while (_pendingRemoteCandidates.Count() > 0)
            {
                if (_pendingRemoteCandidates.TryDequeue(out var candidate))
                {
                    // The reason not to wait for this operation is that the ICE candidate can
                    // contain a hostname and require a DNS lookup. There's nothing that can be done
                    // if the DNS lookup fails so the initiate the task and then keep going with the
                    // adding any other pending candidates and move on with processing the check list.
                    _ = UpdateChecklist(candidate);
                }
            }

            if (ConnectionState == RTCIceConnectionState.checking)
            {
                if (_checklist.Count > 0)
                {
                    if (RemoteIceUser == null || RemoteIcePassword == null)
                    {
                        logger.LogWarning("ICE session checklist processing cannot occur as either the remote ICE user or password are not set.");
                        ConnectionState = RTCIceConnectionState.failed;
                    }
                    else
                    {
                        lock (_checklist)
                        {
                            // The checklist gets sorted into priority order whenever a remote candidate and its corresponding candidate pairs
                            // are added. At this point it can be relied upon that the checklist is correctly sorted by candidate pair priority.

                            // Do a check for any timed out entries.
                            var failedEntries = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                   && DateTime.Now.Subtract(x.LastCheckSentAt).TotalMilliseconds > RTO
                                   && x.ChecksSent >= N).ToList();

                            foreach (var failedEntry in failedEntries)
                            {
                                logger.LogDebug($"Checks for checklist entry have timed out, state being set to failed: {failedEntry.LocalCandidate} -> {failedEntry.RemoteCandidate}.");
                                failedEntry.State = ChecklistEntryState.Failed;
                            }

                            // Move on to checking for  checklist entries that need an initial check sent.
                            var nextEntry = _checklist.Where(x => x.State == ChecklistEntryState.Waiting).FirstOrDefault();

                            if (nextEntry != null)
                            {
                                SendConnectivityCheck(nextEntry, false);
                                return;
                            }

                            // No waiting entries so check for ones requiring a retransmit.
                            var retransmitEntry = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                && DateTime.Now.Subtract(x.LastCheckSentAt).TotalMilliseconds > RTO).FirstOrDefault();

                            if (retransmitEntry != null)
                            {
                                SendConnectivityCheck(retransmitEntry, false);
                                return;
                            }

                            // If this point is reached and all entries are in a failed state then the overall result 
                            // of the ICE check is a failure.
                            if (_checklist.All(x => x.State == ChecklistEntryState.Failed))
                            {
                                _processChecklistTimer.Dispose();
                                _checklistState = ChecklistState.Failed;
                                ConnectionState = RTCIceConnectionState.failed;
                                OnIceConnectionStateChange?.Invoke(ConnectionState);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets the nominated checklist entry. This action completes the checklist processing and 
        /// indicates the connection checks were successful.
        /// </summary>
        /// <param name="entry">The checklist entry that was nominated.</param>
        private void SetNominatedEntry(ChecklistEntry entry)
        {
            entry.Nominated = true;
            _checklistState = ChecklistState.Completed;
            NominatedCandidate = entry.RemoteCandidate;
            ConnectionState = RTCIceConnectionState.connected;
            OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
        }

        /// <summary>
        /// Performs a connectivity check for a single candidate pair entry.
        /// </summary>
        /// <param name="candidatePair">The candidate pair to perform a connectivity check for.</param>
        /// <param name="setUseCandidate">If true indicates we are acting as the "controlling" ICE agent
        /// and are nominating this candidate as the chosen one.</param>
        /// <remarks>As specified in https://tools.ietf.org/html/rfc8445#section-7.2.4.</remarks>
        private void SendConnectivityCheck(ChecklistEntry candidatePair, bool setUseCandidate)
        {
            candidatePair.State = ChecklistEntryState.InProgress;
            candidatePair.LastCheckSentAt = DateTime.Now;
            candidatePair.ChecksSent++;
            candidatePair.RequestTransactionID = Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH);

            IPEndPoint remoteEndPoint = candidatePair.RemoteCandidate.DestinationEndPoint;

            logger.LogDebug($"Sending ICE connectivity check from {_rtpChannel.RTPLocalEndPoint} to {remoteEndPoint} (use candidate {setUseCandidate}).");

            STUNMessage stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(candidatePair.RequestTransactionID);
            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + LocalIceUser);
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(candidatePair.Priority)));

            if (setUseCandidate)
            {
                stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.UseCandidate, null));
            }

            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

            _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunReqBytes);
        }

        /// <summary>
        /// Processes a received STUN request or response.
        /// </summary>
        /// <param name="stunMessage">The STUN message received.</param>
        /// <param name="remoteEndPoint">The remote end point the STUN packet was received from.</param>
        public void ProcessStunMessage(STUNMessage stunMessage, IPEndPoint remoteEndPoint)
        {
            remoteEndPoint = (!remoteEndPoint.Address.IsIPv4MappedToIPv6) ? remoteEndPoint : new IPEndPoint(remoteEndPoint.Address.MapToIPv4(), remoteEndPoint.Port);

            //logger.LogDebug($"STUN message received from remote {remoteEndPoint} {stunMessage.Header.MessageType}.");

            bool isForIceServerCheck = false;

            // Check if the  STUN message is for an ICE server check.
            if (_iceServerConnections != null)
            {
                string txID = Encoding.ASCII.GetString(stunMessage.Header.TransactionId);
                var iceServerConnection = GetIceServerConnection(txID);

                if (iceServerConnection != null)
                {
                    isForIceServerCheck = true;
                    ProcessStunResponseForIceServer(iceServerConnection, stunMessage, remoteEndPoint);
                }
            }

            // If the STUN message isn't for an ICE server then look for matching entries in the checklist.
            if (!isForIceServerCheck)
            {
                if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
                {
                    #region STUN Binding Requests.

                    // TODO: The integrity check method needs to be implemented (currently just returns true).
                    bool result = stunMessage.CheckIntegrity(System.Text.Encoding.UTF8.GetBytes(LocalIcePassword), LocalIceUser, RemoteIceUser);

                    if (!result)
                    {
                        // Send STUN error response.
                        STUNMessage stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse);
                        stunErrResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunErrResponse.ToByteBuffer(null, false));
                    }
                    else
                    {
                        var matchingCandidate = (_remoteCandidates != null) ? _remoteCandidates.Where(x => x.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint)).FirstOrDefault() : null;

                        if (matchingCandidate == null)
                        {
                            // This STUN request has come from a socket not in the remote ICE candidates list. 
                            // Add a new remote peer reflexive candidate.
                            RTCIceCandidate peerRflxCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
                            peerRflxCandidate.SetAddressProperties(RTCIceProtocol.udp, remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.prflx, null, 0);
                            peerRflxCandidate.SetDestinationEndPoint(remoteEndPoint);
                            logger.LogDebug($"Adding peer reflex ICE candidate for {remoteEndPoint}.");
                            _remoteCandidates.Add(peerRflxCandidate);

                            // Add a new entry to the check list for the new peer reflexive candidate.
                            ChecklistEntry entry = new ChecklistEntry(_localChecklistCandidate, peerRflxCandidate, IsController);
                            entry.State = ChecklistEntryState.Waiting;
                            AddChecklistEntry(entry);

                            matchingCandidate = peerRflxCandidate;
                        }

                        // Find the checklist entry for this remote candidate and update its status.
                        ChecklistEntry matchingChecklistEntry = null;

                        lock (_checklist)
                        {
                            matchingChecklistEntry = _checklist.Where(x => x.RemoteCandidate.foundation == matchingCandidate.foundation).FirstOrDefault();
                        }

                        if (matchingChecklistEntry == null)
                        {
                            logger.LogWarning("ICE session STUN request matched a remote candidate but NOT a checklist entry.");
                        }

                        // The UseCandidate attribute is only meant to be set by the "Controller" peer. This implementation
                        // will accept it irrespective of the peer roles. If the remote peer wants us to use a certain remote
                        // end point then so be it.
                        if (stunMessage.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate))
                        {
                            if (ConnectionState != RTCIceConnectionState.connected)
                            {
                                // If we are the "controlled" agent and get a "use candidate" attribute that sets the matching candidate as nominated 
                                // as per https://tools.ietf.org/html/rfc8445#section-7.3.1.5.

                                if (matchingChecklistEntry == null)
                                {
                                    logger.LogWarning("ICE session STUN request had UseCandidate set but no matching checklist entry was found.");
                                }
                                else
                                {
                                    logger.LogDebug($"ICE session remote peer nominated entry from binding request: {matchingChecklistEntry.RemoteCandidate}");
                                    SetNominatedEntry(matchingChecklistEntry);
                                }
                            }
                        }

                        STUNMessage stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
                        stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                        string localIcePassword = LocalIcePassword;
                        byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(localIcePassword, true);
                        _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespBytes);
                    }

                    #endregion
                }
                else if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
                {
                    #region STUN Binding Success Responses

                    // Correlate with request using transaction ID as per https://tools.ietf.org/html/rfc8445#section-7.2.5.

                    // Actions to take on a successful STUN response https://tools.ietf.org/html/rfc8445#section-7.2.5.3
                    // - Discover peer reflexive remote candidates
                    //   (TODO: According to https://tools.ietf.org/html/rfc8445#section-7.2.5.3.1 peer reflexive get added to the local candidates list?)
                    // - Construct a valid pair which means match a candidate pair in the check list and mark it as valid (since a successful STUN exchange 
                    //   has now taken place on it). A new entry may need to be created for this pair since peer reflexive candidates are not added to the connectivity
                    //   check checklist.
                    // - Update state of candidate pair that generated the check to Succeeded.
                    // - If the controlling candidate set the USE_CANDIDATE attribute then the ICE agent that receives the successful response sets the nominated
                    //   flag of the pair to true. Once the nominated flag is set it concludes the ICE processing for that component.

                    if (_checklistState == ChecklistState.Running)
                    {
                        string txID = Encoding.ASCII.GetString(stunMessage.Header.TransactionId);

                        // Attempt to find the checklist entry for this transaction ID.
                        ChecklistEntry matchingChecklistEntry = null;

                        lock (_checklist)
                        {
                            matchingChecklistEntry = _checklist.Where(x => x.RequestTransactionID == txID).FirstOrDefault();
                        }

                        if (matchingChecklistEntry == null)
                        {
                            logger.LogWarning("ICE session STUN response transaction ID did not match a checklist entry.");
                        }
                        else
                        {
                            matchingChecklistEntry.State = ChecklistEntryState.Succeeded;

                            if (matchingChecklistEntry.Nominated)
                            {
                                logger.LogDebug($"ICE session remote peer nominated entry from binding response: {matchingChecklistEntry.RemoteCandidate}");

                                // This is the response to a connectivity check that had the "UseCandidate" attribute set.
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                            else if (this.IsController && !_checklist.Any(x => x.Nominated))
                            {
                                // If we are the controlling ICE agent it's up to us to decide when to nominate a candidate pair to use for the connection.
                                // To start with we'll just use whichever pair gets the first successful STUN exchange. If needs be the selection algorithm can
                                // improve over time.

                                matchingChecklistEntry.ChecksSent = 0;
                                matchingChecklistEntry.LastCheckSentAt = DateTime.MinValue;
                                matchingChecklistEntry.Nominated = true;

                                SendConnectivityCheck(matchingChecklistEntry, true);
                            }
                        }
                    }

                    #endregion
                }
                else if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingErrorResponse)
                {
                    #region STUN Binding Error Responses

                    logger.LogWarning($"A STUN binding error response was received from {remoteEndPoint}.");

                    // Attempt to find the checklist entry for this transaction ID.
                    string txID = Encoding.ASCII.GetString(stunMessage.Header.TransactionId);

                    ChecklistEntry matchingChecklistEntry = null;

                    lock (_checklist)
                    {
                        _checklist.Where(x => x.RequestTransactionID == txID).FirstOrDefault();
                    }

                    if (matchingChecklistEntry == null)
                    {
                        logger.LogWarning("ICE session STUN error response transaction ID did not match a checklist entry.");
                    }
                    else
                    {
                        logger.LogWarning($"ICE session check list entry set to failed: {matchingChecklistEntry.RemoteCandidate}");
                        matchingChecklistEntry.State = ChecklistEntryState.Failed;
                    }

                    #endregion
                }
                else
                {
                    logger.LogWarning($"An unrecognised STUN request was received from {remoteEndPoint}.");
                }
            }
        }

        /// <summary>
        /// Checks a STUN response transaction ID to determine if it matches a check being carried
        /// out for an ICE server.
        /// </summary>
        /// <param name="transactionID">The transaction ID from the STUN response.</param>
        /// <returns>If found a matching state object or null if not.</returns>
        private IceServerConnectionState GetIceServerConnection(string transactionID)
        {
            var entry = _iceServerConnections
                       .Where(x => x.Value.TransactionID == transactionID)
                       .SingleOrDefault();

            if (!entry.Equals(default(KeyValuePair<STUNUri, IceServerConnectionState>)))
            {
                return entry.Value;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Processes a STUN response for an ICE server check.
        /// </summary>
        /// <param name="iceServerConnection">The ICE server connection the STUN response was generated for.</param>
        /// <param name="stunResponse">The STUN response received from the remote server.</param>
        /// <param name="remoteEndPoint">The remote end point the STUN response originated from.</param>
        private void ProcessStunResponseForIceServer(IceServerConnectionState iceServerConnection, STUNMessage stunResponse, IPEndPoint remoteEndPoint)
        {
            if (iceServerConnection == null)
            {
                throw new ArgumentNullException("iceServerConenction", "The ICE server connection parameter cannot be null.");
            }
            else if (stunResponse == null)
            {
                throw new ArgumentNullException("stunResponse", "The STUN response parameter cannot be null.");
            }

            // The STUN response is for a check sent to an ICE server.
            iceServerConnection.LastResponseReceivedAt = DateTime.Now;
            iceServerConnection.OutstandingRequestsSent = 0;

            if (stunResponse.Header.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
            {
                // If the server reflexive end point is set then this connection check has already been completed.
                if (iceServerConnection.ServerReflexiveEndPoint == null)
                {
                    logger.LogDebug($"STUN binding success response received for ICE server check to {iceServerConnection._uri}.");

                    var mappedAddr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                    if (mappedAddr != null)
                    {
                        var mappedIPAddr = (mappedAddr as STUNXORAddressAttribute).Address;
                        int mappedPort = (mappedAddr as STUNXORAddressAttribute).Port;

                        iceServerConnection.ServerReflexiveEndPoint = new IPEndPoint(mappedIPAddr, mappedPort);

                        RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };
                        RTCIceCandidate svrRflxCandidate = iceServerConnection.GetCandidate(init, RTCIceCandidateType.srflx,
                            NetServices.InternetDefaultAddress, (ushort)_rtpChannel.RTPPort);

                        if (svrRflxCandidate != null)
                        {
                            logger.LogDebug($"Adding server reflex ICE candidate for ICE server {iceServerConnection._uri}.");

                            // Note server reflexive candidates don't update the checklist pairs since it's merely an
                            // alternative way to represent an existing host candidate.
                            _candidates.Add(svrRflxCandidate);
                            OnIceCandidate?.Invoke(svrRflxCandidate);
                        }
                    }
                }
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.AllocateSuccessResponse)
            {
                // If the relay end point is set then this connection check has already been completed.
                if (iceServerConnection.RelayEndPoint == null)
                {
                    logger.LogDebug($"TURN allocate success response received for ICE server check to {iceServerConnection._uri}.");

                    var mappedAddr = stunResponse.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORMappedAddress).FirstOrDefault();

                    if (mappedAddr != null)
                    {
                        var mappedIPAddr = (mappedAddr as STUNXORAddressAttribute).Address;
                        int mappedPort = (mappedAddr as STUNXORAddressAttribute).Port;

                        iceServerConnection.ServerReflexiveEndPoint = new IPEndPoint(mappedIPAddr, mappedPort);

                        RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };
                        RTCIceCandidate svrRflxCandidate = iceServerConnection.GetCandidate(init, RTCIceCandidateType.srflx,
                            NetServices.InternetDefaultAddress, (ushort)_rtpChannel.RTPPort);

                        if (svrRflxCandidate != null)
                        {
                            logger.LogDebug($"Adding server reflex ICE candidate for ICE server {iceServerConnection._uri}.");

                            // Note server reflexive candidates don't update the checklist pairs since it's merely an
                            // alternative way to represent an existing host candidate.
                            _candidates.Add(svrRflxCandidate);
                            OnIceCandidate?.Invoke(svrRflxCandidate);
                        }
                    }

                    // TODO: Send the create channel request at this point?

                    // TODO: get the relay address and send the create permissions requests.

                }
            }
            else if (stunResponse.Header.MessageType == STUNMessageTypesEnum.BindingErrorResponse)
            {
                logger.LogWarning($"STUN binding error response received for ICE server check to {iceServerConnection._uri}.");
                // The STUN response is for a check sent to an ICE server.
                iceServerConnection.Error = SocketError.ConnectionRefused;
            }
            else
            {
                logger.LogWarning($"An unrecognised STUN message for an ICE server check was received from {remoteEndPoint}.");
            }
        }

        /// <summary>
        /// Sends a STUN binding request to an ICE server.
        /// </summary>
        /// <param name="iceServer">The ICE server to send the request to.</param>
        /// <returns>The result of the send attempt. Note this is the return code from the
        /// socket send call and not the result code from the STUN response.</returns>
        private SocketError SendStunBindingRequest(IceServerConnectionState iceServer)
        {
            iceServer.OutstandingRequestsSent += 1;
            iceServer.LastRequestSentAt = DateTime.Now;

            // Send a STUN binding request.
            STUNMessage stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
            byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

            var sendResult = _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, stunReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning($"Error sending STUN server binding request {iceServer.OutstandingRequestsSent} for " +
                    $"{iceServer._uri} to {iceServer.ServerEndPoint}. {sendResult}.");
            }

            return sendResult;
        }

        /// <summary>
        /// Sends an allocate request to a TURN server.
        /// </summary>
        /// <param name="iceServer">The TURN server to send the request to.</param>
        /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
        private SocketError SendTurnAllocateRequest(IceServerConnectionState iceServer)
        {
            iceServer.OutstandingRequestsSent += 1;
            iceServer.LastRequestSentAt = DateTime.Now;

            STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Allocate);
            allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
            allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, 3600));
            allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.RequestedTransport, STUNAttributeConstants.UdpTransportType));
            byte[] allocateReqBytes = allocateRequest.ToByteBuffer(null, false);

            var sendResult = _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning($"Error sending STUN server binding request {iceServer.OutstandingRequestsSent} for " +
                    $"{iceServer._uri} to {iceServer.ServerEndPoint}. {sendResult}.");
            }

            return sendResult;
        }

        /// <summary>
        /// Sends a permissions request to a TURN server for a peer end point.
        /// </summary>
        /// <param name="iceServer">The ICE server to send the permissions request to.</param>
        /// <param name="peerEndPoint">The peer end point to request the permissions for.</param>
        /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
        private SocketError SendTurnPermissionsRequest(IceServerConnectionState iceServer, IPEndPoint peerEndPoint)
        {
            // Send create permission request
            STUNMessage createPermissionRequest = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPermissionRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
            //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
            createPermissionRequest.Attributes.Add(new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORPeerAddress, peerEndPoint.Port, peerEndPoint.Address));
            createPermissionRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, Encoding.UTF8.GetBytes(iceServer._username)));
            //turnPermissionRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
            //turnPermissionRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

            //MD5 md5 = new MD5CryptoServiceProvider();
            //byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

            //byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
            byte[] createPermissionReqBytes = createPermissionRequest.ToByteBuffer(null, false);

            var sendResult = _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, createPermissionReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning($"Error sending STUN server binding request {iceServer.OutstandingRequestsSent} for " +
                    $"{iceServer._uri} to {iceServer.ServerEndPoint}. {sendResult}.");
            }

            return sendResult;
        }
    }
}
