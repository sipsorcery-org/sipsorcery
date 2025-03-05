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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.UnitTests")]

namespace SIPSorcery.Net
{
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
    public class RtpIceChannel : RTPChannel
    {
        private static DnsClient.LookupClient _dnsLookupClient;

        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;
        private const int MAX_CHECKLIST_ENTRIES = 25;       // Maximum number of entries that can be added to the checklist of candidate pairs.
        private const string MDNS_TLD = ".local";           // Top Level Domain name for multicast lookups as per RFC6762.
        private const int CONNECTED_CHECK_PERIOD = 3;       // The period in seconds to send STUN connectivity checks once connected. 
        public const string SDP_MID = "0";
        public const int SDP_MLINE_INDEX = 0;

        public class IceTcpReceiver : UdpReceiver
        {
            protected const int REVEIVE_TCP_BUFFER_SIZE = RECEIVE_BUFFER_SIZE * 2;

            protected int m_recvOffset;
            public IceTcpReceiver(Socket socket, int mtu = REVEIVE_TCP_BUFFER_SIZE) : base(socket, mtu)
            {
                m_recvOffset = 0;
            }

            /// <summary>
            /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
            /// return any data received.
            /// </summary>
            public override void BeginReceiveFrom()
            {
                //Prevent call BeginReceiveFrom if it is already running or into invalid state
                if ((m_isClosed || !m_socket.Connected) && m_isRunningReceive)
                {
                    m_isRunningReceive = false;
                }
                if (m_isRunningReceive || m_isClosed || !m_socket.Connected)
                {
                    return;
                }

                try
                {
                    m_isRunningReceive = true;
                    EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    var recvLength = m_recvBuffer.Length - m_recvOffset;
                    //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
                    if (recvLength <= 0 || m_recvOffset < 0)
                    {
                        m_recvOffset = 0;
                        recvLength = m_recvBuffer.Length;
                    }
                    m_socket.BeginReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
                }
                // Thrown when socket is closed. Can be safely ignored.
                // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
                // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
                // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
                catch (ObjectDisposedException)
                {
                    m_isRunningReceive = false;
                }
                catch (SocketException sockExcp)
                {
                    m_isRunningReceive = false;
                    logger.LogWarning(sockExcp, "Socket error {SocketErrorCode} in IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
                    //Close(sockExcp.Message);
                }
                catch (Exception excp)
                {
                    m_isRunningReceive = false;
                    // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
                    // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                    // case the socket can be considered to be unusable and there's no point trying another receive.
                    logger.LogError(excp, "Exception IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", excp.Message);
                    Close(excp.Message);
                }
            }

            /// <summary>
            /// Handler for end of the begin receive call.
            /// </summary>
            /// <param name="ar">Contains the results of the receive.</param>
            protected override void EndReceiveFrom(IAsyncResult ar)
            {
                try
                {
                    // When socket is closed the object will be disposed of in the middle of a receive.
                    if (!m_isClosed)
                    {
                        EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                        int bytesRead = m_socket.EndReceiveFrom(ar, ref remoteEP);

                        if (bytesRead > 0)
                        {
                            ProcessRawBuffer(bytesRead + m_recvOffset, remoteEP as IPEndPoint);
                        }
                    }

                    // If there is still data available it should be read now. This is more efficient than calling
                    // BeginReceiveFrom which will incur the overhead of creating the callback and then immediately firing it.
                    // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
                    // will be called synchronously (if data is available it calls the callback method immediately) which can
                    // create a very nasty stack.
                    if (!m_isClosed && m_socket.Available > 0)
                    {
                        while (!m_isClosed && m_socket.Available > 0)
                        {
                            EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                            var recvLength = m_recvBuffer.Length - m_recvOffset;
                            //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
                            if (recvLength <= 0 || m_recvOffset < 0)
                            {
                                m_recvOffset = 0;
                                recvLength = m_recvBuffer.Length;
                            }
                            int bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref remoteEP);

                            if (bytesReadSync > 0)
                            {
                                if (ProcessRawBuffer(bytesReadSync + m_recvOffset, remoteEP as IPEndPoint) == 0)
                                {
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
                catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Thrown when close is called on a socket from this end. Safe to ignore.
                }
                catch (SocketException sockExcp)
                {
                    // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                    // normal RTP operation. For example:
                    // - the RTP connection may start sending before the remote socket starts listening,
                    // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                    //   or new socket during the transition.
                    // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
                    // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
                    // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
                    // error since the remote party may not have initialised their socket yet.
                    logger.LogWarning(sockExcp, "SocketException IceTcpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
                }
                catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
                { }
                catch (Exception excp)
                {
                    logger.LogError(excp, "Exception IceTcpReceiver.EndReceiveFrom. {ErrorMessage}", excp.Message);
                    Close(excp.Message);
                }
                finally
                {
                    m_isRunningReceive = false;
                    if (!m_isClosed)
                    {
                        BeginReceiveFrom();
                    }
                }
            }

            // TODO: If we miss any package because slow internet connection
            // and initial byte in buffer is not a STUNHeader (starts with 0x00 0x00)
            // and our receive buffer is full, we need a way to discard whole buffer
            // or check for 0x00 0x00 start again.
            protected virtual int ProcessRawBuffer(int bytesRead, IPEndPoint remoteEP)
            {
                var extractCount = 0;
                if (bytesRead > 0)
                {
                    // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
                    // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
                    // is relying on getting the local IP address on Linux then something may fail.
                    //if (packetInfo != null && packetInfo.Address != null)
                    //{
                    //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
                    //}

                    //Try extract all StunMessages from current receive buffer
                    var isFragmented = true;
                    var recvRemainingSegment = new ArraySegment<byte>(m_recvBuffer, 0, bytesRead);

                    while (recvRemainingSegment.Count > STUNHeader.STUN_HEADER_LENGTH)
                    {
                        isFragmented = false;
                        STUNHeader header = null;
                        try
                        {
                            header = STUNHeader.ParseSTUNHeader(recvRemainingSegment);
                        }
                        catch
                        {
                            header = null;
                        }
                        if (header != null)
                        {
                            int stunMsgBytes = STUNHeader.STUN_HEADER_LENGTH + header.MessageLength;
                            if (stunMsgBytes % 4 != 0)
                            {
                                stunMsgBytes = stunMsgBytes - (stunMsgBytes % 4) + 4;
                            }

                            //We have the packet count all inside current receiving buffer
                            if (recvRemainingSegment.Count >= stunMsgBytes)
                            {
                                extractCount++;
                                m_recvOffset = recvRemainingSegment.Offset + recvRemainingSegment.Count;

                                byte[] packetBuffer = new byte[stunMsgBytes];
                                Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, packetBuffer, 0, stunMsgBytes);

                                CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP, packetBuffer);

                                var newOffset = recvRemainingSegment.Offset + stunMsgBytes;
                                var newCount = recvRemainingSegment.Count - stunMsgBytes;
                                if (newCount > STUNHeader.STUN_HEADER_LENGTH && newOffset >= 0)
                                {
                                    recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                                }
                                else
                                {
                                    if (newCount > 0 && newOffset >= 0)
                                    {
                                        recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                                        isFragmented = true;
                                    }
                                    else
                                    {
                                        recvRemainingSegment = new ArraySegment<byte>();
                                        isFragmented = false;
                                    }
                                    break;
                                }
                            }
                            //We have a fragmentation but the header is intact, we need to cache the fragmentation for the next receive cycle
                            else
                            {
                                isFragmented = true;
                                break;
                            }
                        }
                        //Save Remaining Buffer in start of m_recvBuffer
                        else
                        {
                            isFragmented = true;
                            break;
                        }
                    }

                    if (isFragmented)
                    {
                        m_recvOffset = recvRemainingSegment.Count;
                        Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, m_recvBuffer, 0, recvRemainingSegment.Count);
                    }
                    else
                    {
                        m_recvOffset = 0;
                    }
                }

                return extractCount;
            }

            /// <summary>
            /// Closes the socket and stops any new receives from being initiated.
            /// </summary>
            public override void Close(string reason)
            {
                if (!m_isClosed)
                {
                    if (m_socket != null && m_socket.Connected)
                    {
                        m_socket?.Disconnect(false);
                    }
                    base.Close(reason);
                }
            }
        }

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

        private IPAddress _bindAddress;
        private List<RTCIceServer> _iceServers;
        private RTCIceTransportPolicy _policy;

        private DateTime _startedGatheringAt = DateTime.MinValue;
        private DateTime _connectedAt = DateTime.MinValue;

        internal ConcurrentDictionary<STUNUri, IceServer> _iceServerConnections;

        private IceServer _activeIceServer;

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
                return _candidates.ToList();
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
        internal RTCIceCandidate _relayChecklistCandidate;

        /// <summary>
        /// If the connectivity checks are successful this will hold the entry that was 
        /// nominated by the connection check process.
        /// </summary>
        public ChecklistEntry NominatedEntry { get; private set; }

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
                    return Math.Max(500, Ta * Candidates.Count(x => x.type == RTCIceCandidateType.srflx || x.type == RTCIceCandidateType.relay));
                }
                else
                {
                    lock (_checklistLock)
                    {
                        return Math.Max(500, Ta * (_checklist.Count(x => x.State == ChecklistEntryState.Waiting) + _checklist.Count(x => x.State == ChecklistEntryState.InProgress)));
                    }
                }
            }
        }

        public readonly string LocalIceUser;
        public readonly string LocalIcePassword;
        public string RemoteIceUser { get; private set; }
        public string RemoteIcePassword { get; private set; }

