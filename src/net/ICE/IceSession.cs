//-----------------------------------------------------------------------------
// Filename: IceSession.cs
//
// Description: Represents a ICE Session as described in the Interactive
// Connectivity Establishment RFC8445 https://tools.ietf.org/html/rfc8445.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An ICE session carries out connectivity checks with a remote peer in an
    /// attempt to determine the best destination end point to communicate with the
    /// remote party.
    /// </summary>
    /// <remarks>
    /// From https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-4.2.5:
    ///   "The transport address from the peer for the default destination
    ///   is set to IPv4/IPv6 address values "0.0.0.0"/"::" and port value
    ///   of "9".  This MUST NOT be considered as a ICE failure by the peer
    ///   agent and the ICE processing MUST continue as usual."
    /// </remarks>
    public class IceSession
    {
        private const int ICE_UFRAG_LENGTH = 4;
        private const int ICE_PASSWORD_LENGTH = 24;

        private const int INITIAL_STUN_BINDING_PERIOD_MILLISECONDS = 1000;       // The period to send the initial STUN requests used to get an ICE candidates public IP address.
        private const int INITIAL_STUN_BINDING_ATTEMPTS_LIMIT = 3;                // The maximum number of binding attempts to determine a local socket's public IP address before giving up.
        private const int CONNECTIVITY_CHECKS_PERIOD_MILLISCONDS = 1000;
        private const int ICE_CONNECTED_NO_COMMUNICATIONS_TIMEOUT_SECONDS = 35; // If there are no messages received (STUN/RTP/RTCP) within this period the session will be closed.
        private const int MAXIMUM_TURN_ALLOCATE_ATTEMPTS = 4;
        private const int MAXIMUM_STUN_CONNECTION_ATTEMPTS = 5;

        private const int STUN_CHECK_BASE_PERIOD_MILLISECONDS = 5000;
        private const float STUN_CHECK_LOW_RANDOMISATION_FACTOR = 0.5F;
        private const float STUN_CHECK_HIGH_RANDOMISATION_FACTOR = 1.5F;

        private static ILogger logger = Log.Logger;

        private RTPChannel _rtpChannel;

        public RTCIceGatheringState GatheringState { get; private set; } = RTCIceGatheringState.@new;

        public RTCIceConnectionState ConnectionState { get; private set; } = RTCIceConnectionState.@new;

        /// <summary>
        /// The list of host ICE candidates that have been gathered for this peer.
        /// </summary>
        public ConcurrentBag<RTCIceCandidate> Candidates
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
        private ConcurrentBag<RTCIceCandidate> _candidates;
        private ConcurrentBag<RTCIceCandidate> _remoteCandidates = new ConcurrentBag<RTCIceCandidate>();

        /// <summary>
        /// The list of ICE candidates from the remote peer.
        /// </summary>
        public List<RTCIceCandidate> PeerCandidates { get; private set; }

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
        public IceSession(RTPChannel rtpChannel)
        {
            _rtpChannel = rtpChannel;

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
            _stunChecksTimer = new Timer(SendStunConnectivityChecks, null, 0, CONNECTIVITY_CHECKS_PERIOD_MILLISCONDS);
        }

        public void SetRemoteCredentials(string username, string password)
        {
            RemoteIceUser = username;
            RemoteIcePassword = password;
        }

        public void Close(string reason)
        {
            if (!_closed)
            {
                _closed = true;
                _stunChecksTimer.Dispose();
            }
        }

        public void AddRemoteCandidate(RTCIceCandidateInit candidateInit)
        {
            RTCIceCandidate candidate = new RTCIceCandidate(candidateInit);
            _remoteCandidates.Add(candidate);
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
        /// <returns>A list of "host" ICE candidates for the local machine.</returns>
        private ConcurrentBag<RTCIceCandidate> GetHostCandidates()
        {
            ConcurrentBag<RTCIceCandidate> hostCandidates = new ConcurrentBag<RTCIceCandidate>();
            RTCIceCandidateInit init = new RTCIceCandidateInit { usernameFragment = LocalIceUser };

            foreach (var localAddress in NetServices.LocalIPAddresses.Where(x =>
                 !IPAddress.IsLoopback(x) && !x.IsIPv4MappedToIPv6 && !x.IsIPv6LinkLocal && !x.IsIPv6SiteLocal))
            {
                var hostCandidate = new RTCIceCandidate(init);
                hostCandidate.SetAddressProperties(RTCIceProtocol.udp, localAddress, (ushort)_rtpChannel.RTPPort, RTCIceCandidateType.host, null, 0);
                hostCandidates.Add(hostCandidate);
            }

            return hostCandidates;
        }

        /// <summary>
        /// Attempts to get a list of server-reflexive candidates using the local "host" candidates
        /// and a STUN or TURN server.
        /// </summary>
        /// <returns></returns>
        private List<RTCIceCandidate> GetServerRelexiveCandidates()
        {
            throw new NotImplementedException();
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

                    // Some browsers require a STUN binding request from our end before the DTLS handshake will be initiated.
                    // The STUN connectivity checks are already scheduled but we can speed things up by sending a binding
                    // request immediately.
                    SendStunConnectivityChecks(null);
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

        private void SendStunConnectivityChecks(Object stateInfo)
        {
            try
            {
                lock (_stunChecksTimer)
                {
                    //logger.LogDebug($"Send STUN connectivity checks, local candidates {_candidates?.Count()}, remote candidates {_remoteCandidates?.Count()}.");

                    // If one of the ICE candidates has the remote RTP socket set then the negotiation is complete and the STUN checks are to keep the connection alive.
                    if (RemoteIceUser != null && RemoteIcePassword != null)
                    {
                        if (ConnectionState == RTCIceConnectionState.connected)
                        {
                            // Remote RTP endpoint gets set when the DTLS negotiation is finished.
                            if (_connectedRemoteEndPoint != null)
                            {
                                //logger.LogDebug("Sending STUN connectivity check to client " + iceCandidate.RemoteRtpEndPoint + ".");

                                string localUser = LocalIceUser;

                                STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
                                stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                                _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, _connectedRemoteEndPoint, stunReqBytes);

                                //_lastStunSentAt = DateTime.Now;
                            }
                        }
                        else
                        {
                            if (_remoteCandidates.Count() > 0 && _candidates != null)
                            {
                                foreach (var localIceCandidate in _candidates.Where(x => x.IsStunLocalExchangeComplete == false && x.StunConnectionRequestAttempts < MAXIMUM_STUN_CONNECTION_ATTEMPTS))
                                {
                                    localIceCandidate.StunConnectionRequestAttempts++;

                                    // ToDo: Include srflx and relay addresses.

                                    // Only supporting UDP candidates at this stage.
                                    foreach (var remoteIceCandidate in _remoteCandidates.Where(x => x.protocol == RTCIceProtocol.udp && x.address.NotNullOrBlank() && x.HasConnectionError == false))
                                    {
                                        try
                                        {
                                            IPAddress remoteAddress = IPAddress.Parse(remoteIceCandidate.address);

                                            logger.LogDebug($"Sending authenticated STUN binding request {localIceCandidate.StunConnectionRequestAttempts} from {_rtpChannel.RTPLocalEndPoint} to WebRTC peer at {remoteIceCandidate.address}:{remoteIceCandidate.port}.");

                                            string localUser = LocalIceUser;

                                            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                            stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + localUser);
                                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));
                                            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                                            _rtpChannel.SendAsync(RTPChannelSocketsEnum.RTP, new IPEndPoint(IPAddress.Parse(remoteIceCandidate.address), remoteIceCandidate.port), stunReqBytes);

                                            localIceCandidate.LastSTUNSendAt = DateTime.Now;
                                        }
                                        catch (System.Net.Sockets.SocketException sockExcp)
                                        {
                                            logger.LogWarning($"SocketException sending STUN request to {remoteIceCandidate.address}:{remoteIceCandidate.port}, removing candidate. {sockExcp.Message}");
                                            remoteIceCandidate.HasConnectionError = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    //if (!_closed)
                    //{
                    //    var interval = GetNextStunCheckInterval(STUN_CHECK_BASE_PERIOD_MILLISECONDS);

                    //    if (m_stunChecksTimer == null)
                    //    {
                    //        m_stunChecksTimer = new Timer(SendStunConnectivityChecks, null, interval, interval);
                    //    }
                    //    else
                    //    {
                    //        m_stunChecksTimer.Change(interval, interval);
                    //    }
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendStunConnectivityCheck. " + excp);
                //m_stunChecksTimer?.Dispose();
            }
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

        /// <summary>
        /// Gets a pseudo-randomised interval for the next STUN check period.
        /// </summary>
        /// <param name="baseInterval">The base check interval to randomise.</param>
        /// <returns>A value in milliseconds to wait before performing the next STUN check.</returns>
        //private int GetNextStunCheckInterval(int baseInterval)
        //{
        //    return Crypto.GetRandomInt((int)(STUN_CHECK_LOW_RANDOMISATION_FACTOR * baseInterval),
        //        (int)(STUN_CHECK_HIGH_RANDOMISATION_FACTOR * baseInterval));
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
