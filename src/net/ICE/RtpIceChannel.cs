//-----------------------------------------------------------------------------
// Filename: RtpIceChannel.cs
//
// Description: Represents an RTP channel with ICE connectivity checks as 
// described in the Interactive Connectivity Establishment RFC8445
// https://tools.ietf.org/html/rfc8445.
//
// Remarks:
//
// Support for the following standards or proposed standards 
// is included:
//
// - "Trickle ICE" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-ice-trickle-21.
//
// - "WebRTC IP Address Handling Requirements" as per draft RFC 
//   https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12
//   SECURITY NOTE: See https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12#section-5.2
//   for recommendations on how a WebRTC application should expose a
//   hosts IP address information. This implementation is using Mode 2.
//
// - Session Traversal Utilities for NAT (STUN)
//   https://tools.ietf.org/html/rfc8553
//
// - Traversal Using Relays around NAT (TURN): Relay Extensions to 
//   Session Traversal Utilities for NAT (STUN)
//   https://tools.ietf.org/html/rfc5766
//
// - Using Multicast DNS to protect privacy when exposing ICE candidates
//   draft-ietf-rtcweb-mdns-ice-candidates-04 [ed. not implemented as of 26 Jul 2020].
//   https://tools.ietf.org/html/draft-ietf-rtcweb-mdns-ice-candidates-04
//
// - Multicast DNS
//   https://tools.ietf.org/html/rfc6762
//
// Notes:
// The source from Chromium that performs the equivalent of this class
// (and much more) is:
// https://chromium.googlesource.com/external/webrtc/+/refs/heads/master/p2p/base/p2p_transport_channel.cc
//
// Multicast DNS: Chromium (and possibly other WebRTC stacks) make use of *.local
// DNS hostnames (see Multicast RFC linked above). Support for such hostnames is 
// not supported directly in this library because there is no underlying support
// in .NET Core. A callback hook is available so that an application can connect
// up an MDNS resolver if required.
// Windows 10 has recently introduced a level of support for MDNS:
// https://docs.microsoft.com/en-us/uwp/api/windows.networking.servicediscovery.dnssd?view=winrt-19041
// From a command prompt: 
// c:\> dns-md -B
// c:\> dns-sd -G v4 fbba6380-2cc4-41b1-ab0d-61548dd28a29.local
// c:\> dns-sd -G v6 b1f949b8-5ec9-41a6-b3ef-eb529f217de9.local
// But it's expected that it's highly unlikely support will be added to .NET Core
// any time soon (AC 26 Jul 2020).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
// 23 Jun 2020  Aaron Clauson   Renamed from IceSession to RtpIceChannel.
// 03 Oct 2022  Rafal Soares	Add support to TCP IceServer
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// An RTP ICE Channel carries out connectivity checks with a remote peer in an
/// attempt to determine the best destination end point to communicate with the
/// remote party.
/// </summary>
/// <remarks>
/// Local server reflexive candidates don't get added to the checklist since they are just local
/// "host" candidates with an extra NAT address mapping. The NAT address mapping is needed for the
/// remote ICE peer but locally a server reflexive candidate is always going to be represented by
/// a "host" candidate.
/// 
/// Limitations:
///  - To reduce complexity only a single checklist is used. This is based on the main 
///    webrtc use case where RTP (audio and video) and RTCP are all multiplexed on a 
///    single socket pair. Therefore  there only needs to be a single component and single 
///    data stream. If an additional use case occurs then multiple checklists could be added.
///    
/// Developer Notes:
/// There are 4 main tasks occurring during the ICE checks:
/// - Local candidates: ICE server checks (which can take seconds) are being carried out to
///   gather "server reflexive" and "relay" candidates.
/// - Remote candidates: the remote peer should be trickling in its candidates which need to
///   be validated and if accepted new entries added to the checklist.
/// - Checklist connectivity checks: the candidate pairs in the checklist need to have
///   connectivity checks sent.
/// - Match STUN messages: STUN requests and responses are being received and need to be 
///   matched to either an ICE server check or a checklist entry check. After matching 
///   action needs to be taken to update the status of the ICE server or checklist entry
///   check.
/// </remarks>
public partial class RtpIceChannel : RTPChannel
{
    private const int ICE_UFRAG_LENGTH = 4;
    private const int ICE_PASSWORD_LENGTH = 24;
    private const int MAX_CHECKLIST_ENTRIES = 25;       // Maximum number of entries that can be added to the checklist of candidate pairs.
    private const string MDNS_TLD = ".local";           // Top Level Domain name for multicast lookups as per RFC6762.
    private const int CONNECTED_CHECK_PERIOD = 3;       // The period in seconds to send STUN connectivity checks once connected. 
    public const string SDP_MID = "0";
    public const int SDP_MLINE_INDEX = 0;

    private static DnsClient.LookupClient? _dnsLookupClient;

    public static List<DnsClient.NameServer>? DefaultNameServers { get; set; }

    /// <summary>
    /// ICE transaction spacing interval in milliseconds.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8445#section-14.
    /// </remarks>
    private const int Ta = 50;

    private static readonly ILogger logger = Log.Logger;

    /// <summary>
    /// The period in seconds after which a connection will be flagged as disconnected.
    /// </summary>
    public static int DISCONNECTED_TIMEOUT_PERIOD = 8;

    /// <summary>
    /// The period in seconds after which a connection will be flagged as failed.
    /// </summary>
    public static int FAILED_TIMEOUT_PERIOD = 16;

    /// <summary>
    /// The period in seconds after which a CreatePermission will be sent.
    /// </summary>
    public static int REFRESH_PERMISSION_PERIOD = 240;

    /// <summary>
    /// The lifetime value used in refresh request.
    /// </summary>
    public static uint ALLOCATION_TIME_TO_EXPIRY_VALUE = 600;

    private readonly IPAddress? _bindAddress;
    private readonly RTCIceServer[] _iceServers;
    private readonly RTCIceTransportPolicy _policy;

    private DateTime _startedGatheringAt = DateTime.MinValue;
    private DateTime _connectedAt = DateTime.MinValue;

    internal IceServerResolver _iceServerResolver = new IceServerResolver();

    private IceServer? _activeIceServer;

    public RTCIceComponent Component { get; private set; }

    public RTCIceGatheringState IceGatheringState { get; private set; } = RTCIceGatheringState.@new;

