﻿//-----------------------------------------------------------------------------
// Filename: WebRtcPeer.cs
//
// Description: Represents a peer involved in a WebRTC connection.
//
// References:
// - "WebRTC 1.0: Real-time Communication Between Browsers" https://w3c.github.io/webrtc-pc/.
// - "Overview: Real Time Protocols for Browser-based Applications draft-ietf-rtcweb-overview-15" https://tools.ietf.org/html/draft-ietf-rtcweb-overview-15
//
// History:
// 26 Feb 2016	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class WebRtcPeer
    {
        private const int WEBRTC_START_PORT = 49000;
        private const int WEBRTC_END_PORT = 53000;
        private const int EXPIRE_CLIENT_SECONDS = 3;
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;
        private const int ICE_GATHERING_TIMEOUT_MILLISECONDS = 5000;
        private const int ICE_CONNECTION_LIMIT_SECONDS = 30;                    // The amount of time to give the ICE attempts time to establish a connection.
        private const int INITIAL_STUN_BINDING_PERIOD_MILLISECONDS = 500;       // The period to send the initial STUN requests used to get an ICE candidates public IP address.
        private const int INITIAL_STUN_BINDING_ATTEMPTS_LIMIT = 3;              // The maximum number of binding attempts to determine a local socket's public IP address before giving up.
        private const int ESTABLISHED_STUN_BINDING_PERIOD_MILLISECONDS = 1000;  // The period to send STUN binding requests to remote peers once the ICE gathering stage is complete.
        private const int COMMUNICATION_FAILURE_COUNT_FOR_CLOSE = 20;           // If a peer gets this number of communication failures on a socket it will close the peer.
        private const string AUTOMATIC_PRIVATE_ADRRESS_PREFIX = "169.254";      // The prefix of the IP address range automatically assigned to interfaces using DHCP before they have acquired an address.
        private const int MAXIMUM_TURN_ALLOCATE_ATTEMPTS = 4;
        private const int MAXIMUM_STUN_CONNECTION_ATTEMPTS = 5;
        private const int ICE_TIMEOUT_SECONDS = 5;                              // If no response is received to the STUN connectivity check within this number of seconds the WebRTC connection will be assumed to be broken.
        private const int CLOSE_SOCKETS_TIMEOUT_WAIT_MILLISECONDS = 3000;

        private static string _sdpOfferTemplate = @"v=0
o=- {0} 2 IN IP4 127.0.0.1
s=-
t=0 0
m=video {1} RTP/SAVPF " + PAYLOAD_TYPE_ID + @"
c=IN IP4 {2}
{3}
a=ice-ufrag:{4}
a=ice-pwd:{5}
a=fingerprint:sha-256 {6}
a=setup:actpass
a=mid:video
a=sendrecv
a=rtcp-mux
a=rtpmap:" + PAYLOAD_TYPE_ID + @" VP8/90000
";

        private static ILog logger = AppState.logger;

        public string CallID;
        public string SDP;
        public string SdpSessionID;
        public string LocalIceUser;
        public string LocalIcePassword;
        public string RemoteIceUser;
        public string RemoteIcePassword;
        public bool IsDtlsNegotiationComplete;
        public uint SSRC;
        public ushort SequenceNumber;
        public uint LastTimestamp;
        public DateTime LastRtcpSenderReportSentAt = DateTime.MinValue;
        public DateTime IceNegotiationStartedAt;
        public List<IceCandidate> LocalIceCandidates;
        public bool IsClosed;
        public IceConnectionStatesEnum IceConnectionState = IceConnectionStatesEnum.None;

        private List<IceCandidate> _remoteIceCandidates = new List<IceCandidate>();
        public List<IceCandidate> RemoteIceCandidates
        {
            get { return _remoteIceCandidates; }
        }

        private string _dtlsCertificateFingerprint;
        private IPEndPoint _turnServerEndPoint;
        private ManualResetEvent _iceGatheringMRE;
        private int _communicationFailureCount = 0;

        public event Action OnClose;
        public event Action<string> OnSdpOfferReady;
        public event Action<IceConnectionStatesEnum> OnIceStateChange;
        public event Action<IceCandidate, byte[], IPEndPoint> OnDtlsPacket;
        public event Action<IceCandidate, byte[], IPEndPoint> OnMediaPacket;

        public void Close()
        {
            try
            {
                if (!IsClosed)
                {
                    IsClosed = true;
                    
                    // Make sure no further packets get passed onto these handlers! They could get deallocated before the sockets shutdown.
                    OnDtlsPacket = null;
                    OnMediaPacket = null;

                    logger.Debug("WebRTC peer for call " + CallID + " closing.");

                    if (LocalIceCandidates != null && LocalIceCandidates.Count > 0)
                    {
                        foreach (var iceCandidate in LocalIceCandidates)
                        {
                            iceCandidate.IsDisconnected = true;

                            if (iceCandidate.LocalRtpSocket != null)
                            {
                                logger.Debug("Closing local ICE candidate socket for " + iceCandidate.LocalAddress + ":" + iceCandidate.Port + ".");

                                try
                                {
                                    iceCandidate.LocalRtpSocket.Shutdown(SocketShutdown.Both);
                                    iceCandidate.LocalRtpSocket.Close();
                                }
                                catch (Exception closeSockExcp)
                                {
                                    logger.Warn("Exception closing WebRTC peer. " + closeSockExcp.Message);
                                }
                            }
                        }
                    }

                    logger.Debug("WebRTC peer waiting for all ICE candidate RTP listener tasks to complete.");

                    Task.WaitAll(LocalIceCandidates.Where(x => x.RtpListenerTask != null).Select(x => x.RtpListenerTask).ToArray(), CLOSE_SOCKETS_TIMEOUT_WAIT_MILLISECONDS);

                    logger.Debug("WebRTC peer RTP listener tasks now complete.");

                    OnClose?.Invoke();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRtcPeer.Close. " + excp);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dtlsCertificateFingerprint">The SHA256 fingerprint that gets placed in the SDP offer for this WebRTC peer. It must match the certificate being used
        /// in the DTLS negotiation.</param>
        /// <param name="turnServerEndPoint">An optional parameter that can be used include a TURN server in this peer's ICE candidate gathering.</param>
        public void Initialise(string dtlsCertificateFingerprint, IPEndPoint turnServerEndPoint)
        {
            if (dtlsCertificateFingerprint.IsNullOrBlank())
            {
                throw new ArgumentNullException("dtlsCertificateFingerprint", "A DTLS certificate fingerprint must be supplied when initialising a new WebRTC peer (to get the fingerprint use: openssl x509 -fingerprint -sha256 -in server-cert.pem).");
            }

            try
            {
                _dtlsCertificateFingerprint = dtlsCertificateFingerprint;
                _turnServerEndPoint = turnServerEndPoint;

                _iceGatheringMRE = new ManualResetEvent(false);

                DateTime startGatheringTime = DateTime.Now;

                SetIceConnectionState(IceConnectionStatesEnum.Gathering);

                GetIceCandidates(_iceGatheringMRE);

                _iceGatheringMRE.WaitOne(ICE_GATHERING_TIMEOUT_MILLISECONDS, true);

                logger.Debug("ICE gathering completed for call " + CallID + " in " + DateTime.Now.Subtract(startGatheringTime).TotalMilliseconds + "ms, number of local sockets " + LocalIceCandidates.Count + ".");

                SetIceConnectionState(IceConnectionStatesEnum.GatheringComplete);

                if (LocalIceCandidates.Count == 0)
                {
                    logger.Warn("No local socket candidates were found for WebRTC call " + CallID + ", closing peer.");
                    Close();
                }
                else
                {
                    string iceCandidateString = null;

                    logger.Debug("ICE Candidates for " + CallID + ": ");

                    foreach (var iceCandidate in LocalIceCandidates)
                    {
                        iceCandidateString += iceCandidate.ToString();
                    }

                    var localIceUser = Crypto.GetRandomString(20);
                    var localIcePassword = Crypto.GetRandomString(20) + Crypto.GetRandomString(20);

                    var offer = String.Format(_sdpOfferTemplate, Crypto.GetRandomInt(10).ToString(), (LocalIceCandidates.First().LocalRtpSocket.LocalEndPoint as IPEndPoint).Port, LocalIceCandidates.First().LocalAddress, iceCandidateString.TrimEnd(), localIceUser, localIcePassword, _dtlsCertificateFingerprint);

                    //logger.Debug("WebRTC Offer SDP: " + offer);

                    SDP = offer;
                    LocalIceUser = localIceUser;
                    LocalIcePassword = localIcePassword;
                    SSRC = Convert.ToUInt32(Crypto.GetRandomInt(8));
                    SequenceNumber = 1;

                    Task.Run(() => { SendStunConnectivityChecks(); });

                    logger.Debug("Sending SDP offer for WebRTC call " + CallID + ".");

                    if (OnSdpOfferReady != null)
                    {
                        OnSdpOfferReady(offer);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRtcPeer.Initialise. " + excp);
                Close();
            }
        }

        public void AppendRemoteIceCandidate(IceCandidate remoteIceCandidate)
        {
            IPAddress candidateIPAddress = null;

            //foreach (var iceCandidate in remoteIceCandidates)
            //{
            //    logger.Debug("Appending remote ICE candidate " + iceCandidate.NetworkAddress + ":" + iceCandidate.Port + ".");
            //}

            if (remoteIceCandidate.Transport.ToLower() != "udp")
            {
                logger.Debug("Omitting remote non-UDP ICE candidate. " + remoteIceCandidate.RawString + ".");
            }
            else if (!IPAddress.TryParse(remoteIceCandidate.NetworkAddress, out candidateIPAddress))
            {
                logger.Debug("Omitting ICE candidate with unrecognised IP Address. " + remoteIceCandidate.RawString + ".");
            }
            else if (candidateIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                logger.Debug("Omitting IPv6 ICE candidate. " + remoteIceCandidate.RawString + ".");
            }
            else
            {
                // ToDo: Add srflx and relay endpoints as hosts as well.

                if (!_remoteIceCandidates.Any(x => x.NetworkAddress == remoteIceCandidate.NetworkAddress && x.Port == remoteIceCandidate.Port))
                {
                    logger.Debug("Adding remote ICE candidate: " + remoteIceCandidate.CandidateType + " " + remoteIceCandidate.NetworkAddress + ":" + remoteIceCandidate.Port + " (" + remoteIceCandidate.RawString + ").");
                    _remoteIceCandidates.Add(remoteIceCandidate);
                }
            }
        }

        private void GetIceCandidates(ManualResetEvent iceGatheringCompleteMRE)
        {
            IceNegotiationStartedAt = DateTime.Now;
            LocalIceCandidates = new List<IceCandidate>();
            List<UnicastIPAddressInformation> addresses = new List<UnicastIPAddressInformation>();

            // CAUTION: GetUnicastAddresses canj take up to 60 seconds to return if the machine has IP addresses in the IpDadStateTentative state,
            // such as DHCP addresses still checking for their lease. More info at: https://msdn.microsoft.com/en-us/library/windows/desktop/aa814507(v=vs.85).aspx 
            //var addresses = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetUnicastAddresses()
            //    .Where(x =>
            //    x.Address.AddressFamily == AddressFamily.InterNetwork &&    // Exclude IPv6 at this stage.
            //    IPAddress.IsLoopback(x.Address) == false &&
            //    (x.Address != null && x.Address.ToString().StartsWith(AUTOMATIC_PRIVATE_ADRRESS_PREFIX) == false));

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IPAddress.IsLoopback(ip.Address) == false && ip.IsTransient == false)
                        {
                            //Console.WriteLine(ip.Address.ToString());
                            addresses.Add(ip);
                        }
                    }
                }
            }

            foreach (var address in addresses)
            {
                logger.Debug("Attempting to create RTP socket with IP address " + address.Address + ".");

                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(address.Address, WEBRTC_START_PORT, WEBRTC_END_PORT, false, out rtpSocket, out controlSocket);

                if (rtpSocket != null)
                {
                    logger.Debug("RTP socket successfully created on " + rtpSocket.LocalEndPoint + ".");

                    var iceCandidate = new IceCandidate() { LocalAddress = address.Address, Port = ((IPEndPoint)rtpSocket.LocalEndPoint).Port, LocalRtpSocket = rtpSocket, LocalControlSocket = controlSocket,
                        TurnServer = (_turnServerEndPoint != null) ? new TurnServer() { ServerEndPoint = _turnServerEndPoint } : null };

                    LocalIceCandidates.Add(iceCandidate);

                    var listenerTask = Task.Run(() => { StartWebRtcRtpListener(iceCandidate); });

                    iceCandidate.RtpListenerTask = listenerTask;

                    if (_turnServerEndPoint != null)
                    {
                        var stunBindingTask = Task.Run(() => { SendInitialStunBindingRequest(iceCandidate, iceGatheringCompleteMRE); });
                    }
                    else
                    {
                        iceCandidate.IsGatheringComplete = true;

                        // Potentially save a few seconds if all the ICE candidates are now ready.
                        if (LocalIceCandidates.All(x => x.IsGatheringComplete))
                        {
                            iceGatheringCompleteMRE.Set();
                        }
                    }
                }
            }
        }

        private void SendInitialStunBindingRequest(IceCandidate iceCandidate, ManualResetEvent iceGatheringCompleteMRE)
        {
            int attempt = 1;

            while (attempt < INITIAL_STUN_BINDING_ATTEMPTS_LIMIT && !IsClosed && !iceCandidate.IsGatheringComplete)
            {
                logger.Debug("Sending STUN binding request " + attempt + " from " + iceCandidate.LocalRtpSocket.LocalEndPoint + " to " + iceCandidate.TurnServer.ServerEndPoint + ".");

                STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

                iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.TurnServer.ServerEndPoint);

                Thread.Sleep(INITIAL_STUN_BINDING_PERIOD_MILLISECONDS);

                attempt++;
            }

            iceCandidate.IsGatheringComplete = true;

            // Potentially save a few seconds if all the ICE candidates are now ready.
            if (LocalIceCandidates.All(x => x.IsGatheringComplete))
            {
                iceGatheringCompleteMRE.Set();
            }
        }

        private void SendStunConnectivityChecks()
        {
            try
            {
                while (!IsClosed)
                {
                    try
                    {
                        // If one of the ICE candidates has the remote RTP socket set then the negotiation is complete and the STUN checks are to keep the connection alive.
                        if (LocalIceCandidates.Any(x => x.IsConnected == true))
                        {
                            var iceCandidate = LocalIceCandidates.First(x => x.IsConnected == true);

                            // Remote RTP endpoint gets set when the DTLS negotiation is finished.
                            if (iceCandidate.RemoteRtpEndPoint != null)
                            {
                                //logger.Debug("Sending STUN connectivity check to client " + iceCandidate.RemoteRtpEndPoint + ".");

                                STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + LocalIceUser);
                                stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                                iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.RemoteRtpEndPoint);

                                iceCandidate.LastSTUNSendAt = DateTime.Now;
                            }

                            var secondsSinceLastResponse = DateTime.Now.Subtract(iceCandidate.LastCommunicationAt).TotalSeconds;

                            if (secondsSinceLastResponse > ICE_TIMEOUT_SECONDS)
                            {
                                logger.Warn("No STUN response was received on a connected ICE connection for " + secondsSinceLastResponse + "s, closing connection.");

                                iceCandidate.IsDisconnected = true;

                                if (LocalIceCandidates.Any(x => x.IsConnected == true) == false)
                                {
                                    // If there are no connected local candidates left close the peer.
                                    Close();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            if (_remoteIceCandidates.Count() > 0)
                            {
                                foreach (var localIceCandidate in LocalIceCandidates.Where(x => x.IsStunLocalExchangeComplete == false && x.StunConnectionRequestAttempts < MAXIMUM_STUN_CONNECTION_ATTEMPTS))
                                {
                                    localIceCandidate.StunConnectionRequestAttempts++;

                                    // ToDo: Include srflx and relay addresses.

                                    foreach (var remoteIceCandidate in RemoteIceCandidates.Where(x => x.Transport != "tcp" && x.NetworkAddress.NotNullOrBlank()))   // Only supporting UDP candidates at this stage.
                                    {
                                        IPAddress remoteAddress = IPAddress.Parse(remoteIceCandidate.NetworkAddress);

                                        logger.Debug("Sending authenticated STUN binding request " + localIceCandidate.StunConnectionRequestAttempts + " from " + localIceCandidate.LocalRtpSocket.LocalEndPoint + " to WebRTC peer at " + remoteIceCandidate.NetworkAddress + ":" + remoteIceCandidate.Port + ".");

                                        STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                        stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                        stunRequest.AddUsernameAttribute(RemoteIceUser + ":" + LocalIceUser);
                                        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                        stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                        byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(RemoteIcePassword, true);

                                        localIceCandidate.LocalRtpSocket.SendTo(stunReqBytes, new IPEndPoint(IPAddress.Parse(remoteIceCandidate.NetworkAddress), remoteIceCandidate.Port));

                                        localIceCandidate.LastSTUNSendAt = DateTime.Now;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception SendStunConnectivityCheck ConnectivityCheck. " + excp);
                    }

                    if (!IsClosed)
                    {
                        Thread.Sleep(ESTABLISHED_STUN_BINDING_PERIOD_MILLISECONDS);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendStunConnectivityCheck. " + excp);
            }
        }

        private void StartWebRtcRtpListener(IceCandidate iceCandidate)
        {
            string localEndPoint = "?";

            try
            {
                localEndPoint = iceCandidate.LocalRtpSocket.LocalEndPoint.ToString();

                logger.Debug("Starting WebRTC RTP listener for call " + CallID + " on socket " + localEndPoint + ".");

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                UdpClient localSocket = new UdpClient();
                localSocket.Client = iceCandidate.LocalRtpSocket;

                while (!IsClosed)
                {
                    try
                    {
                        //logger.Debug("ListenToReceiverWebRTCClient Receive.");
                        byte[] buffer = localSocket.Receive(ref remoteEndPoint);

                        iceCandidate.LastCommunicationAt = DateTime.Now;

                        //logger.Debug(buffer.Length + " bytes read on Receiver Client media socket from " + remoteEndPoint.ToString() + ".");

                        //if (buffer.Length > 3 && buffer[0] == 0x16 && buffer[1] == 0xfe)
                        if (buffer[0] >= 20 && buffer[0] <= 64)
                        {
                            //OnMediaPacket(iceCandidate, buffer, remoteEndPoint);
                            if (OnDtlsPacket != null)
                            {
                                OnDtlsPacket(iceCandidate, buffer, remoteEndPoint);
                            }
                        }
                        //else if ((buffer[0] & 0x80) == 0)
                        else if (buffer[0] == 0 || buffer[0] == 1)
                        {
                            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);
                            ProcessStunMessage(iceCandidate, stunMessage, remoteEndPoint);
                        }
                        else
                        {
                            if (OnMediaPacket != null)
                            {
                                OnMediaPacket(iceCandidate, buffer, remoteEndPoint);
                            }
                        }
                    }
                    catch (Exception sockExcp)
                    {
                        _communicationFailureCount++;

                        logger.Warn("Exception ListenToReceiverWebRTCClient Receive (" + localEndPoint + " and " + remoteEndPoint + ", failure count " + _communicationFailureCount + "). " + sockExcp.Message);

                        // Need to be careful about deciding when the connection has failed. Sometimes the STUN requests we send will arrive before the remote peer is ready and cause a socket exception.
                        // Only shutdown the peer if we are sure all ICE intialisation is complete and the socket exception occurred after the RTP had stated flowing.
                        if (iceCandidate.IsStunLocalExchangeComplete && iceCandidate.IsStunRemoteExchangeComplete &&
                            iceCandidate.RemoteRtpEndPoint != null && remoteEndPoint != null && iceCandidate.RemoteRtpEndPoint.ToString() == remoteEndPoint.ToString() &&
                            DateTime.Now.Subtract(IceNegotiationStartedAt).TotalSeconds > 10)
                        {
                            logger.Warn("WebRtc peer communication failure on call " + CallID + " for local RTP socket " + localEndPoint + " and remote RTP socket " + remoteEndPoint + " .");
                            iceCandidate.DisconnectionMessage = sockExcp.Message;
                            break;
                        }
                        else if (_communicationFailureCount > COMMUNICATION_FAILURE_COUNT_FOR_CLOSE)
                        {
                            logger.Warn("WebRtc peer communication failures on call " + CallID + " exceeded limit of " + COMMUNICATION_FAILURE_COUNT_FOR_CLOSE + " closing peer.");
                            break;
                        }
                        //else if (DateTime.Now.Subtract(peer.IceNegotiationStartedAt).TotalSeconds > ICE_CONNECTION_LIMIT_SECONDS)
                        //{
                        //    logger.Warn("WebRTC peer ICE connection establishment timed out on call " + peer.CallID + " for " + iceCandidate.LocalRtpSocket.LocalEndPoint + ".");
                        //    break;
                        //}
                    }
                }

                Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception ListenForWebRTCClient (" + localEndPoint + "). " + excp);
            }
        }

        private void ProcessStunMessage(IceCandidate iceCandidate, STUNv2Message stunMessage, IPEndPoint remoteEndPoint)
        {
            //logger.Debug("STUN message received from remote " + remoteEndPoint + " " + stunMessage.Header.MessageType + ".");

            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
            {
                STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);

                // ToDo: Check authentication.

                byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(LocalIcePassword, true);
                iceCandidate.LocalRtpSocket.SendTo(stunRespBytes, remoteEndPoint);

                iceCandidate.LastStunRequestReceivedAt = DateTime.Now;
                iceCandidate.IsStunRemoteExchangeComplete = true;

                if (_remoteIceCandidates != null && !_remoteIceCandidates.Any(x =>
                     (x.NetworkAddress == remoteEndPoint.Address.ToString() || x.RemoteAddress == remoteEndPoint.Address.ToString()) &&
                     (x.Port == remoteEndPoint.Port || x.RemotePort == remoteEndPoint.Port)))
                {
                    // This STUN request has come from a socket not in the remote ICE candidates list. Add it so we can send our STUN binding request to it.
                    IceCandidate remoteIceCandidate = new IceCandidate()
                    {
                        Transport = "udp",
                        NetworkAddress = remoteEndPoint.Address.ToString(),
                        Port = remoteEndPoint.Port,
                        CandidateType = IceCandidateTypesEnum.host
                    };

                    logger.Debug("Adding missing remote ICE candidate for " + remoteEndPoint + ".");

                    _remoteIceCandidates.Add(remoteIceCandidate);
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
            {
                if (_turnServerEndPoint != null && remoteEndPoint.ToString() == _turnServerEndPoint.ToString())
                {
                    if (iceCandidate.IsGatheringComplete == false)
                    {
                        var reflexAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress) as STUNv2XORAddressAttribute;

                        if (reflexAddressAttribute != null)
                        {
                            iceCandidate.StunRflxIPEndPoint = new IPEndPoint(reflexAddressAttribute.Address, reflexAddressAttribute.Port);
                            iceCandidate.IsGatheringComplete = true;

                            logger.Debug("ICE gathering complete for local socket " + iceCandidate.LocalRtpSocket.LocalEndPoint + ", rflx address " + iceCandidate.StunRflxIPEndPoint + ".");
                        }
                        else
                        {
                            iceCandidate.IsGatheringComplete = true;

                            logger.Debug("The STUN binding response received on " + iceCandidate.LocalRtpSocket.LocalEndPoint + " from " + remoteEndPoint + " did not have an XORMappedAddress attribute, rlfx address can not be determined.");
                        }
                    }
                }
                else
                {
                    iceCandidate.LastStunResponseReceivedAt = DateTime.Now;

                    if (iceCandidate.IsStunLocalExchangeComplete == false)
                    {
                        iceCandidate.IsStunLocalExchangeComplete = true;
                        logger.Debug("WebRTC client STUN exchange complete for call " + CallID + ", candidate local socket " + iceCandidate.LocalRtpSocket.LocalEndPoint + ", remote socket " + remoteEndPoint + ".");

                        SetIceConnectionState(IceConnectionStatesEnum.Connected);
                    }
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
            {
                logger.Warn("A STUN binding error response was received on " + iceCandidate.LocalRtpSocket.LocalEndPoint + " from  " + remoteEndPoint + ".");
            }
            else
            {
                logger.Warn("An unrecognised STUN request was received on " + iceCandidate.LocalRtpSocket.LocalEndPoint + " from " + remoteEndPoint + ".");
            }
        }

        private void AllocateTurn(IceCandidate iceCandidate)
        {
            try
            {
                if (iceCandidate.TurnAllocateAttempts >= MAXIMUM_TURN_ALLOCATE_ATTEMPTS)
                {
                    logger.Debug("TURN allocation for local socket " + iceCandidate.LocalAddress + " failed after " + iceCandidate.TurnAllocateAttempts + " attempts.");

                    iceCandidate.IsGatheringComplete = true;
                }
                else
                {
                    iceCandidate.TurnAllocateAttempts++;

                    //logger.Debug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

                    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
                    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));   // UDP
                    byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);
                    iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.TurnServer.ServerEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AllocateTurn. " + excp);
            }
        }

        private void CreateTurnPermissions()
        {
            try
            {
                var localTurnIceCandidate = (from cand in LocalIceCandidates where cand.TurnRelayIPEndPoint != null select cand).First();
                var remoteTurnCandidate = (from cand in RemoteIceCandidates where cand.CandidateType == IceCandidateTypesEnum.relay select cand).First();

                // Send create permission request
                STUNv2Message turnPermissionRequest = new STUNv2Message(STUNv2MessageTypesEnum.CreatePermission);
                turnPermissionRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
                turnPermissionRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, remoteTurnCandidate.Port, IPAddress.Parse(remoteTurnCandidate.NetworkAddress)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

                MD5 md5 = new MD5CryptoServiceProvider();
                byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

                byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
                localTurnIceCandidate.LocalRtpSocket.SendTo(turnPermissionReqBytes, localTurnIceCandidate.TurnServer.ServerEndPoint);
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateTurnPermissions. " + excp);
            }
        }

        private void SetIceConnectionState(IceConnectionStatesEnum iceConnectionState)
        {
            try
            {
                IceConnectionState = iceConnectionState;

                if (OnIceStateChange != null)
                {
                    OnIceStateChange(iceConnectionState);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetIceConnectionState. " + excp);
            }
        }
    }
}