        private bool _closed = false;
        private Timer _connectivityChecksTimer;
        private Timer _processIceServersTimer;
        private Timer _refreshTurnTimer;
        private DateTime _checklistStartedAt = DateTime.MinValue;
        private bool _includeAllInterfaceAddresses = false;
        private ulong _iceTiebreaker;

        public event Action<RTCIceCandidate> OnIceCandidate;
        public event Action<RTCIceConnectionState> OnIceConnectionStateChange;
        public event Action<RTCIceGatheringState> OnIceGatheringStateChange;
        public event Action<RTCIceCandidate, string> OnIceCandidateError;

        public static List<DnsClient.NameServer> DefaultNameServers { get; set; }

        /// <summary>
        /// This event gets fired when a STUN message is received by this channel.
        /// The event is for diagnostic purposes only.
        /// Parameters:
        ///  - STUNMessage: The received STUN message.
        ///  - IPEndPoint: The remote end point the STUN message was received from.
        ///  - bool: True if the message was received via a TURN server relay.
        /// </summary>
        public event Action<STUNMessage, IPEndPoint, bool> OnStunMessageReceived;

        /// <summary>
        /// This event gets fired when a STUN message is sent by this channel.
        /// The event is for diagnostic purposes only.
        /// Parameters:
        ///  - STUNMessage: The STUN message that was sent.
        ///  - IPEndPoint: The remote end point the STUN message was sent to.
        ///  - bool: True if the message was sent via a TURN server relay.
        /// </summary>
        public event Action<STUNMessage, IPEndPoint, bool> OnStunMessageSent;

        public new event Action<int, IPEndPoint, byte[]> OnRTPDataReceived;

        /// <summary>
        /// An optional callback function to resolve remote ICE candidates with MDNS hostnames.
        /// </summary>
        /// <remarks>
        /// The order is <see cref="MdnsGetAddresses"/>, then <see cref="MdnsResolve"/>.
        /// If both are null system <see cref="Dns">DNS resolver</see> will be used.
        /// </remarks>
        public Func<string, Task<IPAddress>> MdnsResolve;

        /// <summary>
        /// An optional callback function to resolve remote ICE candidates with MDNS hostnames.
        /// </summary>
        /// <remarks>
        /// The order is <see cref="MdnsGetAddresses"/>, then <see cref="MdnsResolve"/>.
        /// If both are null system <see cref="Dns">DNS resolver</see> will be used.
        /// </remarks>
        public Func<string, Task<IPAddress[]>> MdnsGetAddresses;

        public Dictionary<STUNUri, Socket> RtpTcpSocketByUri { get; private set; } = new Dictionary<STUNUri, Socket>();

        protected Dictionary<STUNUri, IceTcpReceiver> m_rtpTcpReceiverByUri = new Dictionary<STUNUri, IceTcpReceiver>();

