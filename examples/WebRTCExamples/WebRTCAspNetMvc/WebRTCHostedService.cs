//-----------------------------------------------------------------------------
// Filename: WebRTCHostedService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net
// server application to handle WebRTC peer connections. Currently it does
// not server or process any media. The main point is as a proof of concept
// to be able to negotiate the ICE checks, DTLS handshake and then receive
// RTP & RTCP packets.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 13 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia;

namespace WebRTCAspNetMvc
{
    public class WebRTCHostedService : IHostedService
    {
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";

        private readonly ILogger<WebRTCHostedService> _logger;

        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

        public WebRTCHostedService(ILogger<WebRTCHostedService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("WebRTCHostedService StartAsync.");

            // Initialise OpenSSL & libsrtp, saves a couple of seconds for the first client connection.
            _logger.LogDebug("Initialising OpenSSL and libsrtp...");
            DtlsHandshake.InitialiseOpenSSL();
            Srtp.InitialiseLibSrtp();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<RTCSessionDescriptionInit> GetOffer(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentNullException("id", "A unique ID parameter must be supplied when creating a new peer connection.");
            }
            else if (_peerConnections.ContainsKey(id))
            {
                throw new ArgumentNullException("id", "The specified peer connection ID is already in use.");
            }

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = new List<RTCCertificate>
                {
                    new RTCCertificate
                    {
                        X_CertificatePath = DTLS_CERTIFICATE_PATH,
                        X_KeyPath = DTLS_KEY_PATH,
                        X_Fingerprint = DTLS_CERTIFICATE_FINGERPRINT
                    }
                }
            };

            var peerConnection = new RTCPeerConnection(pcConfiguration);
            var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);

            MediaStreamTrack audioTrack = new MediaStreamTrack("0", SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);

            //peerConnection.OnRtpPacketReceived += (SDPMediaTypesEnum media, RTPPacket rtpPkt) => _logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, SeqNum {rtpPkt.Header.SequenceNumber}.");
            //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            //peerConnection.OnSendReport += RtpSession_OnSendReport;

            peerConnection.OnTimeout += (mediaType) =>
            {
                peerConnection.Close("remote timeout");
                dtls.Shutdown();
            };

            peerConnection.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    //peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    //peerConnection.OnSendReport -= RtpSession_OnSendReport;
                    _logger.LogDebug($"Peer connection {id} closed.");
                    _peerConnections.TryRemove(id, out _);
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    _logger.LogDebug("Peer connection connected.");
                }
            };

            _ = Task<bool>.Run(() => Task.FromResult(DoDtlsHandshake(peerConnection, dtls)))
            .ContinueWith((t) =>
            {
                _logger.LogDebug($"dtls handshake result {t.Result}.");

                if (t.Result)
                {
                    var remoteEP = peerConnection.IceSession.ConnectedRemoteEndPoint;
                    peerConnection.SetDestination(SDPMediaTypesEnum.audio, remoteEP, remoteEP);
                }
                else
                {
                    dtls.Shutdown();
                    peerConnection.Close("dtls handshake failed.");
                }
            });

            var offerSdp = await peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            _peerConnections.TryAdd(id, peerConnection);

            return offerSdp;
        }

        public async Task SetRemoteDescription(string id, RTCSessionDescriptionInit description)
        {
            if (!_peerConnections.TryGetValue(id, out var pc))
            {
                throw new ApplicationException("No peer connection is available for the specified id.");
            }
            else
            { 
                _logger.LogDebug("Answer SDP: " + description.sdp);
                await pc.setRemoteDescription(description);

            }
        }

        public async Task AddIceCandidate(string id, RTCIceCandidateInit iceCandidate)
        {
            if (!_peerConnections.TryGetValue(id, out var pc))
            {
                throw new ApplicationException("No peer connection is available for the specified id.");
            }
            else
            {
                _logger.LogDebug("ICE Candidate: " + iceCandidate.candidate);
                await pc.addIceCandidate(iceCandidate);
            }

        }

        /// <summary>
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        private bool DoDtlsHandshake(RTCPeerConnection peerConnection, DtlsHandshake dtls)
        {
            _logger.LogDebug("DoDtlsHandshake started.");

            if (!File.Exists(DTLS_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {DTLS_CERTIFICATE_PATH}.");
            }
            else if (!File.Exists(DTLS_KEY_PATH))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {DTLS_KEY_PATH}.");
            }

            int res = dtls.DoHandshakeAsServer((ulong)peerConnection.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle);

            _logger.LogDebug("DtlsContext initialisation result=" + res);

            if (dtls.IsHandshakeComplete())
            {
                _logger.LogDebug("DTLS negotiation complete.");

                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                peerConnection.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                //dtls.Shutdown();

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
