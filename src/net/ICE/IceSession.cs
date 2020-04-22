//-----------------------------------------------------------------------------
// Filename: IceSession.cs
//
// Description: Represents a ICE Session as described in the Interactive
// Connectivity Establishment RFC8445 https://tools.ietf.org/html/rfc8445.
//
// Additionally support for the following standards or proposed standards 
// is included:
// - "Trickle ICE" as per draft RFC
//    https://tools.ietf.org/html/draft-ietf-ice-trickle-21.
// - "WebRTC IP Address Handling Requirements" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
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

            public bool Valid;

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

        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;
        private const int MAX_CHECKLIST_ENTRIES = 25;   // Maximum number of entries that can be added to the checklist of candidate pairs.
        //private const int INITIAL_STUN_BINDING_PERIOD_MILLISECONDS = 1000;       // The period to send the initial STUN requests used to get an ICE candidates public IP address.
        private const int INITIAL_STUN_BINDING_ATTEMPTS_LIMIT = 3;                // The maximum number of binding attempts to determine a local socket's public IP address before giving up.
        private const int ICE_CONNECTED_NO_COMMUNICATIONS_TIMEOUT_SECONDS = 35; // If there are no messages received (STUN/RTP/RTCP) within this period the session will be closed.
        private const int MAXIMUM_TURN_ALLOCATE_ATTEMPTS = 4;
        private const int MAXIMUM_STUN_CONNECTION_ATTEMPTS = 5;
        private const int STUN_CHECK_BASE_PERIOD_MILLISECONDS = 5000;
        private const float STUN_CHECK_LOW_RANDOMISATION_FACTOR = 0.5F;
        private const float STUN_CHECK_HIGH_RANDOMISATION_FACTOR = 1.5F;

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

        private static ILogger logger = Log.Logger;

        private RTPChannel _rtpChannel;

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
                if (_candidates == null)
                {
                    _candidates = GetHostCandidates();
                }

                return _candidates;
            }
        }
        private List<RTCIceCandidate> _candidates;
        private List<RTCIceCandidate> _remoteCandidates = new List<RTCIceCandidate>();

        /// <summary>
        /// The state of the checklist as the ICE checks are carried out.
        /// </summary>
        internal ChecklistState _checklistState = ChecklistState.Running;

        /// <summary>
        /// The checklist of local and remote candidate pairs
        /// </summary>
        internal List<ChecklistEntry> _checklist = new List<ChecklistEntry>();

        /// <summary>
        /// The list of ICE candidates from the remote peer.
        /// </summary>
        public List<RTCIceCandidate> PeerCandidates { get; private set; }

        /// <summary>
        /// Retransmission timer for STUN transactions.
        /// </summary>
        /// <remarks>
        /// As specified in https://tools.ietf.org/html/rfc8445#section-14.
        /// </remarks>
        private int RTO
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

        public string LocalIceUser;
        public string LocalIcePassword;
        public string RemoteIceUser;
        public string RemoteIcePassword;

        public IPEndPoint ConnectedRemoteEndPoint
        {
            get { return _connectedRemoteEndPoint; }
        }
        private IPEndPoint _connectedRemoteEndPoint;

        private bool _closed = false;
        private Timer _stunChecksTimer;

        public event Action<RTCIceCandidate> OnIceCandidate;
        public event Action<RTCIceConnectionState> OnIceConnectionStateChange;
        public event Action<RTCIceGatheringState> OnIceGatheringStateChange;

        /// <summary>
        /// Creates a new instance of an ICE session.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel is the object managing the socket
        /// doing the media sending and receiving. Its the same socket the ICE session
        /// will need to initiate all the connectivity checks on.</param>
        /// <param name="component">The component (RTP or RTCP) the channel is being used for. Note
        /// for cases where RTP and RTCP are multiplexed the component is set to RTP.</param>
        public IceSession(RTPChannel rtpChannel, RTCIceComponent component)
        {
            _rtpChannel = rtpChannel;
            Component = component;

            LocalIceUser = Crypto.GetRandomString(ICE_UFRAG_LENGTH);
            LocalIcePassword = Crypto.GetRandomString(ICE_PASSWORD_LENGTH);
        }

        /// <summary>
        /// We've been given the green light to start the ICE candidate gathering process.
        /// This could include contacting external STUN and TURN servers. Events will 
        /// be fired as each ICE is identified and as the gathering state machine changes
        /// state.
        /// </summary>
        public void StartGathering()
        {
            GatheringState = RTCIceGatheringState.gathering;
            OnIceGatheringStateChange?.Invoke(RTCIceGatheringState.gathering);
            ConnectionState = RTCIceConnectionState.checking;
            OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.checking);

            _stunChecksTimer = new Timer(ProcessChecklist, null, 0, Ta);
        }

        public void SetRemoteCredentials(string username, string password)
        {
            RemoteIceUser = username;
            RemoteIcePassword = password;

            // Once the remote party's ICE credentials are known connection checking can 
            // commence immediately as candidates trickle in.
            ConnectionState = RTCIceConnectionState.checking;
            OnIceConnectionStateChange?.Invoke(ConnectionState);
        }

        public void Close(string reason)
        {
            if (!_closed)
            {
                _closed = true;
                _stunChecksTimer.Dispose();
            }
        }

        /// <summary>
        /// Adds a remote ICE candidate to the ICE session.
        /// </summary>
        /// <param name="candidate">An ICE candidate from the remote party.</param>
        public void AddRemoteCandidate(RTCIceCandidate candidate)
        {
            if (!_remoteCandidates.Any(x => x.foundation == candidate.foundation))
            {
                // Have a remote candidate. Connectivity checks can start. Note because we support ICE trickle
                // we may also still be gathering candidates. Connectivity checks and gathering can be done in parallel.

                _remoteCandidates.Add(candidate);
                UpdateChecklist(candidate);
            }
            else
            {
                // This occurs if the remote party made an offer and assumed we couldn't multiplex the audio and video streams.
                // It will offer the same ICE candidates separately for the audio and video announcements.
                logger.LogWarning($"ICE session not adding remote ICE candidate as candidate with foundation {candidate.foundation} already present.");
            }
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
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.1</remarks>
        /// <returns>A list of "host" ICE candidates for the local machine.</returns>
        private List<RTCIceCandidate> GetHostCandidates()
        {
            List<RTCIceCandidate> hostCandidates = new List<RTCIceCandidate>();
            RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

            foreach (var localAddress in NetServices.LocalIPAddresses.Where(x =>
                 !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6SiteLocal))
            {
                var hostCandidate = new RTCIceCandidate(init);
                hostCandidate.SetAddressProperties(RTCIceProtocol.udp, localAddress, (ushort)_rtpChannel.RTPPort, RTCIceCandidateType.host, null, 0);

                // We currently only support a single multiplexed connection for all data streams and RTCP.
                if (hostCandidate.component == RTCIceComponent.rtp && hostCandidate.sdpMLineIndex == 0)
                {
                    hostCandidates.Add(hostCandidate);
                }
            }

            return hostCandidates;
        }

        /// <summary>
        /// Attempts to get a list of server-reflexive candidates using the local "host" candidates
        /// and a STUN or TURN server.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        /// <returns></returns>
        private List<RTCIceCandidate> GetServerRelexiveCandidates()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to get a list of relay candidates from a TURN server.
        /// </summary>
        /// <remarks>See https://tools.ietf.org/html/rfc8445#section-5.1.1.2</remarks>
        /// <returns></returns>
        private List<RTCIceCandidate> GetRelayCandidates()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates the checklist with new candidate pairs.
        /// </summary>
        /// <remarks>
        /// From https://tools.ietf.org/html/rfc8445#section-6.1.2.2:
        /// IPv6 link-local addresses MUST NOT be paired with other than link-local addresses.
        /// </remarks>
        private void UpdateChecklist(RTCIceCandidate remoteCandidate)
        {
            lock (_checklist)
            {
                // Local server reflexive candidates don't get added to the checklist since they are just local
                // "host" candidates with an extra NAT address mapping. The NAT address mapping is needed for the
                // remote ICE peer but locally a server reflexive candidate is always going to be represented by
                // a "host" candidate.

                foreach (var localCandidate in Candidates.Where(x => x.type != RTCIceCandidateType.srflx))
                {
                    if (localCandidate.CandidateAddress != null && remoteCandidate.CandidateAddress != null &&
                        localCandidate.CandidateAddress.AddressFamily == remoteCandidate.CandidateAddress.AddressFamily)
                    {
                        if (remoteCandidate.CandidateAddress.IsIPv6LinkLocal)
                        {
                            if (localCandidate.CandidateAddress.IsIPv6LinkLocal)
                            {
                                // Only pair IPv6 link local candidates if both are link local.
                                ChecklistEntry entry = new ChecklistEntry(localCandidate, remoteCandidate, IsController);

                                // Because only ONE checklist is currently supported each candidate pair can be set to
                                // a "waiting" state. If an additional checklist is ever added then only one candidate
                                // pair with the same foundation should be set to waiting across all checklists.
                                // See https://tools.ietf.org/html/rfc8445#section-6.1.2.6 for a somewhat convoluted
                                // explanation and example.
                                entry.State = ChecklistEntryState.Waiting;

                                _checklist.Add(entry);
                            }
                        }
                        else
                        {
                            ChecklistEntry entry = new ChecklistEntry(localCandidate, remoteCandidate, IsController);
                            // See comment above about why the candidate state is adjusted.
                            entry.State = ChecklistEntryState.Waiting;
                            _checklist.Add(entry);
                        }
                    }
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

        /// <summary>
        /// Attempts to get a list of local ICE candidates.
        /// </summary>
        //private async Task GetIceCandidatesAsync()
        //{
        //    // The media is being multiplexed so the audio and video RTP channel is the same.
        //    var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //    if (rtpChannel == null)
        //    {
        //        throw new ApplicationException("Cannot start gathering ICE candidates without an RTP channel.");
        //    }
        //    else
        //    {
        //        var localIPAddresses = _offerAddresses ?? NetServices.GetAllLocalIPAddresses();
        //        IceNegotiationStartedAt = DateTime.Now;
        //        LocalIceCandidates = new List<IceCandidate>();

        //        foreach (var address in localIPAddresses.Where(x => x.AddressFamily == rtpChannel.RTPLocalEndPoint.AddressFamily))
        //        {
        //            var iceCandidate = new IceCandidate(address, (ushort)rtpChannel.RTPPort);

        //            if (_turnServerEndPoint != null)
        //            {
        //                iceCandidate.TurnServer = new TurnServer() { ServerEndPoint = _turnServerEndPoint };
        //                iceCandidate.InitialStunBindingCheck = SendTurnServerBindingRequest(iceCandidate);
        //            }

        //            LocalIceCandidates.Add(iceCandidate);
        //        }

        //        await Task.WhenAll(LocalIceCandidates.Where(x => x.InitialStunBindingCheck != null).Select(x => x.InitialStunBindingCheck)).ConfigureAwait(false);
        //    }
        //}

        public void ProcessStunMessage(STUNv2Message stunMessage, IPEndPoint receivedOn)
        {
            IPEndPoint remoteEndPoint = (!receivedOn.Address.IsIPv4MappedToIPv6) ? receivedOn : new IPEndPoint(receivedOn.Address.MapToIPv4(), receivedOn.Port);

            //logger.LogDebug($"STUN message received from remote {remoteEndPoint} {stunMessage.Header.MessageType}.");

            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
            {
                STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                // ToDo: Check authentication.

                string localIcePassword = LocalIcePassword;
                byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(localIcePassword, true);
                //iceCandidate.LocalRtpSocket.SendTo(stunRespBytes, remoteEndPoint);
                _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunRespBytes);

                //iceCandidate.LastStunRequestReceivedAt = DateTime.Now;
                //iceCandidate.IsStunRemoteExchangeComplete = true;

                //if (remoteEndPoint == null)
                //{
                //RemoteEndPoint = remoteEndPoint;
                //SetDestination(SDPMediaTypesEnum.audio, RemoteEndPoint, RemoteEndPoint);
                //OnIceConnected?.Invoke(iceCandidate, remoteEndPoint);
                //IceConnectionState = RTCIceConnectionState.connected;
                //}

                if (_remoteCandidates != null && !_remoteCandidates.Any(x =>
                     (x.address == remoteEndPoint.Address.ToString() || x.relatedAddress == remoteEndPoint.Address.ToString()) &&
                     (x.port == remoteEndPoint.Port || x.relatedPort == remoteEndPoint.Port)))
                {
                    // This STUN request has come from a socket not in the remote ICE candidates list. Add it so we can send our STUN binding request to it.
                    // RTCIceCandidate remoteIceCandidate = new IceCandidate("udp", remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.host);
                    RTCIceCandidate peerRflxCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
                    peerRflxCandidate.SetAddressProperties(RTCIceProtocol.udp, remoteEndPoint.Address, (ushort)remoteEndPoint.Port, RTCIceCandidateType.prflx, null, 0);
                    logger.LogDebug($"Adding peer reflex ICE candidate for {remoteEndPoint}.");
                    _remoteCandidates.Add(peerRflxCandidate);
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
            {
                if (ConnectionState != RTCIceConnectionState.connected)
                {
                    logger.LogDebug($"ICE session setting connected remote end point to {remoteEndPoint}.");

                    _connectedRemoteEndPoint = remoteEndPoint;

                    ConnectionState = RTCIceConnectionState.connected;
                    OnIceConnectionStateChange?.Invoke(RTCIceConnectionState.connected);
                }

                // TODO: What needs to be done here?

                //if (_turnServerEndPoint != null && remoteEndPoint.ToString() == _turnServerEndPoint.ToString())
                //{
                //    if (iceCandidate.IsGatheringComplete == false)
                //    {
                //        var reflexAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress) as STUNv2XORAddressAttribute;

                //        if (reflexAddressAttribute != null)
                //        {
                //            iceCandidate.StunRflxIPEndPoint = new IPEndPoint(reflexAddressAttribute.Address, reflexAddressAttribute.Port);
                //            iceCandidate.IsGatheringComplete = true;

                //            logger.LogDebug("ICE gathering complete for local socket " + iceCandidate.RtpChannel.RTPLocalEndPoint + ", rflx address " + iceCandidate.StunRflxIPEndPoint + ".");
                //        }
                //        else
                //        {
                //            iceCandidate.IsGatheringComplete = true;

                //            logger.LogDebug("The STUN binding response received on " + iceCandidate.RtpChannel.RTPLocalEndPoint + " from " + remoteEndPoint + " did not have an XORMappedAddress attribute, rlfx address can not be determined.");
                //        }
                //    }
                //}
                //else
                //{
                //    iceCandidate.LastStunResponseReceivedAt = DateTime.Now;

                //    if (iceCandidate.IsStunLocalExchangeComplete == false)
                //    {
                //        iceCandidate.IsStunLocalExchangeComplete = true;
                //        logger.LogDebug("WebRTC client STUN exchange complete for call " + CallID + ", candidate local socket " + iceCandidate.RtpChannel.RTPLocalEndPoint + ", remote socket " + remoteEndPoint + ".");

                //        SetIceConnectionState(IceConnectionStatesEnum.Connected);
                //    }
                //}
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
            {
                logger.LogWarning($"A STUN binding error response was received from {remoteEndPoint}.");
            }
            else
            {
                logger.LogWarning($"An unrecognised STUN request was received from {remoteEndPoint}.");
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
            try
            {
                if (ConnectionState == RTCIceConnectionState.checking && _checklist != null && _checklist.Count > 0)
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

                            var nextEntry = _checklist.Where(x => x.State == ChecklistEntryState.Waiting).FirstOrDefault();

                            if(nextEntry != null)
                            {
                                DoConnectivityCheck(nextEntry);
                            }

                            //logger.LogDebug($"Send STUN connectivity checks, local candidates {_candidates?.Count()}, remote candidates {_remoteCandidates?.Count()}.");

                            // If one of the ICE candidates has the remote RTP socket set then the negotiation is complete and the STUN checks are to keep the connection alive.
                            //if (ConnectionState == RTCIceConnectionState.connected)
                            //{
                            //    // Remote RTP endpoint gets set when the DTLS negotiation is finished.
                            //    if (_connectedRemoteEndPoint != null)
                            //    {
                            //        //logger.LogDebug("Sending STUN connectivity check to client " + iceCandidate.RemoteRtpEndPoint + ".");

                            //        string localUser = LocalIceUser;

                            //        STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                            //        stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                            //        stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
                            //        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                            //        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                            //        byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                            //        _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, _connectedRemoteEndPoint, stunReqBytes);

                            //        //_lastStunSentAt = DateTime.Now;
                            //    }
                            //}
                            //else
                            //{
                            if (_remoteCandidates.Count() > 0 && _candidates != null)
                            {
                                foreach (var localIceCandidate in _candidates.Where(x => x.IsStunLocalExchangeComplete == false && x.StunConnectionRequestAttempts < MAXIMUM_STUN_CONNECTION_ATTEMPTS))
                                {
                                    localIceCandidate.StunConnectionRequestAttempts++;

                                    foreach (var remoteIceCandidate in _remoteCandidates.Where(x => x.protocol == RTCIceProtocol.udp
                                        && x.address.NotNullOrBlank() && x.HasConnectionError == false))
                                    {

                                    }
                                }
                            }
                            //}
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ProcessChecklist. " + excp);
                //m_stunChecksTimer?.Dispose();
            }
        }

        /// <summary>
        /// Performs a connectivity check for a single candidate pair entry.
        /// </summary>
        /// <param name="candidatePair">The candidate pair to perform a connectivity check for.</param>
        /// <remarks>As specified in https://tools.ietf.org/html/rfc8445#section-7.2.4.</remarks>
        private void DoConnectivityCheck(ChecklistEntry candidatePair)
        {
            candidatePair.State = ChecklistEntryState.InProgress;

            IPAddress remoteAddress = IPAddress.Parse(candidatePair.RemoteCandidate.address);
            IPEndPoint remoteEndPoint = new IPEndPoint(remoteAddress, candidatePair.RemoteCandidate.port);

            logger.LogDebug($"Sending ICE connectivity check from {_rtpChannel.RTPLocalEndPoint} to {remoteEndPoint}.");

            string localUser = LocalIceUser;

            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));
            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

            _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, remoteEndPoint, stunReqBytes);

            //localIceCandidate.LastSTUNSendAt = DateTime.Now;
        }

        //private async Task SendTurnServerBindingRequest(IceCandidate iceCandidate)
        //{
        //    var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //    int attempt = 1;

        //    while (attempt < INITIAL_STUN_BINDING_ATTEMPTS_LIMIT && !IsConnected && !IsClosed && !iceCandidate.IsGatheringComplete)
        //    {
        //        logger.LogDebug($"Sending STUN binding request {attempt} from {rtpChannel.RTPLocalEndPoint} to {iceCandidate.TurnServer.ServerEndPoint}.");

        //        STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
        //        stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //        byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

        //        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);

        //        await Task.Delay(INITIAL_STUN_BINDING_PERIOD_MILLISECONDS).ConfigureAwait(false);

        //        attempt++;
        //    }

        //    iceCandidate.IsGatheringComplete = true;
        //}

        //private void AllocateTurn(IceCandidate iceCandidate)
        //{
        //    try
        //    {
        //        var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //        if (iceCandidate.TurnAllocateAttempts >= MAXIMUM_TURN_ALLOCATE_ATTEMPTS)
        //        {
        //            logger.LogDebug("TURN allocation for local socket " + iceCandidate.NetworkAddress + " failed after " + iceCandidate.TurnAllocateAttempts + " attempts.");

        //            iceCandidate.IsGatheringComplete = true;
        //        }
        //        else
        //        {
        //            iceCandidate.TurnAllocateAttempts++;

        //            //logger.LogDebug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

        //            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
        //            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
        //            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));   // UDP
        //            byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);
        //            rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, iceCandidate.TurnServer.ServerEndPoint, stunReqBytes);
        //        }
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError("Exception AllocateTurn. " + excp);
        //    }
        //}

        //private void CreateTurnPermissions()
        //{
        //    try
        //    {
        //        var rtpChannel = GetRtpChannel(SDPMediaTypesEnum.audio);

        //        var localTurnIceCandidate = (from cand in LocalIceCandidates where cand.TurnRelayIPEndPoint != null select cand).First();
        //        var remoteTurnCandidate = (from cand in RemoteIceCandidates where cand.type == RTCIceCandidateType.relay select cand).First();

        //        // Send create permission request
        //        STUNv2Message turnPermissionRequest = new STUNv2Message(STUNv2MessageTypesEnum.CreatePermission);
        //        turnPermissionRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
        //        //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
        //        turnPermissionRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, remoteTurnCandidate.port, IPAddress.Parse(remoteTurnCandidate.NetworkAddress)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
        //        turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

        //        MD5 md5 = new MD5CryptoServiceProvider();
        //        byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

        //        byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
        //        //localTurnIceCandidate.LocalRtpSocket.SendTo(turnPermissionReqBytes, localTurnIceCandidate.TurnServer.ServerEndPoint);
        //        rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, localTurnIceCandidate.TurnServer.ServerEndPoint, turnPermissionReqBytes);
        //    }
        //    catch (Exception excp)
        //    {
        //        logger.LogError("Exception CreateTurnPermissions. " + excp);
        //    }
        //}
    }
}