        private bool m_tcpRtpReceiverStarted = false;

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
            IPAddress bindAddress,
            RTCIceComponent component,
            List<RTCIceServer> iceServers = null,
            RTCIceTransportPolicy policy = RTCIceTransportPolicy.all,
            bool includeAllInterfaceAddresses = false,
            int bindPort = 0,
            PortRange rtpPortRange = null) :
            base(false, bindAddress, bindPort, rtpPortRange)
        {
            _bindAddress = bindAddress;
            Component = component;
            _iceServers = iceServers != null ? new List<RTCIceServer>(iceServers) : null;
            _policy = policy;
            _includeAllInterfaceAddresses = includeAllInterfaceAddresses;
            _iceTiebreaker = Crypto.GetRandomULong();

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
                base.RTPLocalEndPoint.Address,
                (ushort)base.RTPLocalEndPoint.Port,
                RTCIceCandidateType.host,
                null,
                0);

            // Create TCP Socket to implement TURN Control
            // Take a note that TURN Control will only use TCP for CreatePermissions/Allocate/BindRequests/Data
            // Ice Candidates returned by relay will always be UDP based.
            var tcpIceServers = _iceServers != null ?
                                    _iceServers.FindAll(a =>
                                       a != null &&
                                       (a.urls.Contains(STUNUri.SCHEME_TRANSPORT_TCP) ||
                                       a.urls.Contains(STUNUri.SCHEME_TRANSPORT_TLS))) :
                                    new List<RTCIceServer>();
            var supportTcp = tcpIceServers != null && tcpIceServers.Count > 0;
            if (supportTcp)
            {
                // Init one TCP Socket per IceServer as we need to connect to properly use a TcpSocket (unfortunately).
                RtpTcpSocketByUri = new Dictionary<STUNUri, Socket>();
                foreach (var tcpIceServer in tcpIceServers)
                {
                    var serverUrl = tcpIceServer.urls;
                    STUNUri.TryParse(serverUrl, out STUNUri uri);
                    if (uri != null && !RtpTcpSocketByUri.ContainsKey(uri))
                    {

                        if (uri != null)
                        {
                            NetServices.CreateRtpSocket(false, ProtocolType.Tcp, bindAddress, bindPort, rtpPortRange, true, true, out var rtpTcpSocket, out _);

                            if (rtpTcpSocket == null)
                            {
                                throw new ApplicationException("The RTP channel was not able to create an RTP socket.");
                            }


                            RtpTcpSocketByUri.Add(uri, rtpTcpSocket);
                        }
                    }
                }
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
                if (_iceServers != null)
                {
                    InitialiseIceServers(_iceServers);

                    // DNS is only needed if there are ICE server hostnames to lookup.
                    if (_dnsLookupClient == null && _iceServerConnections.Any(x => !IPAddress.TryParse(x.Key.Host, out _)))
                    {
                        if (DefaultNameServers != null)
                        {
                            _dnsLookupClient = new DnsClient.LookupClient(DefaultNameServers.ToArray());
                        }
                        else
                        {
                            _dnsLookupClient = new DnsClient.LookupClient();
                        }
                    }
                }

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

                logger.LogDebug("RTP ICE Channel discovered {CandidateCount} local candidates.", _candidates.Count);

                if (_iceServerConnections?.Count > 0)
                {
                    InitialiseIceServers(_iceServers);
                    _processIceServersTimer = new Timer(CheckIceServers, null, 0, Ta);
                }
                else
                {
                    // If there are no ICE servers then gathering has finished.
                    IceGatheringState = RTCIceGatheringState.complete;
                    OnIceGatheringStateChange?.Invoke(IceGatheringState);
                }

                _connectivityChecksTimer = new Timer(DoConnectivityCheck, null, 0, Ta);
            }
        }

        protected void StartTcpRtpReceiver()
        {
            if (!m_tcpRtpReceiverStarted && RtpTcpSocketByUri != null && RtpTcpSocketByUri.Count > 0)
            {
                m_tcpRtpReceiverStarted = true;

                // Create TCP Receivers by Tcp Sockets
                m_rtpTcpReceiverByUri = new Dictionary<STUNUri, IceTcpReceiver>();
                foreach (var pair in RtpTcpSocketByUri)
                {
                    var stunUri = pair.Key;
                    var tcpSocket = pair.Value;

                    if (stunUri != null && !m_rtpTcpReceiverByUri.ContainsKey(stunUri) && tcpSocket != null)
                    {
                        var rtpTcpReceiver = new IceTcpReceiver(tcpSocket);

                        Action<string> onClose = (reason) =>
                        {
                            CloseTcp(rtpTcpReceiver, reason);
                        };
                        rtpTcpReceiver.OnPacketReceived += OnRTPPacketReceived;
                        rtpTcpReceiver.OnClosed += onClose;
                        rtpTcpReceiver.BeginReceiveFrom();

                        m_rtpTcpReceiverByUri.Add(stunUri, rtpTcpReceiver);
                    }
                }

                logger.LogDebug("RTPIceChannel TCP for {LocalEndPoint} started.", RtpSocket.LocalEndPoint);

                OnClosed -= CloseTcp;
                OnClosed += CloseTcp;
            }
        }

        protected void CloseTcp(string reason)
        {
            if (m_rtpTcpReceiverByUri != null)
            {
                foreach (var pair in m_rtpTcpReceiverByUri)
                {
                    CloseTcp(pair.Value, reason);
                }
            }
        }

        protected void CloseTcp(IceTcpReceiver target, string reason)
        {
            try
            {
                if (target != null && !target.IsClosed)
                {
                    target?.Close(null);
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception RTPChannel.Close. {ErrorMessage}", excp.Message);
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
            logger.LogDebug("RTP ICE Channel remote credentials set.");

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
                logger.LogDebug("RtpIceChannel for {RTPLocalEndPoint} closed.", base.RTPLocalEndPoint);
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
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.address))
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
            else if (candidate.port <= 0 || candidate.port > IPEndPoint.MaxPort)
            {
                OnIceCandidateError?.Invoke(candidate, $"Remote ICE candidate had an invalid port {candidate.port}.");
            }
            else if(IPAddress.TryParse(candidate.address, out var addrIPv6) &&
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

                logger.LogDebug("RTP ICE Channel received remote candidate: {candidate}", candidate);

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
            _iceServerConnections?.Clear();
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
            List<RTCIceCandidate> hostCandidates = new List<RTCIceCandidate>();
            RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

            // RFC8445 states that loopback addresses should not be included in
            // host candidates. If the provided bind address is a loopback
            // address it means no host candidates will be gathered. To avoid this
            // set the desired interface address to the Internet facing address
            // in the event a loopback address was specified.
            //if (_bindAddress != null &&
            //    (IPAddress.IsLoopback(_bindAddress) ||
            //    IPAddress.Any.Equals(_bindAddress) ||
            //    IPAddress.IPv6Any.Equals(_bindAddress)))
            //{
            //    // By setting to null means the default Internet facing interface will be used.
            //    signallingDstAddress = null;
            //}

            var rtpBindAddress = base.RTPLocalEndPoint.Address;

            // We get a list of local addresses that can be used with the address the RTP socket is bound on.
            List<IPAddress> localAddresses = null;
            if (IPAddress.IPv6Any.Equals(rtpBindAddress))
            {
                if (base.RtpSocket.DualMode)
                {
                    // IPv6 dual mode listening on [::] means we can use all valid local addresses.
                    localAddresses = NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses)
                        .Where(x => !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal && !x.IsIPv6LinkLocal).ToList();
                }
                else
                {
                    // IPv6 but not dual mode on [::] means can use all valid local IPv6 addresses.
                    localAddresses = NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses)
                        .Where(x => x.AddressFamily == AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal && !x.IsIPv6LinkLocal).ToList();
                }
            }
            else if (IPAddress.Any.Equals(rtpBindAddress))
            {
                // IPv4 on 0.0.0.0 means can use all valid local IPv4 addresses.
                localAddresses = NetServices.GetLocalAddressesOnInterface(_bindAddress, _includeAllInterfaceAddresses)
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

        /// <summary>
        /// Initialises the ICE servers if any were provided in the initial configuration.
        /// ICE servers are STUN and TURN servers and are used to gather "server reflexive"
        /// and "relay" candidates. If the transport policy is "relay only" then only TURN 
        /// servers will be added to the list of ICE servers being checked.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        private void InitialiseIceServers(List<RTCIceServer> iceServers)
        {
            _iceServerConnections = new ConcurrentDictionary<STUNUri, IceServer>();

            int iceServerID = IceServer.MINIMUM_ICE_SERVER_ID;

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
                                logger.LogWarning("ICE channel does not currently support TLS for STUN and TURN servers, not checking {stunUri}.", stunUri);
                            }
                            else if (_policy == RTCIceTransportPolicy.relay && stunUri.Scheme == STUNSchemesEnum.stun)
                            {
                                logger.LogWarning("ICE channel policy is relay only, ignoring STUN server {stunUri}.", stunUri);
                            }
                            else if (!_iceServerConnections.ContainsKey(stunUri))
                            {
                                logger.LogDebug("Adding ICE server for {stunUri}.", stunUri);

                                var iceServerState = new IceServer(stunUri, iceServerID, iceServer.username, iceServer.credential);

                                // Check whether the server end point can be set. IF it can't a DNS lookup will be required.
                                if (IPAddress.TryParse(iceServerState._uri.Host, out var serverIPAddress))
                                {
                                    iceServerState.ServerEndPoint = new IPEndPoint(serverIPAddress, iceServerState._uri.Port);
                                    logger.LogDebug("ICE server end point for {Uri} set to {EndPoint}.", iceServerState._uri, iceServerState.ServerEndPoint);
                                }

                                _iceServerConnections.TryAdd(stunUri, iceServerState);

                                iceServerID++;
                                if (iceServerID > IceServer.MAXIMUM_ICE_SERVER_ID)
                                {
                                    logger.LogWarning("The maximum number of ICE servers for the session has been reached.");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            logger.LogWarning("RTP ICE Channel could not parse ICE server URL {url}.", url);
                        }
                    }
                }
            }
        }

        private void RefreshTurn(Object state)
        {
            try
            {
                if (_closed)
                {
                    return;
                }

                if (NominatedEntry == null || _activeIceServer == null)
                {
                    return;
                }
                if (_activeIceServer._uri.Scheme != STUNSchemesEnum.turn || NominatedEntry.LocalCandidate.IceServer is null)
                {
                    _refreshTurnTimer?.Dispose();
                    return;
                }
                if (_activeIceServer.TurnTimeToExpiry.Subtract(DateTime.Now) <= TimeSpan.FromMinutes(1))
                {
                    logger.LogDebug("Sending TURN refresh request to ICE server {Uri}.", _activeIceServer._uri);
                    _activeIceServer.Error = SendTurnRefreshRequest(_activeIceServer);
                }

                if (NominatedEntry.TurnPermissionsRequestSent >= IceServer.MAX_REQUESTS)
                {
                    logger.LogWarning("ICE RTP channel failed to get a Create Permissions response from {IceServerUri} after {TurnPermissionsRequestSent} attempts.", NominatedEntry.LocalCandidate.IceServer._uri, NominatedEntry.TurnPermissionsRequestSent);
                }
                else if (NominatedEntry.TurnPermissionsRequestSent != 1 || NominatedEntry.TurnPermissionsResponseAt == DateTime.MinValue || DateTime.Now.Subtract(NominatedEntry.TurnPermissionsResponseAt).TotalSeconds >
                         REFRESH_PERMISSION_PERIOD)
                {
                    // Send Create Permissions request to TURN server for remote candidate.
                    NominatedEntry.TurnPermissionsRequestSent++;
                    logger.LogDebug("ICE RTP channel sending TURN permissions request {TurnPermissionsRequestSent} to server {IceServerUri} for peer {RemoteCandidate} (TxID: {RequestTransactionID}).",
                        NominatedEntry.TurnPermissionsRequestSent, NominatedEntry.LocalCandidate.IceServer._uri, NominatedEntry.RemoteCandidate.DestinationEndPoint, NominatedEntry.RequestTransactionID);
                    SendTurnCreatePermissionsRequest(NominatedEntry.RequestTransactionID, NominatedEntry.LocalCandidate.IceServer, NominatedEntry.RemoteCandidate.DestinationEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception " + nameof(RefreshTurn) + ". {ErrorMessage}", excp);
            }
        }

        /// <summary>
        /// Checks the list of ICE servers to perform STUN binding or TURN reservation requests.
        /// Only one of the ICE server entries should end up being used. If at least one TURN server
        /// is provided it will take precedence as it can potentially supply both Server Reflexive 
        /// and Relay candidates.
        /// </summary>
        private void CheckIceServers(Object state)
        {
            if (_closed || IceGatheringState == RTCIceGatheringState.complete ||
                !(IceConnectionState == RTCIceConnectionState.@new || IceConnectionState == RTCIceConnectionState.checking))
            {
                logger.LogDebug("ICE RTP channel stopping ICE server checks in gathering state {IceGatheringState} and connection state {IceConnectionState}.", IceGatheringState, IceConnectionState);
                _refreshTurnTimer?.Dispose();
                _refreshTurnTimer = new Timer(RefreshTurn, null, 0, 2000);
                _processIceServersTimer.Dispose();
                return;
            }

            // The lock is to ensure the timer callback doesn't run multiple instances in parallel. 
            if (Monitor.TryEnter(_iceServerConnections))
            {
                try
                {
                    if (_activeIceServer == null || _activeIceServer.Error != SocketError.Success)
                    {
                        if (_iceServerConnections.Count(x => x.Value.Error == SocketError.Success) == 0)
                        {
                            logger.LogDebug("RTP ICE Channel all ICE server connection checks failed, stopping ICE servers timer.");
                            _processIceServersTimer.Dispose();
                        }
                        else
                        {
                            // Select the next server to check.
                            var entry = _iceServerConnections
                            .Where(x => x.Value.Error == SocketError.Success)
                            .OrderByDescending(x => x.Value._uri.Scheme) // TURN serves take priority.
                            .FirstOrDefault();

                            if (!entry.Equals(default(KeyValuePair<STUNUri, IceServer>)))
                            {
                                _activeIceServer = entry.Value;
                            }
                            else
                            {
                                logger.LogDebug("RTP ICE Channel was not able to set an active ICE server, stopping ICE servers timer.");
                                _processIceServersTimer.Dispose();
                            }
                        }
                    }

                    // Run a state machine on the active ICE server.

                    // Something went wrong. An active server could not be set.
                    if (_activeIceServer == null)
                    {
                        logger.LogDebug("RTP ICE Channel was not able to acquire an active ICE server, stopping ICE servers timer.");
                        _processIceServersTimer.Dispose();
                    }
                    else if ((_activeIceServer._uri.Scheme == STUNSchemesEnum.turn && _activeIceServer.RelayEndPoint != null) ||
                        (_activeIceServer._uri.Scheme == STUNSchemesEnum.stun && _activeIceServer.ServerReflexiveEndPoint != null))
                    {
                        // Successfully set up the ICE server. Do nothing.
                    }
                    // If the ICE server hasn't yet been resolved initiate the DNS check.
                    else if (_activeIceServer.ServerEndPoint == null && _activeIceServer.DnsLookupSentAt == DateTime.MinValue)
                    {
                        logger.LogDebug("Attempting to resolve STUN server URI {Uri}.", _activeIceServer._uri);

                        _activeIceServer.DnsLookupSentAt = DateTime.Now;

                        // Don't stop and wait for DNS. Let the timer callback complete and check for the DNS
                        // result on the next few timer callbacks.
                        Task.Run(async () =>
                        {
                            try
                            {
                                var result = await STUNDns.Resolve(_activeIceServer._uri).ConfigureAwait(false);
                                logger.LogDebug("ICE server {Uri} successfully resolved to {Result}.", _activeIceServer._uri, result);
                                _activeIceServer.ServerEndPoint = result;
                            }
                            catch
                            {
                                logger.LogWarning("Unable to resolve ICE server end point for {Uri}.", _activeIceServer._uri);
                                _activeIceServer.Error = SocketError.HostNotFound;
                            }
                        });
                    }
                    // Waiting for DNS lookup to complete.
                    else if (_activeIceServer.ServerEndPoint == null &&
                        DateTime.Now.Subtract(_activeIceServer.DnsLookupSentAt).TotalSeconds < IceServer.DNS_LOOKUP_TIMEOUT_SECONDS)
                    {
                        // Do nothing.
                    }
                    // DNS lookup for ICE server host has timed out.
                    else if (_activeIceServer.ServerEndPoint == null)
                    {
                        logger.LogWarning("ICE server DNS resolution failed for {Uri}.", _activeIceServer._uri);
                        _activeIceServer.Error = SocketError.TimedOut;
                    }
                    // Maximum number of requests have been sent to the ICE server without a response.
                    else if (_activeIceServer.OutstandingRequestsSent >= IceServer.MAX_REQUESTS && _activeIceServer.LastResponseReceivedAt == DateTime.MinValue)
                    {
                        logger.LogWarning("Connection attempt to ICE server {Uri} timed out after {RequestsSent} requests.", _activeIceServer._uri, _activeIceServer.OutstandingRequestsSent);
                        _activeIceServer.Error = SocketError.TimedOut;
                    }
                    // Maximum number of error response have been received for the requests sent to this ICE server.
                    else if (_activeIceServer.ErrorResponseCount >= IceServer.MAX_ERRORS)
                    {
                        logger.LogWarning("Connection attempt to ICE server {Uri} cancelled after {ErrorResponseCount} error responses.", _activeIceServer._uri, _activeIceServer.ErrorResponseCount);
                        _activeIceServer.Error = SocketError.TimedOut;
                    }
                    // Send STUN binding request.
                    else if (_activeIceServer.ServerReflexiveEndPoint == null && _activeIceServer._uri.Scheme == STUNSchemesEnum.stun)
                    {
                        logger.LogDebug("Sending STUN binding request to ICE server {Uri} with address {EndPoint}.", _activeIceServer._uri, _activeIceServer.ServerEndPoint);
                        _activeIceServer.Error = SendStunBindingRequest(_activeIceServer);
                    }
                    // Send TURN binding request.
                    else if (_activeIceServer.ServerReflexiveEndPoint == null && _activeIceServer._uri.Scheme == STUNSchemesEnum.turn)
                    {
                        logger.LogDebug("Sending TURN allocate request to ICE server {Uri} with address {EndPoint}.", _activeIceServer._uri, _activeIceServer.ServerEndPoint);
                        _activeIceServer.Error = SendTurnAllocateRequest(_activeIceServer);
                    }
                    else
                    {
                        logger.LogWarning("The active ICE server reached an unexpected state {Uri}.", _activeIceServer._uri);
                    }
                }
                finally
                {
                    Monitor.Exit(_iceServerConnections);
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
            RTCIceCandidateInit init = new RTCIceCandidateInit
            {
                usernameFragment = LocalIceUser,
                sdpMid = SDP_MID,
                sdpMLineIndex = SDP_MLINE_INDEX,
            };

            if (iceServer.ServerReflexiveEndPoint != null)
            {
                RTCIceCandidate svrRflxCandidate = iceServer.GetCandidate(init, RTCIceCandidateType.srflx);

                if (_policy == RTCIceTransportPolicy.all && svrRflxCandidate != null)
                {
                    logger.LogDebug("Adding server reflex ICE candidate for ICE server {Uri} and {EndPoint}.", iceServer._uri, iceServer.ServerReflexiveEndPoint);

                    // Note server reflexive candidates don't update the checklist pairs since it's merely an
                    // alternative way to represent an existing host candidate.
                    _candidates.Add(svrRflxCandidate);
                    OnIceCandidate?.Invoke(svrRflxCandidate);
                }
            }

            if (_relayChecklistCandidate == null && iceServer.RelayEndPoint != null)
            {
                RTCIceCandidate relayCandidate = iceServer.GetCandidate(init, RTCIceCandidateType.relay);
                relayCandidate.SetDestinationEndPoint(iceServer.RelayEndPoint);

                // A local relay candidate is stored so it can be pared with any remote candidates
                // that arrive after the checklist update carried out in this method.
                _relayChecklistCandidate = relayCandidate;

                if (relayCandidate != null)
                {
                    logger.LogDebug("Adding relay ICE candidate for ICE server {Uri} and {EndPoint}.", iceServer._uri, iceServer.RelayEndPoint);

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
            if (localCandidate == null)
            {
                logger.LogError(nameof(UpdateChecklist) + " the local candidate supplied to UpdateChecklist was null.");
                return;
            }
            else if (remoteCandidate == null)
            {
                logger.LogError(nameof(UpdateChecklist) + " the remote candidate supplied to UpdateChecklist was null.");
                return;
            }

            // This methd is called in a fire and forget fashion so any exceptions need to be handled here.
            try
            {
                // Attempt to resolve the remote candidate address.
                if (!IPAddress.TryParse(remoteCandidate.address, out var remoteCandidateIPAddr))
                {
                    if (remoteCandidate.address.ToLower().EndsWith(MDNS_TLD))
                    {
                        var addresses = await ResolveMdnsName(remoteCandidate).ConfigureAwait(false);
                        if (addresses.Length == 0)
                        {
                            logger.LogWarning("RTP ICE channel MDNS resolver failed to resolve {RemoteCandidateAddress}.", remoteCandidate.address);
                        }
                        else
                        {
                            remoteCandidateIPAddr = addresses[0];
                            logger.LogDebug("RTP ICE channel resolved MDNS hostname {RemoteCandidateAddress} to {RemoteCandidateIPAddr}.", remoteCandidate.address, remoteCandidateIPAddr);

                            var remoteEP = new IPEndPoint(remoteCandidateIPAddr, remoteCandidate.port);
                            remoteCandidate.SetDestinationEndPoint(remoteEP);
                        }
                    }
                    else
                    {
                        // The candidate string can be a hostname or an IP address.
                        var lookupResult = await _dnsLookupClient.QueryAsync(remoteCandidate.address, DnsClient.QueryType.A).ConfigureAwait(false);

                        if (lookupResult.Answers.Count > 0)
                        {
                            remoteCandidateIPAddr = lookupResult.Answers.AddressRecords().FirstOrDefault()?.Address;
                            logger.LogWarning("RTP ICE channel resolved remote candidate {RemoteCandidateAddress} to {RemoteCandidateIPAddr}.", remoteCandidate.address, remoteCandidateIPAddr);
                        }
                        else
                        {
                            logger.LogDebug("RTP ICE channel failed to resolve remote candidate {RemoteCandidateAddress}.", remoteCandidate.address);
                        }

                        if (remoteCandidateIPAddr != null)
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
                if (remoteCandidate.DestinationEndPoint != null)
                {
                    bool supportsIPv4 = true;
                    bool supportsIPv6 = false;

                    if (localCandidate.type == RTCIceCandidateType.relay)
                    {
                        supportsIPv4 = localCandidate.DestinationEndPoint.AddressFamily == AddressFamily.InterNetwork;
                        supportsIPv6 = localCandidate.DestinationEndPoint.AddressFamily == AddressFamily.InterNetworkV6;
                    }
                    else
                    {
                        supportsIPv4 = base.RtpSocket.AddressFamily == AddressFamily.InterNetwork || base.IsDualMode;
                        supportsIPv6 = base.RtpSocket.AddressFamily == AddressFamily.InterNetworkV6 || base.IsDualMode;
                    }

                    lock (_checklistLock)
                    {
                        if (remoteCandidateIPAddr.AddressFamily == AddressFamily.InterNetwork && supportsIPv4 ||
                            remoteCandidateIPAddr.AddressFamily == AddressFamily.InterNetworkV6 && supportsIPv6)
                        {
                            ChecklistEntry entry = new ChecklistEntry(localCandidate, remoteCandidate, IsController);

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
                else
                {
                    logger.LogWarning("RTP ICE Channel could not create a check list entry for a remote candidate with no destination end point, {RemoteCandidate}.", remoteCandidate);
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception " + nameof(UpdateChecklist) + ". {ErrorMessage}", excp);
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

            lock (_checklistLock)
            {
                var existingEntry = _checklist.Where(x =>
                    x.LocalCandidate.type == entry.LocalCandidate.type
                    && x.RemoteCandidate.DestinationEndPoint != null
                    && x.RemoteCandidate.DestinationEndPoint.Address.Equals(entryRemoteEP.Address)
                    && x.RemoteCandidate.DestinationEndPoint.Port == entryRemoteEP.Port
                    && x.RemoteCandidate.protocol == entry.RemoteCandidate.protocol).SingleOrDefault();

                if (existingEntry != null)
                {
                    // Don't replace an existing checklist entry if it's already acting as the nominated entry.
                    if (!existingEntry.Nominated)
                    {
                        if (entry.Priority > existingEntry.Priority)
                        {
                            logger.LogDebug("Removing lower priority entry and adding candidate pair to checklist for: {RemoteCandidate}", entry.RemoteCandidate);
                            _checklist.Remove(existingEntry);
                            _checklist.Add(entry);
                        }
                        else
                        {
                            logger.LogDebug("Existing checklist entry has higher priority, NOT adding entry for: {RemoteCandidate}", entry.RemoteCandidate);
                        }
                    }
                }
                else
                {
                    // No existing entry.
                    logger.LogDebug("Adding new candidate pair to checklist for: {LocalCandidate}->{RemoteCandidate}", entry.LocalCandidate.ToShortString(), entry.RemoteCandidate.ToShortString());
                    _checklist.Add(entry);
                }
            }
        }

        /// <summary>
        /// The periodic logic to run to establish or monitor an ICE connection.
        /// </summary>
        private void DoConnectivityCheck(Object stateInfo)
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
                    SendCheckOnConnectedPair(NominatedEntry);
                    break;

                case RTCIceConnectionState.failed:
                case RTCIceConnectionState.closed:
                    logger.LogDebug("ICE RTP channel stopping connectivity checks in connection state {IceConnectionState}.", IceConnectionState);
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
                while (_pendingRemoteCandidates.Count() > 0)
                {
                    if (_pendingRemoteCandidates.TryDequeue(out var candidate))
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
                        if (_relayChecklistCandidate != null)
                        {
                            // The local relay candidate has already been checked and any hostnames 
                            // resolved when the ICE servers were checked.
                            await UpdateChecklist(_relayChecklistCandidate, candidate).ConfigureAwait(false);
                        }
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
                            if (RemoteIceUser == null || RemoteIcePassword == null)
                            {
                                logger.LogWarning("ICE RTP channel checklist processing cannot occur as either the remote ICE user or password are not set.");
                                IceConnectionState = RTCIceConnectionState.failed;
                            }
                            else
                            {
                                // The checklist gets sorted into priority order whenever a remote candidate and its corresponding candidate pairs
                                // are added. At this point it can be relied upon that the checklist is correctly sorted by candidate pair priority.

                                // Do a check for any timed out entries.
                                var failedEntries = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                       && DateTime.Now.Subtract(x.FirstCheckSentAt).TotalSeconds > FAILED_TIMEOUT_PERIOD).ToList();

                                foreach (var failedEntry in failedEntries)
                                {
                                    logger.LogDebug("ICE RTP channel checks for checklist entry have timed out, state being set to failed: {LocalCandidate}->{RemoteCandidate}.", failedEntry.LocalCandidate.ToShortString(), failedEntry.RemoteCandidate.ToShortString());
                                    failedEntry.State = ChecklistEntryState.Failed;
                                }

                                // Move on to checking for  checklist entries that need an initial check sent.
                                var nextEntry = _checklist.Where(x => x.State == ChecklistEntryState.Waiting).FirstOrDefault();

                                if (nextEntry != null)
                                {
                                    SendConnectivityCheck(nextEntry, false);
                                    return;
                                }

                                var rto = RTO;
                                // No waiting entries so check for ones requiring a retransmit.
                                var retransmitEntry = _checklist.Where(x => x.State == ChecklistEntryState.InProgress
                                    && DateTime.Now.Subtract(x.LastCheckSentAt).TotalMilliseconds > rto).FirstOrDefault();

                                if (retransmitEntry != null)
                                {
                                    SendConnectivityCheck(retransmitEntry, false);
                                    return;
                                }

                                if (IceGatheringState == RTCIceGatheringState.complete)
                                {
                                    //Try force finalize process as probably we lost any RtpPacketResponse during process and we are unable to finalize process
                                    if (NominatedEntry == null)
                                    {
                                        // Do a check for any timed out that has succeded
                                        var failedNominatedEntries = _checklist.Where(x =>
                                            x.State == ChecklistEntryState.Succeeded
                                            && x.LastCheckSentAt > System.DateTime.MinValue
                                            && DateTime.Now.Subtract(x.LastCheckSentAt).TotalSeconds > FAILED_TIMEOUT_PERIOD).ToList();

                                        var requireReprocess = false;
                                        foreach (var failedNominatedEntry in failedNominatedEntries)
                                        {
                                            //Recalculate logic when we lost a nominated entry
                                            if (failedNominatedEntry.Nominated)
                                            {
                                                requireReprocess = true;
                                            }

                                            failedNominatedEntry.State = ChecklistEntryState.Failed;
                                            failedNominatedEntry.Nominated = false;

                                            logger.LogDebug("ICE RTP channel checks for succeded checklist entry have timed out, state being set to failed: {LocalCandidate}->{RemoteCandidate}.", failedNominatedEntry.LocalCandidate.ToShortString(), failedNominatedEntry.RemoteCandidate.ToShortString());
                                        }

                                        //Try nominate another entry
                                        if (requireReprocess)
                                        {
                                            ProcessNominateLogicAsController(null);
                                        }
                                    }

                                    // If this point is reached and all entries are in a failed state then the overall result 
                                    // of the ICE check is a failure.
                                    if (_checklist.All(x => x.State == ChecklistEntryState.Failed))
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
                            logger.LogWarning("ICE RTP channel failed to connect as no checklist entries became available within {ElapsedSeconds}s.", DateTime.Now.Subtract(_checklistStartedAt).TotalSeconds);

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
            if (NominatedEntry == null)
            {
                _connectedAt = DateTime.Now;
                int duration = (int)_connectedAt.Subtract(_startedGatheringAt).TotalMilliseconds;

                logger.LogDebug("ICE RTP channel connected after {Duration:0.##}ms {LocalCandidate}->{RemoteCandidate}.", duration, entry.LocalCandidate.ToShortString(), entry.RemoteCandidate.ToShortString());

                entry.Nominated = true;
                entry.LastConnectedResponseAt = DateTime.Now;
                _checklistState = ChecklistState.Completed;
                _connectivityChecksTimer.Change(CONNECTED_CHECK_PERIOD * 1000, CONNECTED_CHECK_PERIOD * 1000);
                NominatedEntry = entry;
                IceConnectionState = RTCIceConnectionState.connected;
                OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
            }
            else
            {
                // The nominated entry has been changed.
                logger.LogDebug("ICE RTP channel remote nominated candidate changed from {OldCandidate} to {NewCandidate}.", NominatedEntry.RemoteCandidate.ToShortString(), entry.RemoteCandidate.ToShortString());

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

            bool isRelayCheck = candidatePair.LocalCandidate.type == RTCIceCandidateType.relay;
            //bool isTcpProtocol = candidatePair.LocalCandidate.IceServer?.Protocol == ProtocolType.Tcp;

            if (isRelayCheck && candidatePair.TurnPermissionsResponseAt == DateTime.MinValue)
            {
                if (candidatePair.TurnPermissionsRequestSent >= IceServer.MAX_REQUESTS)
                {
                    logger.LogWarning("ICE RTP channel failed to get a Create Permissions response from {IceServerUri} after {TurnPermissionsRequestSent} attempts.", candidatePair.LocalCandidate.IceServer._uri, candidatePair.TurnPermissionsRequestSent);
                    candidatePair.State = ChecklistEntryState.Failed;
                }
                else
                {
                    // Send Create Permissions request to TURN server for remote candidate.
                    candidatePair.TurnPermissionsRequestSent++;

                    logger.LogDebug("ICE RTP channel sending TURN permissions request {TurnPermissionsRequestSent} to server {IceServerUri} for peer {RemoteCandidate} (TxID: {RequestTransactionID}).", candidatePair.TurnPermissionsRequestSent, candidatePair.LocalCandidate.IceServer._uri, candidatePair.RemoteCandidate.DestinationEndPoint, candidatePair.RequestTransactionID);
                    SendTurnCreatePermissionsRequest(candidatePair.RequestTransactionID, candidatePair.LocalCandidate.IceServer, candidatePair.RemoteCandidate.DestinationEndPoint);
                }
            }
            else
            {
                if (candidatePair.LocalCandidate.type == RTCIceCandidateType.relay)
                {
                    IPEndPoint relayServerEP = candidatePair.LocalCandidate.IceServer.ServerEndPoint;
                    logger.LogDebug("ICE RTP channel sending connectivity check for {LocalCandidate}->{RemoteCandidate} from {LocalEndPoint} to relay at {RelayServerEndPoint} (use candidate {SetUseCandidate}).", candidatePair.LocalCandidate.ToShortString(), candidatePair.RemoteCandidate.ToShortString(), base.RTPLocalEndPoint, relayServerEP, setUseCandidate);
                }
                else
                {
                    IPEndPoint remoteEndPoint = candidatePair.RemoteCandidate.DestinationEndPoint;
                    logger.LogDebug("ICE RTP channel sending connectivity check for {LocalCandidate}->{RemoteCandidate} from {LocalEndPoint} to {RemoteEndPoint} (use candidate {SetUseCandidate}).", candidatePair.LocalCandidate.ToShortString(), candidatePair.RemoteCandidate.ToShortString(), base.RTPLocalEndPoint, remoteEndPoint, setUseCandidate);
                }
                SendSTUNBindingRequest(candidatePair, setUseCandidate);
            }
        }

        /// <summary>
        /// Builds and sends a STUN binding request to a remote peer based on the candidate pair properties.
        /// </summary>
        /// <param name="candidatePair">The candidate pair identifying the remote peer to send the STUN Binding Request
        /// to.</param>
        /// <param name="setUseCandidate">Set to true to add a "UseCandidate" attribute to the STUN request.</param>
        private void SendSTUNBindingRequest(ChecklistEntry candidatePair, bool setUseCandidate)
        {
            STUNMessage stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(candidatePair.RequestTransactionID);
            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + LocalIceUser);
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Priority, BitConverter.GetBytes(candidatePair.LocalPriority)));

            if (IsController)
            {
                stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.IceControlling, NetConvert.GetBytes(_iceTiebreaker)));
            }
            else
            {
                stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.IceControlled, NetConvert.GetBytes(_iceTiebreaker)));
            }

            if (setUseCandidate)
            {
                stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.UseCandidate, null));
            }

            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

            if (candidatePair.LocalCandidate.type == RTCIceCandidateType.relay)
            {
                IPEndPoint relayServerEP = candidatePair.LocalCandidate.IceServer.ServerEndPoint;
                var protocol = candidatePair.LocalCandidate.IceServer.Protocol;
                SendRelay(protocol, candidatePair.RemoteCandidate.DestinationEndPoint, stunReqBytes, relayServerEP, candidatePair.LocalCandidate.IceServer);
            }
            else
            {
                IPEndPoint remoteEndPoint = candidatePair.RemoteCandidate.DestinationEndPoint;
                var sendResult = base.Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunReqBytes);

                if (sendResult != SocketError.Success)
                {
                    logger.LogWarning("Error sending STUN server binding request to {RemoteEndPoint}. {SendResult}.", remoteEndPoint, sendResult);
                }
                else
                {
                    OnStunMessageSent?.Invoke(stunRequest, remoteEndPoint, false);
                }
            }
        }

        /// <summary>
        /// Builds and sends the connectivity check on a candidate pair that is set
        /// as the current nominated, connected pair.
        /// </summary>
        /// <param name="candidatePair">The pair to send the connectivity check on.</param>
        private void SendCheckOnConnectedPair(ChecklistEntry candidatePair)
        {
            if (candidatePair == null)
            {
                logger.LogWarning("RTP ICE channel was requested to send a connectivity check on an empty candidate pair.");
            }
            else
            {
                if (DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds > FAILED_TIMEOUT_PERIOD &&
                    DateTime.Now.Subtract(candidatePair.LastBindingRequestReceivedAt).TotalSeconds > FAILED_TIMEOUT_PERIOD)
                {
                    int duration = (int)DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds;
                    logger.LogWarning("ICE RTP channel failed after {Duration:0.##}s {LocalCandidate}->{RemoteCandidate}.", duration, candidatePair.LocalCandidate.ToShortString(), candidatePair.RemoteCandidate.ToShortString());

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
                            int duration = (int)DateTime.Now.Subtract(candidatePair.LastConnectedResponseAt).TotalSeconds;
                            logger.LogWarning("ICE RTP channel disconnected after {Duration:0.##}s {LocalCandidate}->{RemoteCandidate}.", duration, candidatePair.LocalCandidate.ToShortString(), candidatePair.RemoteCandidate.ToShortString());

                            IceConnectionState = RTCIceConnectionState.disconnected;
                            OnIceConnectionStateChange?.Invoke(IceConnectionState);
                        }
                    }
                    else if (IceConnectionState != RTCIceConnectionState.connected)
                    {
                        logger.LogDebug("ICE RTP channel has re-connected {LocalCandidate}->{RemoteCandidate}.", candidatePair.LocalCandidate.ToShortString(), candidatePair.RemoteCandidate.ToShortString());

                        // Re-connected.
                        IceConnectionState = RTCIceConnectionState.connected;
                        OnIceConnectionStateChange?.Invoke(IceConnectionState);
                    }

                    candidatePair.RequestTransactionID = candidatePair.RequestTransactionID ?? Crypto.GetRandomString(STUNHeader.TRANSACTION_ID_LENGTH);
                    candidatePair.LastCheckSentAt = DateTime.Now;
                    candidatePair.ChecksSent++;

                    SendSTUNBindingRequest(candidatePair, false);
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

            OnStunMessageReceived?.Invoke(stunMessage, remoteEndPoint, wasRelayed);

            // Check if the  STUN message is for an ICE server check.
            var iceServer = GetIceServerForTransactionID(stunMessage.Header.TransactionId);
            if (iceServer != null)
            {
                bool candidatesAvailable = iceServer.GotStunResponse(stunMessage, remoteEndPoint);
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
                else if (stunMessage.Header.MessageClass == STUNClassTypesEnum.ErrorResponse ||
                         stunMessage.Header.MessageClass == STUNClassTypesEnum.SuccessResponse)
                {
                    // Correlate with request using transaction ID as per https://tools.ietf.org/html/rfc8445#section-7.2.5.
                    var matchingChecklistEntry = GetChecklistEntryForStunResponse(stunMessage.Header.TransactionId);

                    if (matchingChecklistEntry == null)
                    {
                        if (IceConnectionState != RTCIceConnectionState.connected)
                        {
                            // If the channel is connected a mismatched txid can result if the connection is very busy, i.e. streaming 1080p video,
                            // it's likely to only be transient and does not impact the connection state.
                            logger.LogWarning("ICE RTP channel received a STUN {MessageType} with a transaction ID that did not match a checklist entry.", stunMessage.Header.MessageType);
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
                                logger.LogDebug("ICE RTP channel remote peer nominated entry from binding response {RemoteCandidate}", matchingChecklistEntry.RemoteCandidate.ToShortString());

                                // This is the response to a connectivity check that had the "UseCandidate" attribute set.
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                            else if (IsController)
                            {
                                logger.LogDebug("ICE RTP channel binding response state {State} as Controller for {RemoteCandidate}", matchingChecklistEntry.State, matchingChecklistEntry.RemoteCandidate.ToShortString());
                                ProcessNominateLogicAsController(matchingChecklistEntry);
                            }
                        }
                    }
                }
                else
                {
                    logger.LogWarning("ICE RTP channel received an unexpected STUN message {MessageType} from {RemoteEndPoint}.\nJson: {StunMessage}", stunMessage.Header.MessageType, remoteEndPoint, stunMessage);
                }
            }
        }

        /// <summary>
        /// Handles Nominate logic when Agent is the controller
        /// </summary>
        /// <param name="possibleMatchingCheckEntry">Optional initial ChecklistEntry.</param>
        private void ProcessNominateLogicAsController(ChecklistEntry possibleMatchingCheckEntry)
        {
            if (IsController && (NominatedEntry == null || !NominatedEntry.Nominated || NominatedEntry.State != ChecklistEntryState.Succeeded))
            {
                lock (_checklistLock)
                {
                    _checklist.Sort();

                    var findBetterOptionOrWait = possibleMatchingCheckEntry == null; //|| possibleMatchingCheckEntry.RemoteCandidate.type == RTCIceCandidateType.relay;
                    var nominatedCandidate = _checklist.Find(
                            x => x.Nominated
                            && x.State == ChecklistEntryState.Succeeded
                            && (x.LastCheckSentAt == DateTime.MinValue ||
                                DateTime.Now.Subtract(x.LastCheckSentAt).TotalSeconds <= FAILED_TIMEOUT_PERIOD));

                    //We already have a good candidate, discard our succeded candidate
                    if (nominatedCandidate != null /*&& nominatedCandidate.RemoteCandidate.type != RTCIceCandidateType.relay*/)
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
                            (possibleMatchingCheckEntry == null ||
                             (x.Priority > possibleMatchingCheckEntry.Priority /*&& x.RemoteCandidate.type != RTCIceCandidateType.relay*/) ||
                             possibleMatchingCheckEntry.State != ChecklistEntryState.Succeeded));

                        if (betterOptionEntry != null)
                        {
                            possibleMatchingCheckEntry = betterOptionEntry;
                            findBetterOptionOrWait = false; //possibleMatchingCheckEntry.RemoteCandidate.type == RTCIceCandidateType.relay;
                        }

                        //if we still need to find a better option, we will search for matching entries with high priority that still processing
                        if (findBetterOptionOrWait)
                        {
                            var waitOptionEntry = _checklist.Find(x =>
                                (x.State == ChecklistEntryState.InProgress || x.State == ChecklistEntryState.Waiting) &&
                                 (possibleMatchingCheckEntry == null ||
                                  (x.Priority > possibleMatchingCheckEntry.Priority /*&& x.RemoteCandidate.type != RTCIceCandidateType.relay*/) ||
                                  possibleMatchingCheckEntry.State != ChecklistEntryState.Succeeded));

                            if (waitOptionEntry != null)
                            {
                                possibleMatchingCheckEntry = null;
                            }
                        }
                    }
                }

                //Nominate Candidate if we pass in all heuristic checks from previous algorithm
                if (possibleMatchingCheckEntry != null && possibleMatchingCheckEntry.State == ChecklistEntryState.Succeeded)
                {
                    possibleMatchingCheckEntry.Nominated = true;
                    SendConnectivityCheck(possibleMatchingCheckEntry, true);
                }
            }

            /*if (IsController && !_checklist.Any(x => x.Nominated))
            {
                // If we are the controlling ICE agent it's up to us to decide when to nominate a candidate pair to use for the connection.
                // For the lack of a more sophisticated approach use whichever pair gets the first successful STUN exchange. If needs be 
                // the selection algorithm can improve over time.

                //Find high priority succeded event
                _checklist.Sort();
                var matchingCheckEntry = _checklist.Find(x => x.State == ChecklistEntryState.Succeeded);

                //We can nominate this entry (if exists)
                if (matchingCheckEntry != null)
                {
                    matchingCheckEntry.Nominated = true;
                    SendConnectivityCheck(matchingCheckEntry, true);
                }
            }*/
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
                logger.LogWarning("ICE RTP channel rejecting non-relayed STUN binding request from {RemoteEndPoint}.", remoteEndPoint);

                STUNMessage stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse);
                stunErrResponse.Header.TransactionId = bindingRequest.Header.TransactionId;
                Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunErrResponse.ToByteBuffer(null, false));

                OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
            }
            else
            {
                bool result = bindingRequest.CheckIntegrity(Encoding.UTF8.GetBytes(LocalIcePassword));

                if (!result)
                {
                    // Send STUN error response.
                    logger.LogWarning("ICE RTP channel STUN binding request from {RemoteEndPoint} failed an integrity check, rejecting.", remoteEndPoint);
                    STUNMessage stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse);
                    stunErrResponse.Header.TransactionId = bindingRequest.Header.TransactionId;
                    Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunErrResponse.ToByteBuffer(null, false));

                    OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
                }
                else
                {
                    ChecklistEntry matchingChecklistEntry = null;

                    // Find the checklist entry for this remote candidate and update its status.
                    lock (_checklistLock)
                    {
                        // The matching checklist entry is chosen as:
                        // - The entry that has a remote candidate with an end point that matches the endpoint this STUN request came from,
                        // - And if the STUN request was relayed through a TURN server then only match is the checklist local candidate is 
                        //   also a relay type. It is possible for the same remote end point to send STUN requests directly and via a TURN server.
                        matchingChecklistEntry = _checklist.Where(x => x.RemoteCandidate.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint) &&
                         (!wasRelayed || x.LocalCandidate.type == RTCIceCandidateType.relay)
                         ).FirstOrDefault();
                    }

                    if (matchingChecklistEntry == null &&
                        (_remoteCandidates == null || !_remoteCandidates.Any(x => x.IsEquivalentEndPoint(RTCIceProtocol.udp, remoteEndPoint))))
                    {
                        // This STUN request has come from a socket not in the remote ICE candidates list. 
                        // Add a new remote peer reflexive candidate. 
                        RTCIceCandidate peerRflxCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
                        peerRflxCandidate.SetAddressProperties(RTCIceProtocol.udp, remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.prflx, null, 0);
                        peerRflxCandidate.SetDestinationEndPoint(remoteEndPoint);
                        logger.LogDebug("Adding peer reflex ICE candidate for {RemoteEndPoint}.", remoteEndPoint);
                        _remoteCandidates.Add(peerRflxCandidate);

                        // Add a new entry to the check list for the new peer reflexive candidate.
                        ChecklistEntry entry = new ChecklistEntry(wasRelayed ? _relayChecklistCandidate : _localChecklistCandidate,
                            peerRflxCandidate, IsController);
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

                    if (matchingChecklistEntry == null)
                    {
                        logger.LogWarning("ICE RTP channel STUN request matched a remote candidate but NOT a checklist entry.");
                        STUNMessage stunErrResponse = new STUNMessage(STUNMessageTypesEnum.BindingErrorResponse);
                        stunErrResponse.Header.TransactionId = bindingRequest.Header.TransactionId;
                        Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunErrResponse.ToByteBuffer(null, false));

                        OnStunMessageSent?.Invoke(stunErrResponse, remoteEndPoint, false);
                    }
                    else
                    {
                        // The UseCandidate attribute is only meant to be set by the "Controller" peer. This implementation
                        // will accept it irrespective of the peer roles. If the remote peer wants us to use a certain remote
                        // end point then so be it.
                        if (bindingRequest.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate))
                        {
                            if (IceConnectionState != RTCIceConnectionState.connected)
                            {
                                // If we are the "controlled" agent and get a "use candidate" attribute that sets the matching candidate as nominated 
                                // as per https://tools.ietf.org/html/rfc8445#section-7.3.1.5.
                                logger.LogDebug("ICE RTP channel remote peer nominated entry from binding request: {RemoteCandidate}.", matchingChecklistEntry.RemoteCandidate.ToShortString());
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                            else if (matchingChecklistEntry.RemoteCandidate.ToString() != NominatedEntry.RemoteCandidate.ToString())
                            {
                                // The remote peer is changing the nominated candidate.
                                logger.LogDebug("ICE RTP channel remote peer nominated a new candidate: {RemoteCandidate}.", matchingChecklistEntry.RemoteCandidate.ToShortString());
                                SetNominatedEntry(matchingChecklistEntry);
                            }
                        }

                        matchingChecklistEntry.LastBindingRequestReceivedAt = DateTime.Now;

                        STUNMessage stunResponse = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
                        stunResponse.Header.TransactionId = bindingRequest.Header.TransactionId;
                        stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                        byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(LocalIcePassword, true);

                        if (wasRelayed)
                        {
                            var protocol = matchingChecklistEntry.LocalCandidate.IceServer.Protocol;
                            SendRelay(protocol, remoteEndPoint, stunRespBytes, matchingChecklistEntry.LocalCandidate.IceServer.ServerEndPoint, matchingChecklistEntry.LocalCandidate.IceServer);
                            OnStunMessageSent?.Invoke(stunResponse, remoteEndPoint, true);
                        }
                        else
                        {
                            Send(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespBytes);
                            OnStunMessageSent?.Invoke(stunResponse, remoteEndPoint, false);
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
        private ChecklistEntry GetChecklistEntryForStunResponse(byte[] transactionID)
        {
            string txID = Encoding.ASCII.GetString(transactionID);
            ChecklistEntry matchingChecklistEntry = null;

            lock (_checklistLock)
            {
                matchingChecklistEntry = _checklist.Where(x => x.IsTransactionIDMatch(txID)).FirstOrDefault();
            }

            return matchingChecklistEntry;
        }

        /// <summary>
        /// Checks a STUN response transaction ID to determine if it matches a check being carried
        /// out for an ICE server.
        /// </summary>
        /// <param name="transactionID">The transaction ID from the STUN response.</param>
        /// <returns>If found a matching state object or null if not.</returns>
        private IceServer GetIceServerForTransactionID(byte[] transactionID)
        {
            if (_iceServerConnections == null || _iceServerConnections.Count == 0)
            {
                return null;
            }
            else
            {
                string txID = Encoding.ASCII.GetString(transactionID);

                var entry = _iceServerConnections
                           .Where(x => x.Value.IsTransactionIDMatch(txID))
                           .SingleOrDefault();

                if (!entry.Equals(default(KeyValuePair<STUNUri, IceServer>)))
                {
                    return entry.Value;
                }
                else
                {
                    return null;
                }
            }
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

            // Send a STUN binding request.
            STUNMessage stunRequest = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);

            byte[] stunReqBytes = null;

            if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
            {
                stunReqBytes = GetAuthenticatedStunRequest(stunRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
            }
            else
            {
                stunReqBytes = stunRequest.ToByteBuffer(null, false);
            }

            var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                                SendOverTCP(iceServer, stunReqBytes) :
                                base.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, stunReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning("Error sending STUN server binding request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                    iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
            }
            else
            {
                OnStunMessageSent?.Invoke(stunRequest, iceServer.ServerEndPoint, false);
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

            STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Allocate);
            allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
            allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.RequestedTransport, STUNAttributeConstants.UdpTransportType));
            allocateRequest.Attributes.Add(
                new STUNAttribute(STUNAttributeTypesEnum.RequestedAddressFamily,
                iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork ?
                STUNAttributeConstants.IPv4AddressFamily : STUNAttributeConstants.IPv6AddressFamily));

            byte[] allocateReqBytes = null;

            if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
            {
                allocateReqBytes = GetAuthenticatedStunRequest(allocateRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
            }
            else
            {
                allocateReqBytes = allocateRequest.ToByteBuffer(null, false);
            }

            var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                                SendOverTCP(iceServer, allocateReqBytes) :
                                base.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning("Error sending TURN Allocate request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                    iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
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

            STUNMessage allocateRequest = new STUNMessage(STUNMessageTypesEnum.Refresh);
            allocateRequest.Header.TransactionId = Encoding.ASCII.GetBytes(iceServer.TransactionID);
            //allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, 3600));
            allocateRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, ALLOCATION_TIME_TO_EXPIRY_VALUE));

            allocateRequest.Attributes.Add(
                new STUNAttribute(STUNAttributeTypesEnum.RequestedAddressFamily,
                iceServer.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork ?
                STUNAttributeConstants.IPv4AddressFamily : STUNAttributeConstants.IPv6AddressFamily));

            byte[] allocateReqBytes = null;

            if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
            {
                allocateReqBytes = GetAuthenticatedStunRequest(allocateRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
            }
            else
            {
                allocateReqBytes = allocateRequest.ToByteBuffer(null, false);
            }

            var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                                SendOverTCP(iceServer, allocateReqBytes) :
                                base.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, allocateReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning("Error sending TURN Refresh request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                    iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
            }
            else
            {
                OnStunMessageSent?.Invoke(allocateRequest, iceServer.ServerEndPoint, false);
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
            STUNMessage permissionsRequest = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            permissionsRequest.Header.TransactionId = Encoding.ASCII.GetBytes(transactionID);
            permissionsRequest.Attributes.Add(new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORPeerAddress, peerEndPoint.Port, peerEndPoint.Address, permissionsRequest.Header.TransactionId));

            byte[] createPermissionReqBytes = null;

            if (iceServer.Nonce != null && iceServer.Realm != null && iceServer._username != null && iceServer._password != null)
            {
                createPermissionReqBytes = GetAuthenticatedStunRequest(permissionsRequest, iceServer._username, iceServer.Realm, iceServer._password, iceServer.Nonce);
            }
            else
            {
                createPermissionReqBytes = permissionsRequest.ToByteBuffer(null, false);
            }

            var sendResult = iceServer.Protocol == ProtocolType.Tcp ?
                                SendOverTCP(iceServer, createPermissionReqBytes) :
                                base.Send(RTPChannelSocketsEnum.RTP, iceServer.ServerEndPoint, createPermissionReqBytes);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning("Error sending TURN Create Permissions request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.",
                    iceServer.OutstandingRequestsSent, iceServer._uri, iceServer.ServerEndPoint, sendResult);
            }
            else
            {
                OnStunMessageSent?.Invoke(permissionsRequest, iceServer.ServerEndPoint, false);
            }

            return sendResult;
        }

        protected virtual SocketError SendOverTCP(IceServer iceServer, byte[] buffer)
        {
            IPEndPoint dstEndPoint = iceServer?.ServerEndPoint;
            if (IsClosed)
            {
                return SocketError.Disconnecting;
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in RTPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in RTPChannel.");
            }
            else if (IPAddress.Any.Equals(dstEndPoint.Address) || IPAddress.IPv6Any.Equals(dstEndPoint.Address))
            {
                logger.LogWarning("The destination address for Send in RTPChannel cannot be {Address}.", dstEndPoint.Address);
                return SocketError.DestinationAddressRequired;
            }
            else
            {
                try
                {
                    //Connect to destination
                    RtpTcpSocketByUri.TryGetValue(iceServer?._uri, out Socket sendSocket);
                    //LastRtpDestination = dstEndPoint;

                    if (sendSocket == null)
                    {
                        return SocketError.Fault;
                    }

                    //Prevent Send to IPV4 while socket is IPV6 (Mono Error)
                    if (dstEndPoint.AddressFamily == AddressFamily.InterNetwork && sendSocket.AddressFamily != dstEndPoint.AddressFamily)
                    {
                        dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
                    }

                    Func<IPEndPoint, IPEndPoint, bool> equals = (IPEndPoint e1, IPEndPoint e2) =>
                    {
                        return e1.Port == e2.Port && e1.Address.Equals(e2.Address);
                    };

                    if (!sendSocket.Connected || !(sendSocket.RemoteEndPoint is IPEndPoint) || !equals(sendSocket.RemoteEndPoint as IPEndPoint, dstEndPoint))
                    {
                        if (sendSocket.Connected)
                        {
                            logger.LogDebug("SendOverTCP request disconnect.");
                            sendSocket.Disconnect(true);
                        }
                        sendSocket.Connect(dstEndPoint);

                        logger.LogDebug("SendOverTCP status: {Status} endpoint: {EndPoint}", sendSocket.Connected, dstEndPoint);
                    }

                    //Fix ReceiveFrom logic if any previous exception happens
                    m_rtpTcpReceiverByUri.TryGetValue(iceServer?._uri, out IceTcpReceiver rtpTcpReceiver);
                    if (rtpTcpReceiver != null && !rtpTcpReceiver.IsRunningReceive && !rtpTcpReceiver.IsClosed)
                    {
                        rtpTcpReceiver.BeginReceiveFrom();
                    }

                    sendSocket.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, dstEndPoint, EndSendToTCP, sendSocket);
                    return SocketError.Success;
                }
                catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
                {
                    return SocketError.Disconnecting;
                }
                catch (SocketException sockExcp)
                {
                    return sockExcp.SocketErrorCode;
                }
                catch (Exception excp)
                {
                    logger.LogError(excp, "Exception RTPIceChannel.SendOverTCP. {ErrorMessage}", excp.Message);
                    return SocketError.Fault;
                }
            }
        }

        protected virtual void EndSendToTCP(IAsyncResult ar)
        {
            try
            {
                Socket sendSocket = (Socket)ar.AsyncState;
                int bytesSent = sendSocket.EndSendTo(ar);
            }
            catch (SocketException sockExcp)
            {
                // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                // normal RTP operation. For example:
                // - the RTP connection may start sending before the remote socket starts listening,
                // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                //   or new socket during the transition.
                logger.LogWarning(sockExcp, "SocketException RTPIceChannel EndSendToTCP ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception RTPIceChannel EndSendToTCP. {ErrorMessage}", excp.Message);
            }
        }

        /// <summary>
        /// Adds the authentication fields to a STUN request.
        /// </summary>
        /// <returns>The serialised STUN request.</returns>
        private byte[] GetAuthenticatedStunRequest(STUNMessage stunRequest, string username, byte[] realm, string password, byte[] nonce)
        {
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, nonce));
            stunRequest.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, realm));
            stunRequest.AddUsernameAttribute(username);

            // See https://tools.ietf.org/html/rfc5389#section-15.4
            string key = $"{username}:{Encoding.UTF8.GetString(realm)}:{password}";
            var buffer = Encoding.UTF8.GetBytes(key);
            var md5Digest = new MD5Digest();
            var hash = new byte[md5Digest.GetDigestSize()];

            md5Digest.BlockUpdate(buffer, 0, buffer.Length);
            md5Digest.DoFinal(hash, 0);

            return stunRequest.ToByteBuffer(hash, true);
        }

        /// <summary>
        /// Event handler for packets received on the RTP UDP socket. This channel will detect STUN messages
        /// and extract STUN messages to deal with ICE connectivity checks and TURN relays.
        /// </summary>
        /// <param name="receiver">The UDP receiver the packet was received on.</param>
        /// <param name="localPort">The local port it was received on.</param>
        /// <param name="remoteEndPoint">The remote end point of the sender.</param>
        /// <param name="packet">The raw packet received (note this may not be RTP if other protocols are being multiplexed).</param>
        protected override void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
        {
            if (packet?.Length > 0)
            {
                bool wasRelayed = false;

                if (packet[0] == 0x00 && packet[1] == 0x17)
                {
                    wasRelayed = true;

                    // TURN data indication. Extract the data payload and adjust the end point.
                    var dataIndication = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                    var dataAttribute = dataIndication.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.Data).FirstOrDefault();
                    packet = dataAttribute?.Value;

                    var peerAddrAttribute = dataIndication.Attributes.Where(x => x.AttributeType == STUNAttributeTypesEnum.XORPeerAddress).FirstOrDefault();
                    remoteEndPoint = (peerAddrAttribute as STUNXORAddressAttribute)?.GetIPEndPoint();
                }

                base.LastRtpDestination = remoteEndPoint;

                if (packet[0] == 0x00 || packet[0] == 0x01)
                {
                    // STUN packet.
                    var stunMessage = STUNMessage.ParseSTUNMessage(packet, packet.Length);
                    _ = ProcessStunMessage(stunMessage, remoteEndPoint, wasRelayed);
                }
                else
                {
                    OnRTPDataReceived?.Invoke(localPort, remoteEndPoint, packet);
                }
            }
        }

        /// <summary>
        /// Sends a packet via a TURN relay server.
        /// </summary>
        /// <param name="dstEndPoint">The peer destination end point.</param>
        /// <param name="buffer">The data to send to the peer.</param>
        /// <param name="relayEndPoint">The TURN server end point to send the relayed request to.</param>
        /// <returns></returns>
        private SocketError SendRelay(ProtocolType protocol, IPEndPoint dstEndPoint, byte[] buffer, IPEndPoint relayEndPoint, IceServer iceServer)
        {
            STUNMessage sendReq = new STUNMessage(STUNMessageTypesEnum.SendIndication);
            sendReq.AddXORPeerAddressAttribute(dstEndPoint.Address, dstEndPoint.Port);
            sendReq.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, buffer));

            var request = sendReq.ToByteBuffer(null, false);
            var sendResult = protocol == ProtocolType.Tcp ?
                SendOverTCP(iceServer, request) :
                base.Send(RTPChannelSocketsEnum.RTP, relayEndPoint, request);

            if (sendResult != SocketError.Success)
            {
                logger.LogWarning("Error sending TURN relay request to TURN server at {RelayEndPoint}. {SendResult}.", relayEndPoint, sendResult);
            }
            else
            {
                OnStunMessageSent?.Invoke(sendReq, relayEndPoint, true);
            }

            return sendResult;
        }

        private async Task<IPAddress[]> ResolveMdnsName(RTCIceCandidate candidate)
        {
            if (MdnsGetAddresses != null)
            {
                if (MdnsResolve != null)
                {
                    logger.LogWarning("RTP ICE channel has both "+ nameof(MdnsGetAddresses) + " and " + nameof(MdnsGetAddresses) + " set. Only " + nameof(MdnsGetAddresses) + " will be used.");
                }
                return await MdnsGetAddresses(candidate.address).ConfigureAwait(false);
            }
            if (MdnsResolve != null)
            {
                var address = await MdnsResolve(candidate.address).ConfigureAwait(false);
                return address != null ? new IPAddress[] { address } : Array.Empty<IPAddress>();
            }


            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(candidate.address).ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                logger.LogError(e, "Error resolving mDNS hostname {Name}", candidate.address);
                return Array.Empty<IPAddress>();
            }
            catch (ArgumentException e)
            {
                logger.LogError(e, "Unsupported mDNS hostname {Name}", candidate.address);
                return Array.Empty<IPAddress>();
            }

            if (addresses.Length == 0)
            {
                logger.LogWarning("RTP ICE channel has no MDNS resolver set, and the system can not resolve remote candidate with MDNS hostname {CandidateAddress}.", candidate.address);
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
        /// <returns>The result of initiating the send. This result does not reflect anything about
        /// whether the remote party received the packet or not.</returns>
        public override SocketError Send(RTPChannelSocketsEnum sendOn, IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (NominatedEntry != null && NominatedEntry.LocalCandidate.type == RTCIceCandidateType.relay &&
                NominatedEntry.LocalCandidate.IceServer != null &&
                NominatedEntry.RemoteCandidate.DestinationEndPoint.Address.Equals(dstEndPoint.Address) &&
                NominatedEntry.RemoteCandidate.DestinationEndPoint.Port == dstEndPoint.Port)
            {
                // A TURN relay channel is being used to communicate with the remote peer.
                var protocol = NominatedEntry.LocalCandidate.IceServer.Protocol;
                var serverEndPoint = NominatedEntry.LocalCandidate.IceServer.ServerEndPoint;
                return SendRelay(protocol, dstEndPoint, buffer, serverEndPoint, NominatedEntry.LocalCandidate.IceServer);
            }
            else
            {
                return base.Send(sendOn, dstEndPoint, buffer);
            }
        }
    }
}