    public RTCIceConnectionState IceConnectionState { get; private set; } = RTCIceConnectionState.@new;

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
            return new List<RTCIceCandidate>(_candidates);
        }
    }

    private ConcurrentBag<RTCIceCandidate> _candidates = new ConcurrentBag<RTCIceCandidate>();
    internal ConcurrentBag<RTCIceCandidate> _remoteCandidates = new ConcurrentBag<RTCIceCandidate>();

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
    /// Lock to co-ordinate access to the _checklist.
    /// </summary>
    private readonly object _checklistLock = new object();

    /// <summary>
    /// For local candidates this implementation takes a shortcut to reduce complexity. 
    /// The RTP socket will always be bound to one of:
    ///  - IPAddress.IPv6Any [::], 
    ///  - IPAddress.Any 0.0.0.0, or,
    ///  - a specific single IP address. 
    /// As such it's only necessary to create a single checklist entry to cover all local
    /// Host type candidates.
    /// Host candidates must still be generated, based on all local IP addresses, and
    /// will need to be transmitted to the remote peer but they don't need to
    /// be used when populating the checklist.
    /// </summary>
    internal readonly RTCIceCandidate _localChecklistCandidate;

    /// <summary>
    /// If a TURN server is being used for this session and has received a successful
    /// response to the allocate request then this field will hold the candidate to
    /// use in the checklist.
    /// </summary>
    internal RTCIceCandidate? _relayChecklistCandidate;

    /// <summary>
    /// If the connectivity checks are successful this will hold the entry that was 
    /// nominated by the connection check process.
    /// </summary>
    public ChecklistEntry? NominatedEntry { get; private set; }

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
            if (IceGatheringState == RTCIceGatheringState.gathering)
            {
                var rto = 500;
                foreach (var candidate in Candidates)
                {
                    if (candidate.type is RTCIceCandidateType.srflx or RTCIceCandidateType.relay)
                    {
                        rto += Ta;
                    }
                }
                return rto;
            }
            else
            {
                lock (_checklistLock)
                {
                    var rto = 500;
                    foreach (var entry in _checklist)
                    {
                        if (entry.State is ChecklistEntryState.Waiting or ChecklistEntryState.InProgress)
                        {
                            rto += Ta;
                        }
                    }
                    return rto;
                }
            }
        }
    }

    public readonly string LocalIceUser;
    public readonly string LocalIcePassword;
    public string? RemoteIceUser { get; private set; }
    public string? RemoteIcePassword { get; private set; }

    private bool _closed;
    private Timer? _connectivityChecksTimer;
    private Timer? _processIceServersTimer;
    private Timer? _refreshTurnTimer;
    private DateTime _checklistStartedAt = DateTime.MinValue;
    private bool _includeAllInterfaceAddresses;
    private ulong _iceTiebreaker;

    public event Action<RTCIceCandidate>? OnIceCandidate;
    public event Action<RTCIceConnectionState>? OnIceConnectionStateChange;
    public event Action<RTCIceGatheringState>? OnIceGatheringStateChange;
    public event Action<RTCIceCandidate?, string>? OnIceCandidateError;

    /// <summary>
    /// This event gets fired when a STUN message is sent by this channel.
    /// The event is for diagnostic purposes only.
    /// Parameters:
    ///  - STUNMessage: The STUN message that was sent.
    ///  - IPEndPoint: The remote end point the STUN message was sent to.
    ///  - bool: True if the message was sent via a TURN server relay.
    /// </summary>
    public event Action<STUNMessage, IPEndPoint, bool>? OnStunMessageSent;

    /// <summary>
    /// An optional callback function to resolve remote ICE candidates with MDNS hostnames.
    /// </summary>
    /// <remarks>
    /// The order is <see cref="MdnsGetAddresses"/>, then <see cref="MdnsResolve"/>.
    /// If both are null system <see cref="Dns">DNS resolver</see> will be used.
    /// </remarks>
    public Func<string, Task<IPAddress>>? MdnsResolve;

    /// <summary>
    /// An optional callback function to resolve remote ICE candidates with MDNS hostnames.
    /// </summary>
    /// <remarks>
    /// The order is <see cref="MdnsGetAddresses"/>, then <see cref="MdnsResolve"/>.
    /// If both are null system <see cref="Dns">DNS resolver</see> will be used.
    /// </remarks>
    public Func<string, Task<IPAddress[]>>? MdnsGetAddresses;

    private readonly FrozenDictionary<STUNUri, SocketConnection> m_iceServerConnections;

    private bool m_tcpRtpReceiverStarted;

    /// <summary>
    /// Creates a new instance of an RTP ICE channel to provide RTP channel functions 
    /// with ICE connectivity checks.
    /// </summary>
    public RtpIceChannel() :
        this(null, RTCIceComponent.rtp)
    { }

    /// <summary>
    /// Creates a new instance of an RTP ICE channel to provide RTP channel functions 
    /// with ICE connectivity checks.
    /// </summary>
    /// <param name="bindAddress"> Optional. If this is not set then the default is to 
    /// bind to the IPv6 wildcard address in dual mode to the IPv4 wildcard address if
    /// IPv6 is not available.</param>
    /// <param name="component">The component (RTP or RTCP) the channel is being used for. Note
    /// for cases where RTP and RTCP are multiplexed the component is set to RTP.</param>
    /// <param name="iceServers">A list of STUN or TURN servers that can be used by this ICE agent.</param>
    /// <param name="policy">Determines which ICE candidates can be used in this RTP ICE Channel.</param>
    /// <param name="includeAllInterfaceAddresses">If set to true then IP addresses from ALL local  
    /// interfaces will be used for host ICE candidates. If left as the default false value host 
    /// candidates will be restricted to the single interface that the OS routing table matches to
    /// the destination address or the Internet facing interface if the destination is not known.
    /// The restrictive behaviour is as per the recommendation at:
    /// https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12#section-5.2.
    /// </param>
    public RtpIceChannel(
        IPAddress? bindAddress,
        RTCIceComponent component,
        IEnumerable<RTCIceServer>? iceServers = null,
        RTCIceTransportPolicy policy = RTCIceTransportPolicy.all,
        bool includeAllInterfaceAddresses = false,
        int bindPort = 0,
        PortRange? rtpPortRange = null) :
        base(false, bindAddress, bindPort, rtpPortRange)
    {
        _bindAddress = bindAddress;
        Component = component;
        _iceServers = iceServers is null ? Array.Empty<RTCIceServer>() : System.Linq.Enumerable.ToArray(iceServers);
        _policy = policy;
        _includeAllInterfaceAddresses = includeAllInterfaceAddresses;
        _iceTiebreaker = Crypto.GetRandomULong();

        LocalIceUser = Crypto.GetRandomString(ICE_UFRAG_LENGTH);
        LocalIcePassword = Crypto.GetRandomString(ICE_PASSWORD_LENGTH);

        base.OnStunMessageReceived += (stunMessage, remoteEndPoint, wasRelayed) =>
        {
            _ = ProcessStunMessage(stunMessage, remoteEndPoint, wasRelayed);
        };

        _localChecklistCandidate = new RTCIceCandidate(new RTCIceCandidateInit
        {
            sdpMid = SDP_MID,
            sdpMLineIndex = SDP_MLINE_INDEX,
            usernameFragment = LocalIceUser
        });

        _localChecklistCandidate.SetAddressProperties(
            RTCIceProtocol.udp,
            base.RTPLocalEndPoint.Address,
            (ushort)base.RTPLocalEndPoint.Port,
            RTCIceCandidateType.host,
            null,
            0);

        if (_iceServers is { Length: > 0 })
        {
            var iceServerConnections = new Dictionary<STUNUri, SocketConnection>();

            _iceServerResolver.InitialiseIceServers(_iceServers, _policy);

            var resolvedIceServers = _iceServerResolver.IceServers;

            foreach (var (uri, iceServer) in resolvedIceServers)
            {
                switch (iceServer.Protocol)
                {
                    case ProtocolType.Udp:
                        iceServerConnections[uri] = RtpConnection;
                        break;
                    case ProtocolType.Tcp when iceServer.Uri.Scheme is STUNSchemesEnum.turn:
                        {
                            NetServices.CreateRtpSocket(
                                false,
                                ProtocolType.Tcp,
                                bindAddress,
                                bindPort,
                                rtpPortRange,
                                true,
                                true,
                                out var rtpTcpSocket,
                                out _);

                            Debug.Assert(rtpTcpSocket is { });
                            var iceServerConnection = new SocketTcpConnection(rtpTcpSocket);
                            iceServerConnection.OnPacketReceived += OnRTPPacketReceived;
                            iceServerConnection.OnClosed += reason => CloseIceServerTcpConnection(iceServerConnection, reason);
                            iceServerConnections[uri] = iceServerConnection;
                            break;
                        }

                    case ProtocolType.Tcp when iceServer.Uri.Scheme is STUNSchemesEnum.turns:
                        {
                            NetServices.CreateRtpSocket(
                                false,
                                ProtocolType.Tcp,
                                bindAddress,
                                bindPort,
                                rtpPortRange,
                                true,
                                true,
                                out var rtpTcpSocket,
                                out _);

                            Debug.Assert(rtpTcpSocket is { });
                            var iceServerConnection = new SocketTlsConnection(rtpTcpSocket, iceServer.Uri.Host, iceServer.SslClientAuthenticationOptions);
                            iceServerConnection.OnPacketReceived += OnRTPPacketReceived;
                            iceServerConnection.OnClosed += reason => CloseIceServerTcpConnection(iceServerConnection, reason);
                            iceServerConnections[uri] = iceServerConnection;
                            break;
                        }
                }
            }

            m_iceServerConnections = iceServerConnections.ToFrozenDictionary();
        }
        else
        {
            m_iceServerConnections = FrozenDictionary<STUNUri, SocketConnection>.Empty;
        }

        if (DefaultNameServers is { })
        {
            _dnsLookupClient = new DnsClient.LookupClient(DefaultNameServers.ToArray());
        }
        else
        {
            _dnsLookupClient = new DnsClient.LookupClient();
        }
    }

    /// <summary>
    /// We've been given the green light to start the ICE candidate gathering process.
    /// This could include contacting external STUN and TURN servers. Events will 
    /// be fired as each ICE is identified and as the gathering state machine changes
    /// state.
    /// </summary>
    public void StartGathering()
    {
        if (!_closed && IceGatheringState == RTCIceGatheringState.@new)
        {
            _startedGatheringAt = DateTime.Now;

            // Start listening on the UDP socket.
            base.Start();
            StartTcpRtpReceiver();

            IceGatheringState = RTCIceGatheringState.gathering;
            OnIceGatheringStateChange?.Invoke(IceGatheringState);

            if (_policy == RTCIceTransportPolicy.all)
            {
                _candidates = new ConcurrentBag<RTCIceCandidate>();
                foreach (var iceCandidate in GetHostCandidates())
                {
                    _candidates.Add(iceCandidate);
                }
            }

            logger.LogIceChannelLocalCandidates(_candidates.Count);

            if (_iceServerResolver.IceServers is { Count: > 0 })
            {
                _processIceServersTimer = new Timer(CheckIceServers, null, Timeout.Infinite, Timeout.Infinite);
                _processIceServersTimer.Change(0, Ta);
            }
            else
            {
                // If there are no ICE servers then gathering has finished.
                IceGatheringState = RTCIceGatheringState.complete;
                OnIceGatheringStateChange?.Invoke(IceGatheringState);
            }

            _connectivityChecksTimer = new Timer(DoConnectivityCheck, null, Timeout.Infinite, Timeout.Infinite);
            _connectivityChecksTimer.Change(0, Ta);
        }
    }

    protected void StartTcpRtpReceiver()
    {
        if (!m_tcpRtpReceiverStarted)
        {
            m_tcpRtpReceiverStarted = true;

            foreach (var (uri, iceServerConnection) in m_iceServerConnections)
            {
                //iceServerConnection.OnClosed += reason => CloseIceServerConnection(receiver, reason);
                iceServerConnection.BeginReceiveFrom();
            }

            Debug.Assert(RtpConnection is { });

            logger.LogTcpStarted(RtpConnection.Socket.LocalEndPoint);

            OnClosed -= CloseIceServerTcpConnections;
            OnClosed += CloseIceServerTcpConnections;
        }
    }

    protected void CloseIceServerTcpConnections(string reason)
    {
        foreach (var (uri, iceServerConnection) in m_iceServerConnections)
        {
            if (iceServerConnection is SocketTcpConnection iceServerTcpConnection)
            {
                CloseIceServerTcpConnection(iceServerTcpConnection, reason);
            }
        }
    }

    protected internal void CloseIceServerTcpConnection(SocketTcpConnection target, string? reason)
    {
        try
        {
            target.Close(reason);
        }
        catch (Exception excp)
        {
            logger.LogTcpError(excp.Message, excp);
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
        logger.LogRemoteCredentialsSet();

        RemoteIceUser = username;
        RemoteIcePassword = password;

        if (IceConnectionState == RTCIceConnectionState.@new)
        {
            // A potential race condition exists here. The remote peer can send a binding request that
            // results in the ICE channel connecting BEFORE the remote credentials get set. Since the goal
            // is to connect ICE as quickly as possible it does not seem sensible to force a wait for the
            // remote credentials to be set. The credentials will still be used on STUN binding requests
            // sent on the connected ICE channel. In the case of WebRTC transport confidentiality is still
            // preserved since the DTLS negotiation will sill need to check the certificate fingerprint in
            // supplied by the remote offer.

            _checklistStartedAt = DateTime.Now;

            // Once the remote party's ICE credentials are known connection checking can 
            // commence immediately as candidates trickle in.
            IceConnectionState = RTCIceConnectionState.checking;
            OnIceConnectionStateChange?.Invoke(IceConnectionState);
        }
    }

    /// <summary>
    /// Closes the RTP ICE Channel and stops any further connectivity checks.
    /// </summary>
    public void Close()
    {
        if (!_closed)
        {
            logger.LogIceClosed(base.RTPLocalEndPoint);
            _closed = true;
            _connectivityChecksTimer?.Dispose();
            _processIceServersTimer?.Dispose();
            _refreshTurnTimer?.Dispose();
        }
    }

    /// <summary>
    /// Adds a remote ICE candidate to the RTP ICE Channel.
    /// </summary>
    /// <param name="candidate">An ICE candidate from the remote party.</param>
    public void AddRemoteCandidate(RTCIceCandidate candidate)
    {
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.address))
        {
            // Note that the way ICE signals the end of the gathering stage is to send
            // an empty candidate or "end-of-candidates" SDP attribute.
            OnIceCandidateError?.Invoke(candidate, "Remote ICE candidate was empty.");
        }
        else if (candidate.component != Component)
        {
            // This occurs if the remote party made an offer and assumed we couldn't multiplex the audio and video streams.
            // It will offer the same ICE candidates separately for the audio and video announcements.
            OnIceCandidateError?.Invoke(candidate, "Remote ICE candidate has unsupported component.");
        }
        else if (candidate.sdpMLineIndex != 0)
        {
            // This implementation currently only supports audio and video multiplexed on a single channel.
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate only supports multiplexed media, excluding remote candidate with non-zero sdpMLineIndex of {candidate.sdpMLineIndex}.");
        }
        else if (candidate.protocol != RTCIceProtocol.udp)
        {
            // This implementation currently only supports UDP for RTP communications.
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate has an unsupported transport protocol {candidate.protocol}.");
        }
        else if (IPAddress.TryParse(candidate.address, out var addr) &&
            (IPAddress.Any.Equals(addr) || IPAddress.IPv6Any.Equals(addr)))
        {
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate had a wildcard IP address {candidate.address}.");
        }
        else if (candidate.port is <= 0 or > IPEndPoint.MaxPort)
        {
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate had an invalid port {candidate.port}.");
        }
        else if (IPAddress.TryParse(candidate.address, out var addrIPv6) &&
                addrIPv6.AddressFamily == AddressFamily.InterNetworkV6 &&
                !Socket.OSSupportsIPv6 &&
                NetServices.HasActiveIPv6Address())
        {
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate was for IPv6 but OS does not support {candidate.address}.");
        }
        else
        {
            // Have a remote candidate. Connectivity checks can start. Note because we support ICE trickle
            // we may also still be gathering candidates. Connectivity checks and gathering can be done in parallel.

            logger.LogRemoteCandidate(candidate);

            _remoteCandidates.Add(candidate);
            _pendingRemoteCandidates.Enqueue(candidate);
        }
    }

    /// <summary>
    /// Restarts the ICE gathering and connection checks for this RTP ICE Channel.
    /// </summary>
    public void Restart()
    {
        // Reset the session state.
        _connectivityChecksTimer?.Dispose();
        _processIceServersTimer?.Dispose();
        _refreshTurnTimer?.Dispose();
        _candidates = new ConcurrentBag<RTCIceCandidate>();
        lock (_checklistLock)
        {
            _checklist?.Clear();
        }
        _iceServerResolver.InitialiseIceServers(_iceServers, _policy);
        IceGatheringState = RTCIceGatheringState.@new;
        IceConnectionState = RTCIceConnectionState.@new;

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
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8445#section-5.1.1.1
    /// See https://tools.ietf.org/html/rfc6874 for a recommendation on how scope or zone ID's
    /// should be represented as strings in IPv6 link local addresses. Due to parsing
    /// issues in at least two other WebRTC stacks (as of Feb 2021) any zone ID is removed
    /// from an ICE candidate string.
    /// </remarks>
    /// <returns>A list of "host" ICE candidates for the local machine.</returns>
    private List<RTCIceCandidate> GetHostCandidates()
    {
        var hostCandidates = new List<RTCIceCandidate>();
        var init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

        // RFC8445 states that loopback addresses should not be included in
        // host candidates. If the provided bind address is a loopback
        // address it means no host candidates will be gathered. To avoid this
        // set the desired interface address to the Internet facing address
        // in the event a loopback address was specified.

        var rtpBindAddress = base.RTPLocalEndPoint.Address;

        Debug.Assert(RtpConnection is { });

        // We get a list of local addresses that can be used with the address the RTP socket is bound on.
        IEnumerable<IPAddress> localAddresses;
        if (IPAddress.IPv6Any.Equals(rtpBindAddress))
        {
            if (RtpConnection.Socket.DualMode)
            {
                // IPv6 dual mode listening on [::] means we can use all valid local addresses.
                var list = new List<IPAddress>();
                foreach (var x in NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses))
                {
                    if (!IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal && !x.IsIPv6LinkLocal)
                    {
                        list.Add(x);
                    }
                }
                localAddresses = list;
            }
            else
            {
                // IPv6 but not dual mode on [::] means can use all valid local IPv6 addresses.
                var list = new List<IPAddress>();
                foreach (var x in NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses))
                {
                    if (x.AddressFamily == AddressFamily.InterNetworkV6 && !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal && !x.IsIPv6LinkLocal)
                    {
                        list.Add(x);
                    }
                }
                localAddresses = list;
            }
        }
        else if (IPAddress.Any.Equals(rtpBindAddress))
        {
            // IPv4 on 0.0.0.0 means can use all valid local IPv4 addresses.
            var list = new List<IPAddress>();
            foreach (var x in NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses))
            {
                if (x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
                {
                    list.Add(x);
                }
            }
            localAddresses = list;
        }
        else
        {
            // If not bound on a [::] or 0.0.0.0 means we're only listening on a specific IP address
            // and that's the only one that can be used for the host candidate.
            localAddresses = new IPAddress[] { rtpBindAddress };
        }

        foreach (var localAddress in localAddresses)
        {
            var hostCandidate = new RTCIceCandidate(init);
            hostCandidate.SetAddressProperties(RTCIceProtocol.udp, localAddress, (ushort)base.RTPPort, RTCIceCandidateType.host, null, 0);

            // We currently only support a single multiplexed connection for all data streams and RTCP.
            if (hostCandidate.component == RTCIceComponent.rtp && hostCandidate.sdpMLineIndex == SDP_MLINE_INDEX)
            {
                hostCandidates.Add(hostCandidate);

                OnIceCandidate?.Invoke(hostCandidate);
            }
        }

        return hostCandidates;
    }

    private void RefreshTurn(object? state)
    {
        try
        {
            if (_closed)
            {
                return;
            }

            if (NominatedEntry is null || _activeIceServer is null)
            {
                return;
            }
            if (_activeIceServer.Uri.Scheme != STUNSchemesEnum.turn || NominatedEntry.LocalCandidate.IceServer is null)
            {
                _refreshTurnTimer?.Dispose();
                _refreshTurnTimer = new Timer(RefreshTurn, null, Timeout.Infinite, Timeout.Infinite);
                _refreshTurnTimer.Change(0, 2000);
                return;
            }
            if (_activeIceServer.TurnTimeToExpiry.Subtract(DateTime.Now) <= TimeSpan.FromMinutes(1))
            {
                logger.LogIceTurnRefreshRequest(_activeIceServer.Uri);
                _activeIceServer.Error = SendTurnRefreshRequest(_activeIceServer);
            }

            if (NominatedEntry.TurnPermissionsRequestSent >= IceServer.MAX_REQUESTS)
            {
                logger.LogIceTurnPermissionsFailed(NominatedEntry.LocalCandidate.IceServer.Uri, NominatedEntry.TurnPermissionsRequestSent);
            }
            else if (NominatedEntry.TurnPermissionsRequestSent != 1 || NominatedEntry.TurnPermissionsResponseAt == DateTime.MinValue || DateTime.Now.Subtract(NominatedEntry.TurnPermissionsResponseAt).TotalSeconds >
                     REFRESH_PERMISSION_PERIOD)
            {
                // Send Create Permissions request to TURN server for remote candidate.
                var turnPermissionsRequestSent = ++NominatedEntry.TurnPermissionsRequestSent;
                var iceServer = NominatedEntry.LocalCandidate.IceServer;
                var destinationEndPoint = NominatedEntry.RemoteCandidate.DestinationEndPoint;
                var requestTransactionID = NominatedEntry.RequestTransactionID;
                Debug.Assert(iceServer is { });
                Debug.Assert(destinationEndPoint is { });
                Debug.Assert(requestTransactionID is { });
                logger.LogIceTurnPermissionsRequest(turnPermissionsRequestSent, iceServer.Uri, destinationEndPoint, requestTransactionID);
                SendTurnCreatePermissionsRequest(requestTransactionID, iceServer, destinationEndPoint);
            }
        }
        catch (Exception excp)
        {
            logger.LogRefreshError(excp.Message, excp);
        }
    }

    /// <summary>
    /// Checks the list of ICE servers to perform STUN binding or TURN reservation requests.
    /// Only one of the ICE server entries should end up being used. If at least one TURN server
    /// is provided it will take precedence as it can potentially supply both Server Reflexive 
    /// and Relay candidates.
    /// </summary>
    private void CheckIceServers(object? state)
    {
        if (_closed || IceGatheringState == RTCIceGatheringState.complete ||
            !(IceConnectionState == RTCIceConnectionState.@new || IceConnectionState == RTCIceConnectionState.checking))
        {
            logger.LogIceStopsProcessing(IceGatheringState, IceConnectionState);
            _refreshTurnTimer?.Dispose();
            _refreshTurnTimer = new Timer(RefreshTurn, null, Timeout.Infinite, Timeout.Infinite);
            _refreshTurnTimer.Change(0, 2000);

            Debug.Assert(_processIceServersTimer is { });
            _processIceServersTimer.Dispose();
            return;
        }

        // The lock is to ensure the timer callback doesn't run multiple instances in parallel. 
        if (Monitor.TryEnter(_iceServerResolver))
        {
            try
            {
                if (_activeIceServer is null || _activeIceServer.Error != SocketError.Success)
                {
                    // Select the next server to check.

                    foreach (var (uri, iceServer) in _iceServerResolver.IceServers)
                    {
                        if (iceServer.Error != SocketError.Success)
                        {
                            continue;
                        }

                        var iceServerScheme = iceServer.Uri.Scheme;

                        if (iceServerScheme == STUNSchemesEnum.turns)
                        {
                            _activeIceServer = iceServer;
                            break;
                        }

                        if (iceServerScheme == STUNSchemesEnum.turn || _activeIceServer is null)
                        {
                            _activeIceServer = iceServer;
                        }
                    }

                    if (_activeIceServer is null)
                    {
                        // no server found.

                        logger.LogIceServersChecksFailed();
                        Debug.Assert(_processIceServersTimer is { });
                        _processIceServersTimer.Dispose();
                    }
                }

                // Run a state machine on the active ICE server.

                // Something went wrong. An active server could not be set.
                if (_activeIceServer is null)
                {
                    logger.LogIceServerNotAcquired();
                    Debug.Assert(_processIceServersTimer is { });
                    _processIceServersTimer.Dispose();
                    return;
                }

                var activeIceServer = _activeIceServer;
                var activeIceServerUri = activeIceServer.Uri;

                if (activeIceServerUri.Scheme is STUNSchemesEnum.turn or STUNSchemesEnum.turns && activeIceServer.RelayEndPoint is { } ||
                    activeIceServerUri.Scheme is STUNSchemesEnum.stun && activeIceServer.ServerReflexiveEndPoint is { })
                {
                    // Successfully set up the ICE server. Do nothing.
                }
                // If the ICE server hasn't yet been resolved skip and retyr again when teh next ICE server checnk runs.
                if (activeIceServer.ServerEndPoint is null &&
                    DateTime.Now.Subtract(activeIceServer.DnsLookupSentAt).TotalSeconds < IceServer.DNS_LOOKUP_TIMEOUT_SECONDS)
                {
                    // Do nothing.
                }
                // DNS lookup for ICE server host has timed out.
                else if (activeIceServer.ServerEndPoint is null)
                {
                    logger.LogIceServerDnsResolutionFailed(activeIceServerUri);
                    activeIceServer.Error = SocketError.TimedOut;
                }
                // Maximum number of requests have been sent to the ICE server without a response.
                else if (activeIceServer.OutstandingRequestsSent >= IceServer.MAX_REQUESTS && activeIceServer.LastResponseReceivedAt == DateTime.MinValue)
                {
                    logger.LogIceServerConnectionTimeout(activeIceServerUri, activeIceServer.OutstandingRequestsSent);
                    activeIceServer.Error = SocketError.TimedOut;
                }
                // Maximum number of error response have been received for the requests sent to this ICE server.
                else if (activeIceServer.ErrorResponseCount >= IceServer.MAX_ERRORS)
                {
                    logger.LogIceServerErrorResponses(activeIceServerUri, activeIceServer.ErrorResponseCount);
                    activeIceServer.Error = SocketError.TimedOut;
                }
                // Send STUN binding request.
                else if (activeIceServer.ServerReflexiveEndPoint is null && activeIceServerUri.Scheme is STUNSchemesEnum.stun)
                {
                    activeIceServer.Error = SendStunBindingRequest(_activeIceServer);
                }
                // Send TURN binding request.
                else if (activeIceServer.ServerReflexiveEndPoint is null && activeIceServerUri.Scheme is STUNSchemesEnum.turn or STUNSchemesEnum.turns)
                {
                    activeIceServer.Error = SendTurnAllocateRequest(_activeIceServer);
                }
                else
                {
                    logger.LogIceUnexpectedState(activeIceServerUri);
                }
            }
            finally
            {
                Monitor.Exit(_iceServerResolver);
            }
        }
    }

    /// <summary>
    /// Adds candidates and updates the checklist for an ICE server that has completed
    /// the initial connectivity checks.
    /// </summary>
    /// <param name="iceServer">The ICE server that the initial checks have been completed
    /// for.</param>
    private async Task AddCandidatesForIceServer(IceServer iceServer)
    {
        var init = new RTCIceCandidateInit
        {
            usernameFragment = LocalIceUser,
            sdpMid = SDP_MID,
            sdpMLineIndex = SDP_MLINE_INDEX,
        };

        if (iceServer.ServerReflexiveEndPoint is { })
        {
            var svrRflxCandidate = iceServer.GetCandidate(init, RTCIceCandidateType.srflx);

            if (_policy == RTCIceTransportPolicy.all && svrRflxCandidate is { })
            {
                logger.LogIcePeerReflexAddingCandidate(iceServer.Uri, iceServer.ServerReflexiveEndPoint);

                // Note server reflexive candidates don't update the checklist pairs since it's merely an
                // alternative way to represent an existing host candidate.
                _candidates.Add(svrRflxCandidate);
                OnIceCandidate?.Invoke(svrRflxCandidate);
            }
        }

        if (_relayChecklistCandidate is null && iceServer.RelayEndPoint is { })
        {
            var relayCandidate = iceServer.GetCandidate(init, RTCIceCandidateType.relay);
            Debug.Assert(relayCandidate is { });
            relayCandidate.SetDestinationEndPoint(iceServer.RelayEndPoint);

            // A local relay candidate is stored so it can be pared with any remote candidates
            // that arrive after the checklist update carried out in this method.
            _relayChecklistCandidate = relayCandidate;

            if (relayCandidate is { })
            {
                logger.LogIceRelayAddingCandidate(iceServer.Uri, iceServer.RelayEndPoint);

                _candidates.Add(relayCandidate);
                OnIceCandidate?.Invoke(relayCandidate);
            }

            foreach (var remoteCandidate in _remoteCandidates)
            {
                await UpdateChecklist(_relayChecklistCandidate, remoteCandidate).ConfigureAwait(false);
            }
        }

        IceGatheringState = RTCIceGatheringState.complete;
        OnIceGatheringStateChange?.Invoke(IceGatheringState);
    }

    /// <summary>
    /// Updates the checklist with new candidate pairs.
    /// </summary>
    /// <remarks>
    /// From https://tools.ietf.org/html/rfc8445#section-6.1.2.2:
    /// IPv6 link-local addresses MUST NOT be paired with other than link-local addresses.
    /// </remarks>
    /// <param name="localCandidate">The local candidate for the checklist entry.</param>
    /// <param name="remoteCandidate">The remote candidate to attempt to create a new checklist
    /// entry for.</param>
    private async Task UpdateChecklist(RTCIceCandidate localCandidate, RTCIceCandidate remoteCandidate)
    {
        if (localCandidate is null)
        {
            logger.LogIceLocalCandidateUpdateChecklistError();
            return;
        }
        else if (remoteCandidate is null)
        {
            logger.LogIceRemoteCandidateUpdateChecklistError();
            return;
        }

        // This method is called in a fire and forget fashion so any exceptions need to be handled here.
        try
        {
            // Attempt to resolve the remote candidate address.
            if (!IPAddress.TryParse(remoteCandidate.address, out var remoteCandidateIPAddr))
            {
                Debug.Assert(remoteCandidate.address is { });
                if (remoteCandidate.address.EndsWith(MDNS_TLD, StringComparison.OrdinalIgnoreCase))
                {
                    var addresses = await ResolveMdnsName(remoteCandidate).ConfigureAwait(false);
                    if (addresses.Length == 0)
                    {
                        logger.LogIceMdnsResolutionFailed(remoteCandidate.address);
                    }
                    else
                    {
                        remoteCandidateIPAddr = addresses[0];
                        logger.LogIceMdnsResolutionSuccess(remoteCandidate.address, remoteCandidateIPAddr);

                        var remoteEP = new IPEndPoint(remoteCandidateIPAddr, remoteCandidate.port);
                        remoteCandidate.SetDestinationEndPoint(remoteEP);
                    }
                }
                else
                {
                    Debug.Assert(_dnsLookupClient is { });
                    // The candidate string can be a hostname or an IP address.
                    var lookupResult = await _dnsLookupClient.QueryAsync(remoteCandidate.address, DnsClient.QueryType.A).ConfigureAwait(false);

                    if (lookupResult.Answers.Count > 0)
                    {
                        remoteCandidateIPAddr = null;
                        foreach (var rr in lookupResult.Answers)
                        {
                            if (rr is DnsClient.Protocol.ARecord a)
                            {
                                remoteCandidateIPAddr = a.Address;
                                break;
                            }
                        }
                        logger.LogIceDnsResolutionSuccess(remoteCandidate.address, remoteCandidateIPAddr);
                    }
                    else
                    {
                        logger.LogIceDnsResolutionFailed(remoteCandidate.address);
                    }

                    if (remoteCandidateIPAddr is { })
                    {
                        var remoteEP = new IPEndPoint(remoteCandidateIPAddr, remoteCandidate.port);
                        remoteCandidate.SetDestinationEndPoint(remoteEP);
                    }
                }
            }
            else
            {
                var remoteEP = new IPEndPoint(remoteCandidateIPAddr, remoteCandidate.port);
                remoteCandidate.SetDestinationEndPoint(remoteEP);
            }

            // If the remote candidate is resolvable create a new checklist entry.
            if (remoteCandidate.DestinationEndPoint is { })
            {
                var supportsIPv4 = true;
                var supportsIPv6 = false;

                if (localCandidate.type == RTCIceCandidateType.relay)
                {
                    Debug.Assert(localCandidate.DestinationEndPoint is { });
                    var addressFamily = localCandidate.DestinationEndPoint.AddressFamily;
                    supportsIPv4 = addressFamily == AddressFamily.InterNetwork;
                    supportsIPv6 = addressFamily == AddressFamily.InterNetworkV6;
                }
                else
                {
                    Debug.Assert(RtpConnection is { });

                    var addressFamily = RtpConnection.Socket.AddressFamily;
                    supportsIPv4 = addressFamily == AddressFamily.InterNetwork || base.IsDualMode;
                    supportsIPv6 = addressFamily == AddressFamily.InterNetworkV6 || base.IsDualMode;
                }

                if (remoteCandidateIPAddr is { })
                {
                    lock (_checklistLock)
                    {
                        Debug.Assert(remoteCandidateIPAddr is { });
                        var addressFamily = remoteCandidateIPAddr.AddressFamily;
                        if (addressFamily == AddressFamily.InterNetwork && supportsIPv4 ||
                            addressFamily == AddressFamily.InterNetworkV6 && supportsIPv6)
                        {
                            var entry = new ChecklistEntry(localCandidate, remoteCandidate, IsController);

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
            else
            {
                logger.LogRtpCandidatesUnavailable(remoteCandidate);
            }
        }
        catch (Exception excp)
        {
            logger.LogUpdateChecklistError(excp.Message, excp);
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

        lock (_checklistLock)
        {
            if (FindMatchingChecklistEntry(entry) is { } existingEntry)
            {
                // Don't replace an existing checklist entry if it's already acting as the nominated entry.
                if (!existingEntry.Nominated)
                {
                    if (entry.Priority > existingEntry.Priority)
                    {
                        logger.LogIceChecklistEntryLowerPriority(entry.RemoteCandidate);
                        _checklist.Remove(existingEntry);
                        _checklist.Add(entry);
                    }
                    else
                    {
                        logger.LogIceChecklistEntryHigherPriority(entry.RemoteCandidate);
                    }
                }
            }
            else
            {
                // No existing entry.
                logger.LogIceNewChecklistEntry(entry.LocalCandidate, entry.RemoteCandidate);
                _checklist.Add(entry);
            }

            ChecklistEntry? FindMatchingChecklistEntry(ChecklistEntry targetEntry)
            {
                var entryRemoteEP = targetEntry.RemoteCandidate.DestinationEndPoint;
                Debug.Assert(entryRemoteEP is { });

                foreach (var x in _checklist)
                {
                    var remoteCandidate = x.RemoteCandidate;
                    var destinationEndPoint = remoteCandidate.DestinationEndPoint;
                    if (x.LocalCandidate.type == targetEntry.LocalCandidate.type
                        && destinationEndPoint is { }
                        && destinationEndPoint.Address.Equals(entryRemoteEP.Address)
                        && destinationEndPoint.Port == entryRemoteEP.Port
                        && remoteCandidate.protocol == targetEntry.RemoteCandidate.protocol)
                    {
                        return x;
                    }
                }

                return null;

            }
        }
    }

    /// <summary>
    /// The periodic logic to run to establish or monitor an ICE connection.
    /// </summary>
    private void DoConnectivityCheck(object? stateInfo)
    {
        if (_closed)
        {
            return;
        }

        switch (IceConnectionState)
        {
            case RTCIceConnectionState.@new:
            case RTCIceConnectionState.checking:
                ProcessChecklist();
                break;

            case RTCIceConnectionState.connected:
            case RTCIceConnectionState.disconnected:
                // Periodic checks on the nominated peer.
                Debug.Assert(NominatedEntry is { });
                SendCheckOnConnectedPair(NominatedEntry);
                break;

            case RTCIceConnectionState.failed:
            case RTCIceConnectionState.closed:
                logger.LogIceChecksTimerStopped(IceConnectionState);
                _connectivityChecksTimer?.Dispose();
                break;
        }
    }

    /// <summary>
    /// Processes the checklist and sends any required STUN requests to perform connectivity checks.
    /// </summary>
    /// <remarks>
    /// The scheduling mechanism for ICE is specified in https://tools.ietf.org/html/rfc8445#section-6.1.4.
    /// </remarks>
    private async void ProcessChecklist()
    {
        if (!_closed && (IceConnectionState == RTCIceConnectionState.@new ||
            IceConnectionState == RTCIceConnectionState.checking))
        {
            while (_pendingRemoteCandidates.TryDequeue(out var candidate))
            {
                if (_policy != RTCIceTransportPolicy.relay)
                {
                    // The reason not to wait for this operation is that the ICE candidate can
                    // contain a hostname and require a DNS lookup. There's nothing that can be done
                    // if the DNS lookup fails so initiate the task and then keep going with
                    // adding any other pending candidates and move on with processing the check list.
                    _ = UpdateChecklist(_localChecklistCandidate, candidate);
                }

                // If a relay server is available add a checklist entry for it as well.
                if (_relayChecklistCandidate is { })
                {
                    // The local relay candidate has already been checked and any hostnames 
                    // resolved when the ICE servers were checked.
                    await UpdateChecklist(_relayChecklistCandidate, candidate).ConfigureAwait(false);
                }
            }

            // The connection state will be set to checking when the remote ICE user and password are available.
            // Until that happens there is no work to do.
            if (IceConnectionState == RTCIceConnectionState.checking)
            {
                lock (_checklistLock)
                {
                    if (_checklist.Count > 0)
                    {
                        if (RemoteIceUser is null || RemoteIcePassword is null)
                        {
                            logger.LogIceFailedNoRemoteCredentials();
                            IceConnectionState = RTCIceConnectionState.failed;
                        }
                        else
                        {
                            // The checklist gets sorted into priority order whenever a remote candidate and its corresponding candidate pairs
                            // are added. At this point it can be relied upon that the checklist is correctly sorted by candidate pair priority.

                            // Do a check for any timed out entries.
                            var now = DateTime.Now;
                            var rto = RTO;
                            ChecklistEntry? nextEntry = null;
                            ChecklistEntry? retransmitEntry = null;
                            foreach (var entry in _checklist)
                            {
                                if (entry.State == ChecklistEntryState.InProgress
                                    && now.Subtract(entry.FirstCheckSentAt).TotalSeconds > FAILED_TIMEOUT_PERIOD)
                                {
                                    logger.LogIceChecklistEntryTimeout(entry.LocalCandidate, entry.RemoteCandidate);
                                    entry.State = ChecklistEntryState.Failed;
                                }

                                // Capture the first waiting entry for connectivity check
                                if (nextEntry is null && entry.State == ChecklistEntryState.Waiting)
                                {
                                    nextEntry = entry;
                                }

                                // Capture the first retransmit candidate
                                if (retransmitEntry is null
                                    && entry.State == ChecklistEntryState.InProgress
                                    && now.Subtract(entry.LastCheckSentAt).TotalMilliseconds > rto)
                                {
                                    retransmitEntry = entry;
                                }

                            }

                            // Move on to checking for  checklist entries that need an initial check sent.
                            if (nextEntry is { })
                            {
                                SendConnectivityCheck(nextEntry, false);
                                return;
                            }

                            // No waiting entries so check for ones requiring a retransmit.
                            if (retransmitEntry is { })
                            {
                                SendConnectivityCheck(retransmitEntry, false);
                                return;
                            }

                            if (IceGatheringState == RTCIceGatheringState.complete)
                            {
                                //Try force finalize process as probably we lost any RtpPacketResponse during process and we are unable to finalize process
                                if (NominatedEntry is null)
                                {
                                    // Do a check for any timed out that has
                                    var requireReprocess = false;
                                    foreach (var entry in _checklist)
                                    {
                                        if (entry.State == ChecklistEntryState.Succeeded
                                            && entry.LastCheckSentAt > DateTime.MinValue
                                            && now.Subtract(entry.LastCheckSentAt).TotalSeconds > FAILED_TIMEOUT_PERIOD)
                                        {
                                            if (entry.Nominated)
                                            {
                                                requireReprocess = true;
                                            }

                                            entry.State = ChecklistEntryState.Failed;
                                            entry.Nominated = false;

                                            logger.LogIceChecklistEntrySucceededTimeout(entry.LocalCandidate, entry.RemoteCandidate);
                                        }
                                    }

                                    //Try nominate another entry
                                    if (requireReprocess)
                                    {
                                        ProcessNominateLogicAsController(null);
                                    }
                                }

                                // If this point is reached and all entries are in a failed state then the overall result 
                                // of the ICE check is a failure.
                                if (_checklist.TrueForAll(static x => x.State == ChecklistEntryState.Failed))
                                {
                                    _checklistState = ChecklistState.Failed;
                                    IceConnectionState = RTCIceConnectionState.failed;
                                    OnIceConnectionStateChange?.Invoke(IceConnectionState);
                                }
                            }
                        }
                    }
                    else if (_checklistStartedAt != DateTime.MinValue &&
                        DateTime.Now.Subtract(_checklistStartedAt).TotalSeconds > FAILED_TIMEOUT_PERIOD)
                    {
                        // No checklist entries were made available before the failed timeout.
                        logger.LogIceChannelFailed(_checklistStartedAt);

                        _checklistState = ChecklistState.Failed;
                        //IceConnectionState = RTCIceConnectionState.disconnected;
                        // No point going to and ICE disconnected state as there was never a connection and therefore
                        // nothing to monitor for a re-connection.
                        IceConnectionState = RTCIceConnectionState.failed;
                        OnIceConnectionStateChange?.Invoke(IceConnectionState);
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
        if (NominatedEntry is null)
        {
            _connectedAt = DateTime.Now;
            var duration = (int)_connectedAt.Subtract(_startedGatheringAt).TotalMilliseconds;

            logger.LogIceChannelConnected(duration, entry.LocalCandidate, entry.RemoteCandidate);

            entry.Nominated = true;
            entry.LastConnectedResponseAt = DateTime.Now;
            _checklistState = ChecklistState.Completed;
            Debug.Assert(_connectivityChecksTimer is { });
            _connectivityChecksTimer.Change(CONNECTED_CHECK_PERIOD * 1000, CONNECTED_CHECK_PERIOD * 1000);
            NominatedEntry = entry;
            IceConnectionState = RTCIceConnectionState.connected;
            OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
        }
        else
        {
            // The nominated entry has been changed.
            logger.LogIceChannelNominatedChanged(NominatedEntry.RemoteCandidate, entry.RemoteCandidate);

            entry.Nominated = true;
            entry.LastConnectedResponseAt = DateTime.Now;
            NominatedEntry = entry;
            OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
        }
    }

    /// <summary>
    /// Performs a connectivity check for a single candidate pair entry.
    /// </summary>
    /// <param name="candidatePair">The candidate pair to perform a connectivity check for.</param>
    /// <param name="setUseCandidate">If true indicates we are acting as the "controlling" ICE agent
    /// and are nominating this candidate as the chosen one.</param>
    /// <remarks>As specified in https://tools.ietf.org/html/rfc8445#section-7.2.4.
    /// 
    /// Relay candidates are a special (and more difficult) case. The extra steps required to send packets via
    /// a TURN server are:
    /// - A Channel Bind request needs to be sent for each peer end point the channel will be used to
    ///   communicate with.
    /// - Packets need to be sent and received as TURN Channel Data messages.
    /// </remarks>
    /// 
    private void SendConnectivityCheck(ChecklistEntry candidatePair, bool setUseCandidate)
    {
        if (_closed)
        {
            return;
        }

        if (candidatePair.FirstCheckSentAt == DateTime.MinValue)
        {
            candidatePair.FirstCheckSentAt = DateTime.Now;
            candidatePair.State = ChecklistEntryState.InProgress;
        }

        candidatePair.LastCheckSentAt = DateTime.Now;
        candidatePair.ChecksSent++;
        candidatePair.RequestTransactionID = Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH);

        var localCandidate = candidatePair.LocalCandidate;
        var isRelayCheck = localCandidate.type == RTCIceCandidateType.relay;

        if (isRelayCheck && candidatePair.TurnPermissionsResponseAt == DateTime.MinValue)
        {
            if (candidatePair.TurnPermissionsRequestSent >= IceServer.MAX_REQUESTS)
            {
                logger.LogIceTurnPermissionsFailed(localCandidate.IceServer?.Uri, candidatePair.TurnPermissionsRequestSent);
                candidatePair.State = ChecklistEntryState.Failed;
            }
            else
            {
                // Send Create Permissions request to TURN server for remote candidate.
                candidatePair.TurnPermissionsRequestSent++;

                var requestTransactionID = candidatePair.RequestTransactionID;
                Debug.Assert(requestTransactionID is { });

                logger.LogIceTurnPermissionsRequest(
                    candidatePair.TurnPermissionsRequestSent,
                    localCandidate.IceServer?.Uri,
                    candidatePair.RemoteCandidate.DestinationEndPoint,
                    requestTransactionID);

                Debug.Assert(localCandidate?.IceServer is { });
                Debug.Assert(candidatePair?.RemoteCandidate?.DestinationEndPoint is { });
                SendTurnCreatePermissionsRequest(requestTransactionID, localCandidate.IceServer, candidatePair.RemoteCandidate.DestinationEndPoint);
            }
        }
        else
        {
            if (localCandidate.type == RTCIceCandidateType.relay)
            {
                logger.LogIceConnectivityCheck(
                    localCandidate,
                    candidatePair.RemoteCandidate,
                    base.RTPLocalEndPoint,
                    localCandidate.IceServer?.ServerEndPoint,
                    setUseCandidate);
            }
            else
            {
                logger.LogIceRelayCheck(
                    localCandidate,
                    candidatePair.RemoteCandidate,
                    base.RTPLocalEndPoint,
                    candidatePair.RemoteCandidate.DestinationEndPoint,
                    setUseCandidate);
            }

            SendStunBindingRequest(candidatePair, setUseCandidate);
        }
    }

    /// <summary>
    /// Builds and sends a STUN binding request to a remote peer based on the candidate pair properties.
    /// </summary>
    /// <param name="candidatePair">The candidate pair identifying the remote peer to send the STUN Binding Request
    /// to.</param>
    /// <param name="setUseCandidate">Set to true to add a "UseCandidate" attribute to the STUN request.</param>
    private void SendStunBindingRequest(ChecklistEntry candidatePair, bool setUseCandidate)
    {
        var requestTransactionID = candidatePair.RequestTransactionID;
        Debug.Assert(requestTransactionID is { });
        var stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest)
        {
            Header =
            {
                TransactionId = Encoding.ASCII.GetBytes(requestTransactionID)
            },
            Attributes =
            {
                new STUNAttribute(STUNAttributeTypesEnum.Priority, candidatePair.LocalPriority)
            },
        };

        stunRequest.AddUsernameAttribute($"{RemoteIceUser}:{LocalIceUser}");

        if (IsController)
        {
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.IceControlling, _iceTiebreaker));
        }
        else
        {
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.IceControlled, _iceTiebreaker));
        }

        if (setUseCandidate)
        {
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.UseCandidate, ReadOnlyMemory<byte>.Empty));
        }

        //logger.LogSendStunBindingRequest(activeIceServerUri, remoteCandidateEndPoint, stunRequest);

        var remoteCandidateEndPoint = candidatePair.RemoteCandidate.DestinationEndPoint;

        var bufferSize = stunRequest.GetByteBufferSizeStringKey(RemoteIcePassword, addFingerprint: true);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            stunRequest.WriteToBufferStringKey(rentedBuffer.AsSpan(0, bufferSize), RemoteIcePassword, addFingerprint: true);

            Debug.Assert(remoteCandidateEndPoint is { });

            if (candidatePair.LocalCandidate.type == RTCIceCandidateType.relay)
            {
                Debug.Assert(candidatePair?.LocalCandidate?.IceServer is { });
                var relayServerEP = candidatePair.LocalCandidate.IceServer.ServerEndPoint;
                var protocol = candidatePair.LocalCandidate.IceServer.Protocol;

                Debug.Assert(relayServerEP is { });

                using var activity = NetIceActivitySource.StartStunMessageSentActivity(
                    stunRequest,
                    relayServerEP,
                    null);

                SendRelay(
                    protocol,
                    remoteCandidateEndPoint,
                    rentedBuffer.AsMemory(0, bufferSize),
                    null,
                    relayServerEP,
                    candidatePair.LocalCandidate.IceServer);
            }
            else
            {
                using var activity = NetIceActivitySource.StartStunMessageSentActivity(
                    stunRequest,
                    remoteCandidateEndPoint,
                    null);

                var sendResult = base.Send(RTPChannelSocketsEnum.RTP, remoteCandidateEndPoint, rentedBuffer.AsMemory(0, bufferSize));

                if (sendResult != SocketError.Success)
                {
                    logger.LogStunBindingSendError(remoteCandidateEndPoint, sendResult);
                }
                else
                {
                    OnStunMessageSent?.Invoke(stunRequest, remoteCandidateEndPoint, false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Builds and sends the connectivity check on a candidate pair that is set
    /// as the current nominated, connected pair.
    /// </summary>
    /// <param name="candidatePair">The pair to send the connectivity check on.</param>
    private void SendCheckOnConnectedPair(ChecklistEntry candidatePair)
    {
        if (candidatePair is null)
        {
            logger.LogIceConnCheckEmptyPair();
        }
        else
        {
            if (DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds > FAILED_TIMEOUT_PERIOD &&
                DateTime.Now.Subtract(candidatePair.LastBindingRequestReceivedAt).TotalSeconds > FAILED_TIMEOUT_PERIOD)
            {
                var duration = (int)DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds;
                logger.LogIceChannelFailedTimeout(duration, candidatePair.LocalCandidate, candidatePair.RemoteCandidate);

                IceConnectionState = RTCIceConnectionState.failed;
                OnIceConnectionStateChange?.Invoke(IceConnectionState);

                _connectivityChecksTimer?.Dispose();
            }
            else
            {
                if (DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds > DISCONNECTED_TIMEOUT_PERIOD &&
                    DateTime.Now.Subtract(candidatePair.LastBindingRequestReceivedAt).TotalSeconds > DISCONNECTED_TIMEOUT_PERIOD)
                {
                    if (IceConnectionState == RTCIceConnectionState.connected)
                    {
                        var duration = (int)DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds;
                        logger.LogIceChannelDisconnected(duration, candidatePair.LocalCandidate, candidatePair.RemoteCandidate);

                        IceConnectionState = RTCIceConnectionState.disconnected;
                        OnIceConnectionStateChange?.Invoke(IceConnectionState);
                    }
                }
                else if (IceConnectionState != RTCIceConnectionState.connected)
                {
                    logger.LogIceChannelReconnected(candidatePair.LocalCandidate, candidatePair.RemoteCandidate);

                    // Re-connected.
                    IceConnectionState = RTCIceConnectionState.connected;
                    OnIceConnectionStateChange?.Invoke(IceConnectionState);
                }

                candidatePair.RequestTransactionID = candidatePair.RequestTransactionID ?? Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH);
                candidatePair.LastCheckSentAt = DateTime.Now;
                candidatePair.ChecksSent++;

                SendStunBindingRequest(candidatePair, false);
            }
        }
    }

    /// <summary>
    /// Processes a received STUN request or response.
    /// </summary>
    /// <remarks>
    /// Actions to take on a successful STUN response https://tools.ietf.org/html/rfc8445#section-7.2.5.3
    /// - Discover peer reflexive remote candidates as per https://tools.ietf.org/html/rfc8445#section-7.2.5.3.1.
    /// - Construct a valid pair which means match a candidate pair in the check list and mark it as valid (since a successful STUN exchange 
    ///   has now taken place on it). A new entry may need to be created for this pair for a peer reflexive candidate.
    /// - Update state of candidate pair that generated the check to Succeeded.
    /// - If the controlling candidate set the USE_CANDIDATE attribute then the ICE agent that receives the successful response sets the nominated
    ///   flag of the pair to true. Once the nominated flag is set it concludes the ICE processing for that component.
    /// </remarks>
    /// <param name="stunMessage">The STUN message received.</param>
    /// <param name="remoteEndPoint">The remote end point the STUN packet was received from.</param>
    public async Task ProcessStunMessage(STUNMessage stunMessage, IPEndPoint remoteEndPoint, bool wasRelayed)
    {
        if (_closed)
        {
            return;
        }

        remoteEndPoint = (!remoteEndPoint.Address.IsIPv4MappedToIPv6) ? remoteEndPoint : new IPEndPoint(remoteEndPoint.Address.MapToIPv4(), remoteEndPoint.Port);

        // Check if the  STUN message is for an ICE server check.
        var iceServer = GetIceServerForTransactionID(stunMessage.Header.TransactionId);
        if (iceServer is { })
        {
            var candidatesAvailable = iceServer.GotStunResponse(stunMessage, remoteEndPoint);
            if (candidatesAvailable)
            {
                // Safe to wait here as the candidates from an ICE server will always be IP addresses only,
                // no DNS lookups required.
                await AddCandidatesForIceServer(iceServer).ConfigureAwait(false);
            }
        }
        else
        {
            // If the STUN message isn't for an ICE server then it needs to be matched against a remote
            // candidate and a checklist entry and if no match a "peer reflexive" candidate may need to
            // be created.
            if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
            {
                GotStunBindingRequest(stunMessage, remoteEndPoint, wasRelayed);
            }
            else if (stunMessage.Header.MessageClass is STUNClassTypesEnum.ErrorResponse or STUNClassTypesEnum.SuccessResponse)
            {
                // Correlate with request using transaction ID as per https://tools.ietf.org/html/rfc8445#section-7.2.5.
                var matchingChecklistEntry = GetChecklistEntryForStunResponse(stunMessage.Header.TransactionId);

                if (matchingChecklistEntry is null)
                {
                    if (IceConnectionState != RTCIceConnectionState.connected)
                    {
                        // If the channel is connected a mismatched txid can result if the connection is very busy, i.e. streaming 1080p video,
                        // it's likely to only be transient and does not impact the connection state.
                        logger.LogIceStunRequestTxIdMismatch(stunMessage.Header.MessageType);
                    }
                }
                else
                {
                    matchingChecklistEntry.GotStunResponse(stunMessage, remoteEndPoint);

                    if (_checklistState == ChecklistState.Running &&
                        stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
                    {
                        if (matchingChecklistEntry.Nominated)
                        {
                            logger.LogIceChecklistNominatedResponse(matchingChecklistEntry.RemoteCandidate);

                            // This is the response to a connectivity check that had the "UseCandidate" attribute set.
                            SetNominatedEntry(matchingChecklistEntry);
                        }
                        else if (IsController)
                        {
                            logger.LogIceChecklistBindingResponse(matchingChecklistEntry.State, matchingChecklistEntry.RemoteCandidate);
                            ProcessNominateLogicAsController(matchingChecklistEntry);
                        }
                    }
                }
            }
            else
            {
                logger.LogDtlsUnexpectedStunMessage(stunMessage.Header.MessageType, remoteEndPoint, stunMessage);
            }
        }
    }

    /// <summary>
    /// Handles Nominate logic when Agent is the controller
    /// </summary>
    /// <param name="possibleMatchingCheckEntry">Optional initial ChecklistEntry.</param>
    private void ProcessNominateLogicAsController(ChecklistEntry? possibleMatchingCheckEntry)
    {
        if (IsController && (NominatedEntry is null || !NominatedEntry.Nominated || NominatedEntry.State != ChecklistEntryState.Succeeded))
        {
            lock (_checklistLock)
            {
                _checklist.Sort();

                var findBetterOptionOrWait = possibleMatchingCheckEntry is null; //|| possibleMatchingCheckEntry.RemoteCandidate.type == RTCIceCandidateType.relay;
                var nominatedCandidate = _checklist.Find(
                        x => x.Nominated
                        && x.State == ChecklistEntryState.Succeeded
                        && (x.LastCheckSentAt == DateTime.MinValue ||
                            DateTime.Now.Subtract(x.LastCheckSentAt).TotalSeconds <= FAILED_TIMEOUT_PERIOD));

                //We already have a good candidate, discard our succeded candidate
                if (nominatedCandidate is { } /*&& nominatedCandidate.RemoteCandidate.type != RTCIceCandidateType.relay*/)
                {
                    possibleMatchingCheckEntry = null;
                    findBetterOptionOrWait = false;
                }

                if (findBetterOptionOrWait)
                {
                    //Search for another succeded non-nominated entries with better priority over our current object.
                    var betterOptionEntry = _checklist.Find(x =>
                       x.State == ChecklistEntryState.Succeeded &&
                        !x.Nominated &&
                        (possibleMatchingCheckEntry is null ||
                         (x.Priority > possibleMatchingCheckEntry.Priority /*&& x.RemoteCandidate.type != RTCIceCandidateType.relay*/) ||
                         possibleMatchingCheckEntry.State != ChecklistEntryState.Succeeded));

                    if (betterOptionEntry is { })
                    {
                        possibleMatchingCheckEntry = betterOptionEntry;
                        findBetterOptionOrWait = false; //possibleMatchingCheckEntry.RemoteCandidate.type == RTCIceCandidateType.relay;
                    }

                    //if we still need to find a better option, we will search for matching entries with high priority that still processing
                    if (findBetterOptionOrWait)
                    {
                        var waitOptionEntry = _checklist.Find(x =>
                            (x.State == ChecklistEntryState.InProgress || x.State == ChecklistEntryState.Waiting) &&
                             (possibleMatchingCheckEntry is null ||
                              (x.Priority > possibleMatchingCheckEntry.Priority /*&& x.RemoteCandidate.type != RTCIceCandidateType.relay*/) ||
                              possibleMatchingCheckEntry.State != ChecklistEntryState.Succeeded));

                        if (waitOptionEntry is { })
                        {
                            possibleMatchingCheckEntry = null;
                        }
                    }
                }
            }

            //Nominate Candidate if we pass in all heuristic checks from previous algorithm
            if (possibleMatchingCheckEntry is { State: ChecklistEntryState.Succeeded })
            {
                possibleMatchingCheckEntry.Nominated = true;
                SendConnectivityCheck(possibleMatchingCheckEntry, true);
            }
        }
    }

    /// <summary>
    /// Handles STUN binding requests received from remote candidates as part of the ICE connectivity checks.
    /// </summary>
    /// <param name="bindingRequest">The binding request received.</param>
    /// <param name="remoteEndPoint">The end point the request was received from.</param>
    /// <param name="wasRelayed">True of the request was relayed via the TURN server in use
    /// by this ICE channel (i.e. the ICE server that this channel is acting as the client with).</param>
    private void GotStunBindingRequest(STUNMessage bindingRequest, IPEndPoint remoteEndPoint, bool wasRelayed)
    {
        if (_closed)
        {
            return;
        }

        if (_policy == RTCIceTransportPolicy.relay && !wasRelayed)
        {
            // If the policy is "relay only" then direct binding requests are not accepted.
            logger.LogIceBindingRequestRejected(remoteEndPoint);

            var stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse)
            {
                Header = { TransactionId = bindingRequest.Header.TransactionId }
            };

            var bufferSize = stunErrResponse.GetByteBufferSize(ReadOnlySpan<byte>.Empty, addFingerprint: false);
            var rentedBuffer = MemoryPool<byte>.Shared.Rent(bufferSize);
            var memory = rentedBuffer.Memory.Slice(0, bufferSize);

            stunErrResponse.WriteToBuffer(memory.Span, ReadOnlySpan<byte>.Empty, addFingerprint: false);
            Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, memory, rentedBuffer);

            OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
        }
        else
        {
            var result = bindingRequest.CheckIntegrity(Encoding.UTF8.GetBytes(LocalIcePassword));

            if (!result)
            {
                // Send STUN error response.
                logger.LogIceStunBindingRequestFailed(remoteEndPoint);

                var stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse)
                {
                    Header = { TransactionId = bindingRequest.Header.TransactionId }
                };

                var bufferSize = stunErrResponse.GetByteBufferSize(ReadOnlySpan<byte>.Empty, addFingerprint: false);
                var rentedBuffer = MemoryPool<byte>.Shared.Rent(bufferSize);
                var memory = rentedBuffer.Memory.Slice(0, bufferSize);

                stunErrResponse.WriteToBuffer(memory.Span, ReadOnlySpan<byte>.Empty, addFingerprint: false);
                Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, memory, rentedBuffer);

                OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
            }
            else
            {
                ChecklistEntry? matchingChecklistEntry = null;

                // Find the checklist entry for this remote candidate and update its status.
                lock (_checklistLock)
                {
                    // The matching checklist entry is chosen as:
                    // - The entry that has a remote candidate with an end point that matches the endpoint this STUN request came from,
                    // - And if the STUN request was relayed through a TURN server then only match is the checklist local candidate is 
                    //   also a relay type. It is possible for the same remote end point to send STUN requests directly and via a TURN server.
                    foreach (var entry in _checklist)
                    {
                        if (entry.RemoteCandidate.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint)
                            && (!wasRelayed || entry.LocalCandidate.type == RTCIceCandidateType.relay))
                        {
                            matchingChecklistEntry = entry;
                            break;
                        }
                    }
                }

                bool IsUnknownRemoteCandidate(IPEndPoint remoteEndPoint)
                {
                    if (_remoteCandidates is null)
                    {
                        return true;
                    }

                    foreach (var candidate in _remoteCandidates)
                    {
                        if (candidate.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint))
                        {
                            return false;
                        }
                    }
                    return true;
                }

                if (matchingChecklistEntry is null && IsUnknownRemoteCandidate(remoteEndPoint))
                {
                    // This STUN request has come from a socket not in the remote ICE candidates list. 
                    // Add a new remote peer reflexive candidate. 
                    var peerRflxCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
                    peerRflxCandidate.SetAddressProperties(RTCIceProtocol.udp, remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.prflx, null, 0);
                    peerRflxCandidate.SetDestinationEndPoint(remoteEndPoint);
                    logger.LogIcePeerReflexAdded(remoteEndPoint);

                    Debug.Assert(_remoteCandidates is { });
                    _remoteCandidates.Add(peerRflxCandidate);

                    // Add a new entry to the check list for the new peer reflexive candidate.
                    var localCandidate = wasRelayed ? _relayChecklistCandidate : _localChecklistCandidate;
                    Debug.Assert(localCandidate is { });
                    var entry = new ChecklistEntry(
                        localCandidate,
                        peerRflxCandidate,
                        IsController);
                    entry.State = ChecklistEntryState.Waiting;

                    if (wasRelayed)
                    {
                        // No need to send a TURN permissions request given this request was already successfully relayed.
                        entry.TurnPermissionsRequestSent = 1;
                        entry.TurnPermissionsResponseAt = DateTime.Now;
                    }

                    AddChecklistEntry(entry);

                    matchingChecklistEntry = entry;
                }

                if (matchingChecklistEntry is null)
                {
                    logger.LogIceStunRequestMismatch();

                    var stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse)
                    {
                        Header = { TransactionId = bindingRequest.Header.TransactionId }
                    };

                    var bufferSize = stunErrResponse.GetByteBufferSize(ReadOnlySpan<byte>.Empty, addFingerprint: false);
                    var rentedBuffer = MemoryPool<byte>.Shared.Rent(bufferSize);
                    var memory = rentedBuffer.Memory.Slice(0, bufferSize);

                    stunErrResponse.WriteToBuffer(memory.Span, ReadOnlySpan<byte>.Empty, addFingerprint: false);
                    Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, memory, rentedBuffer);

                    OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
                }
                else
                {
                    // The UseCandidate attribute is only meant to be set by the "Controller" peer. This implementation
                    // will accept it irrespective of the peer roles. If the remote peer wants us to use a certain remote
                    // end point then so be it.
                    if (bindingRequest.Attributes.Exists(static x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate))
                    {
                        if (IceConnectionState != RTCIceConnectionState.connected)
                        {
                            // If we are the "controlled" agent and get a "use candidate" attribute that sets the matching candidate as nominated 
                            // as per https://tools.ietf.org/html/rfc8445#section-7.3.1.5.
                            logger.LogIceChecklistNominatedBinding(matchingChecklistEntry.RemoteCandidate);

                            SetNominatedEntry(matchingChecklistEntry);
                        }
                        else
                        {
                            Debug.Assert(NominatedEntry is { });
                            if (!matchingChecklistEntry.RemoteCandidate.Equals(NominatedEntry.RemoteCandidate))
                            {
                                // The remote peer is changing the nominated candidate.
                                logger.LogIceNominatedNewCandidate(matchingChecklistEntry.RemoteCandidate);
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                        }
                    }

                    matchingChecklistEntry.LastBindingRequestReceivedAt = DateTime.Now;

                    var stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse)
                    {
                        Header = { TransactionId = bindingRequest.Header.TransactionId }
                    };

                    stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                    var bufferSize = stunResponse.GetByteBufferSizeStringKey(LocalIcePassword, addFingerprint: true);
                    var rentedBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);

                    try
                    {
                        stunResponse.WriteToBufferStringKey(rentedBuffer.AsSpan(0, bufferSize), LocalIcePassword, addFingerprint: true);
                        var stunRespMemory = rentedBuffer.AsMemory(0, bufferSize);

                        if (wasRelayed)
                        {
                            using var activity = NetIceActivitySource.StartStunMessageSentActivity(
                                stunResponse,
                                remoteEndPoint,
                                null);

                            Debug.Assert(matchingChecklistEntry?.LocalCandidate?.IceServer is { });
                            Debug.Assert(matchingChecklistEntry?.LocalCandidate?.IceServer.ServerEndPoint is { });
                            var protocol = matchingChecklistEntry.LocalCandidate.IceServer.Protocol;
                            SendRelay(
                                protocol,
                                remoteEndPoint,
                                stunRespMemory,
                                null,
                                matchingChecklistEntry.LocalCandidate.IceServer.ServerEndPoint,
                                matchingChecklistEntry.LocalCandidate.IceServer);

                            OnStunMessageSent?.Invoke(stunResponse, remoteEndPoint, true);
                        }
                        else
                        {
                            using var activity = NetIceActivitySource.StartStunMessageSentActivity(
                                stunResponse,
                                remoteEndPoint,
                                null);

                            Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespMemory, null);
                            OnStunMessageSent?.Invoke(stunResponse, remoteEndPoint, false);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts to get the matching checklist entry for the transaction ID in a STUN response.
    /// </summary>
    /// <param name="transactionID">The STUN response transaction ID.</param>
    /// <returns>A checklist entry or null if there was no match.</returns>
    private ChecklistEntry? GetChecklistEntryForStunResponse(byte[] transactionID)
    {
        var txID = Encoding.ASCII.GetString(transactionID);

        lock (_checklistLock)
        {
            foreach (var entry in _checklist)
            {
                if (entry.IsTransactionIDMatch(txID))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a STUN response transaction ID to determine if it matches a check being carried
    /// out for an ICE server.
    /// </summary>
    /// <param name="transactionID">The transaction ID from the STUN response.</param>
    /// <returns>If found a matching state object or null if not.</returns>
    private IceServer? GetIceServerForTransactionID(byte[] transactionID)
    {
        if (_iceServerResolver.IceServers.Count == 0)
        {
            return null;
        }

        var txID = Encoding.ASCII.GetString(transactionID);

        foreach (var (_, iceServer) in _iceServerResolver.IceServers)
        {
            if (iceServer.IsTransactionIDMatch(txID))
            {
                return iceServer;
            }
        }

        return null;
    }

    /// <summary>
    /// Sends a STUN binding request to an ICE server.
    /// </summary>
    /// <param name="iceServer">The ICE server to send the request to.</param>
    /// <returns>The result of the send attempt. Note this is the return code from the
    /// socket send call and not the result code from the STUN response.</returns>
    private SocketError SendStunBindingRequest(IceServer iceServer)
    {
        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        Debug.Assert(iceServer.TransactionID is { });

        // Send a STUN binding request.
        var stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest)
        {
            Header =
            {
                TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID),
            },
        };

        var iceServerUri = iceServer.Uri;
        var iceServerEndPoint = iceServer.ServerEndPoint;

        Debug.Assert(iceServerEndPoint is { });

        using var activity = NetIceActivitySource.StartStunMessageSentActivity(
            stunRequest,
            iceServerEndPoint,
            iceServerUri,
            iceServer.Realm,
            iceServer.Nonce);

        logger.LogSendStunBindingRequest(iceServerUri, iceServerEndPoint, stunRequest);

        var sendResult = SendStunMessage(stunRequest, iceServer);

        if (sendResult != SocketError.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sendResult.ToStringFast());

            logger.LogIceStunServerBindingSendError(iceServer.OutstandingRequestsSent, iceServerUri, iceServerEndPoint, sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(stunRequest, iceServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends an allocate request to a TURN server.
    /// </summary>
    /// <param name="iceServer">The TURN server to send the request to.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    private SocketError SendTurnAllocateRequest(IceServer iceServer)
    {
        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        Debug.Assert(iceServer.TransactionID is { });
        Debug.Assert(iceServer.ServerEndPoint is { });

        var allocateRequest = new STUNMessage(STUNMessageTypesEnum.Allocate)
        {
            Header =
            {
                TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID),
            },
            Attributes =
            {
                new STUNAttribute(STUNAttributeTypesEnum.RequestedTransport, STUNAttributeConstants.UdpTransportType),
                new STUNAttribute(
                    STUNAttributeTypesEnum.RequestedAddressFamily,
                    iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork
                        ? STUNAttributeConstants.IPv4AddressFamily
                        : STUNAttributeConstants.IPv6AddressFamily),
            },
        };

        var iceServerUri = iceServer.Uri;
        var iceServerEndPoint = iceServer.ServerEndPoint;

        using var activity = NetIceActivitySource.StartStunMessageSentActivity(
            allocateRequest,
            iceServerEndPoint,
            iceServerUri,
            iceServer.Realm,
            iceServer.Nonce);

        logger.LogSendTurnAllocateRequest(iceServerUri, iceServerEndPoint, allocateRequest);

        var sendResult = SendStunMessage(allocateRequest, iceServer);

        if (sendResult != SocketError.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sendResult.ToStringFast());

            logger.LogIceTurnAllocateRequestSendError(
                iceServer.OutstandingRequestsSent,
                iceServer.Uri,
                iceServer.ServerEndPoint,
                sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(allocateRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends an allocate request to a TURN server.
    /// </summary>
    /// <param name="iceServer">The TURN server to send the request to.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    private SocketError SendTurnRefreshRequest(IceServer iceServer)
    {
        iceServer.OutstandingRequestsSent += 1;
        iceServer.LastRequestSentAt = DateTime.Now;

        Debug.Assert(iceServer.TransactionID is { });
        Debug.Assert(iceServer.ServerEndPoint is { });

        var refreshRequest = new STUNMessage(STUNMessageTypesEnum.Refresh)
        {
            Header =
            {
                TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID),
            },
            Attributes =
            {
                new STUNAttribute(STUNAttributeTypesEnum.Lifetime, ALLOCATION_TIME_TO_EXPIRY_VALUE),
                new STUNAttribute(
                    STUNAttributeTypesEnum.RequestedAddressFamily,
                    iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork
                        ? STUNAttributeConstants.IPv4AddressFamily
                        : STUNAttributeConstants.IPv6AddressFamily),
            }
        };

        var iceServerUri = iceServer.Uri;
        var iceServerEndPoint = iceServer.ServerEndPoint;

        using var activity = NetIceActivitySource.StartStunMessageSentActivity(
            refreshRequest,
            iceServerEndPoint,
            iceServerUri,
            iceServer.Realm,
            iceServer.Nonce);

        logger.LogSendTurnRefreshRequest(iceServerUri, iceServerEndPoint, refreshRequest);

        var sendResult = SendStunMessage(refreshRequest, iceServer);

        if (sendResult != SocketError.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sendResult.ToStringFast());

            logger.LogIceTurnRefreshRequestSendError(
                iceServer.OutstandingRequestsSent,
                iceServer.Uri,
                iceServer.ServerEndPoint,
                sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(refreshRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends a create permissions request to a TURN server for a peer end point.
    /// </summary>
    /// <param name="transactionID">The transaction ID to set on the request. This
    /// gets used to match responses back to the sender.</param>
    /// <param name="iceServer">The ICE server to send the request to.</param>
    /// <param name="peerEndPoint">The peer end point to request the channel bind for.</param>
    /// <returns>The result from the socket send (not the response code from the TURN server).</returns>
    private SocketError SendTurnCreatePermissionsRequest(string transactionID, IceServer iceServer, IPEndPoint peerEndPoint)
    {
        var transactionId = Encoding.ASCII.GetBytes(transactionID);

        var permissionsRequest = new STUNMessage(STUNMessageTypesEnum.CreatePermission)
        {
            Header =
            {
                TransactionId = transactionId,
            },
            Attributes =
            {
                new STUNXORAddressAttribute(
                    STUNAttributeTypesEnum.XORPeerAddress,
                    peerEndPoint.Port,
                    peerEndPoint.Address,
                    transactionId)
            }
        };

        var iceServerUri = iceServer.Uri;
        var iceServerEndPoint = iceServer.ServerEndPoint;

        Debug.Assert(iceServerEndPoint is { });

        using var activity = NetIceActivitySource.StartStunMessageSentActivity(
            permissionsRequest,
            iceServerEndPoint,
            iceServerUri,
            iceServer.Realm,
            iceServer.Nonce);

        logger.LogSendTurnCreatePermissionsRequest(iceServerUri, iceServerEndPoint, permissionsRequest);

        var sendResult = SendStunMessage(permissionsRequest, iceServer);

        Debug.Assert(iceServer.ServerEndPoint is { });

        if (sendResult != SocketError.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sendResult.ToStringFast());

            logger.LogIceTurnCreatePermissionsRequestSendError(
                iceServer.OutstandingRequestsSent,
                iceServer.Uri,
                iceServer.ServerEndPoint,
                sendResult);
        }
        else
        {
            OnStunMessageSent?.Invoke(permissionsRequest, iceServer.ServerEndPoint, false);
        }

        return sendResult;
    }

    /// <summary>
    /// Sends a packet via a TURN relay server.
    /// </summary>
    /// <param name="dstEndPoint">The peer destination end point.</param>
    /// <param name="buffer">The data to send to the peer.</param>
    /// <param name="memoryOwner">The owner of the buffer memory, if any.</param>
    /// <param name="relayEndPoint">The TURN server end point to send the relayed request to.</param>
    /// <returns></returns>
    private SocketError SendRelay(ProtocolType protocol, IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner, IPEndPoint relayEndPoint, IceServer iceServer)
    {
        using (memoryOwner)
        {
            var sendReq = new STUNMessage(STUNMessageTypesEnum.SendIndication);
            sendReq.AddXORPeerAddressAttribute(dstEndPoint.Address, dstEndPoint.Port);
            sendReq.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, buffer));

            using var activity = NetIceActivitySource.StartStunMessageSentActivity(sendReq, dstEndPoint, isRelayed: true);
            activity?.SetRelayEndpoint(relayEndPoint);

            var bufferSize = sendReq.GetByteBufferSize(default, addFingerprint: false);
            var rentedStunBufferOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            var memory = rentedStunBufferOwner.Memory.Slice(0, bufferSize);

            sendReq.WriteToBuffer(memory.Span, ReadOnlySpan<byte>.Empty, addFingerprint: false);

            var sendResult = GetIceServerConection(iceServer).SendTo(relayEndPoint, memory, rentedStunBufferOwner);

            if (sendResult != SocketError.Success)
            {
                logger.LogTurnRelayError(relayEndPoint, sendResult);
            }
            else
            {
                OnStunMessageSent?.Invoke(sendReq, relayEndPoint, true);
            }

            return sendResult;
        }
    }

    /// <summary>
    /// Sends the STUN request. Optionally adds the authentication fields.
    /// </summary>
    /// <seealso href="https://tools.ietf.org/html/rfc5389#section-15.4"/>
    private SocketError SendStunMessage(STUNMessage stunMessage, IceServer iceServer)
    {
        if (!iceServer.MessageIntegrityKey.IsEmpty)
        {
            // https://tools.ietf.org/html/rfc5389#section-15.4

            stunMessage.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, iceServer.Nonce));
            stunMessage.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, iceServer.Realm));
            stunMessage.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, iceServer.Username));
        }

        var messageIntegrityKeySpan = iceServer.MessageIntegrityKey.Span;

        var bufferSize = stunMessage.GetByteBufferSize(messageIntegrityKeySpan, addFingerprint: true);
        var rentedMemory = MemoryPool<byte>.Shared.Rent(bufferSize);

        var requestBytes = rentedMemory.Memory.Slice(0, bufferSize);
        stunMessage.WriteToBuffer(requestBytes.Span, messageIntegrityKeySpan, addFingerprint: true);
        Debug.Assert(iceServer.ServerEndPoint is { });
        return GetIceServerConection(iceServer).SendTo(iceServer.ServerEndPoint, requestBytes, rentedMemory);
    }

    private SocketConnection GetIceServerConection(IceServer iceServer)
    {
        if (!m_iceServerConnections.TryGetValue(iceServer.Uri, out var iceServerConnection))
        {
            throw new InvalidOperationException($"Invalid ICE server: {iceServer.Uri}");
        }

        return iceServerConnection;
    }

    private async Task<IPAddress[]> ResolveMdnsName(RTCIceCandidate candidate)
    {
        Debug.Assert(candidate.address is { });

        if (MdnsGetAddresses is { } mdnsGetAddresses)
        {
            if (MdnsResolve is { })
            {
                logger.LogIceMdnsBothSet();
            }

            return await mdnsGetAddresses(candidate.address).ConfigureAwait(false);
        }

        if (MdnsResolve is { } mdsnResolve)
        {
            var address = await mdsnResolve(candidate.address).ConfigureAwait(false);
            return address is { } ? new IPAddress[] { address } : Array.Empty<IPAddress>();
        }


        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(candidate.address).ConfigureAwait(false);
        }
        catch (SocketException e)
        {
            logger.LogMdnsResolutionError(candidate.address, e);
            return Array.Empty<IPAddress>();
        }
        catch (ArgumentException e)
        {
            logger.LogMdnsNameError(candidate.address, e);
            return Array.Empty<IPAddress>();
        }

        if (addresses.Length == 0)
        {
            logger.LogTcpAddress(candidate.address);
            // Supporting MDNS lookups means an additional nuget dependency. Hopefully
            // support is coming to .Net Core soon (AC 12 Jun 2020).
            OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate has an unsupported MDNS hostname {candidate.address}.");
        }
        return addresses;
    }

    /// <summary>
    /// The send method for the RTP ICE channel. The sole purpose of this overload is to package up
    /// sends that need to be relayed via a TURN server. If the connected channel is not a relay then
    /// the send can be passed straight through to the underlying RTP channel.
    /// </summary>
    /// <param name="sendOn">The socket to send on. Can be the RTP or Control socket.</param>
    /// <param name="dstEndPoint">The destination end point to send to.</param>
    /// <param name="buffer">The data to send.</param>
    /// <param name="memoryOwner">The onwer of the <paramref name="buffer"/> memory.</param>
    /// <returns>The result of initiating the send. This result does not reflect anything about
    /// whether the remote party received the packet or not.</returns>
    public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner = null)
    {
        if (NominatedEntry is
            {
                LocalCandidate:
                {
                    type: RTCIceCandidateType.relay,
                    IceServer: { } iceServer
                },
                RemoteCandidate:
                {
                    DestinationEndPoint: { } remoteEndPoint
                }
            } &&
            remoteEndPoint.Port == dstEndPoint.Port &&
            remoteEndPoint.Address.Equals(dstEndPoint.Address))
        {
            // A TURN relay channel is being used to communicate with the remote peer.
            Debug.Assert(iceServer.ServerEndPoint is { });
            return SendRelay(
                iceServer.Protocol,
                dstEndPoint,
                buffer,
                memoryOwner,
                iceServer.ServerEndPoint,
                iceServer);
        }
        else
        {
            return base.Send(sendOn, dstEndPoint, buffer, memoryOwner);
        }
    }
}
