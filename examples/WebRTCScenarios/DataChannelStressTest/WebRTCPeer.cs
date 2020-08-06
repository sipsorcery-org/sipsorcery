//-----------------------------------------------------------------------------
// Filename: WebRTCPeer.cs
//
// Description: A console application to load test the WebRTC data channel
// send message API.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 06 Aug 2020	Aaron Clauson	Created based on example from @Terricide.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Demo
{
    public class WebRTCPeer
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private RTCPeerConnection _peerConnection = null;
        private const int MDNS_TIMEOUT = 2000;
        private string _dataChannelLabel;
        public event Action<RTCSessionDescriptionInit> OnOffer;
        public event Action<RTCSessionDescriptionInit> OnAnswer;
        public event Action<RTCIceCandidateInit> OnIce;
        private TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        public string Name;

        private Dictionary<string, RTCDataChannel> _dataChannels = new Dictionary<string, RTCDataChannel>();

        public WebRTCPeer(string dataChannelLabel)
        {
            _dataChannelLabel = dataChannelLabel;
        }

        public async Task<bool> Connect()
        {
            Createpc(null);

            var offerSdp = _peerConnection.createOffer(null);
            await _peerConnection.setLocalDescription(offerSdp);
            OnOffer.Invoke(offerSdp);
            return await tcs.Task;
        }

        private void Createpc(RTCIceServer stunServer)
        {
            if (_peerConnection != null)
            {
                _peerConnection.Close("normal");
            }

            List<RTCCertificate> presetCertificates = null;
            if (File.Exists(LOCALHOST_CERTIFICATE_PATH))
            {
                var localhostCert = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH, (string)null, X509KeyStorageFlags.Exportable);
                presetCertificates = new List<RTCCertificate> { new RTCCertificate { Certificate = localhostCert } };
            }

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = presetCertificates,
                X_RemoteSignallingAddress = null,
                iceServers = stunServer != null ? new List<RTCIceServer> { stunServer } : null,
                iceTransportPolicy = RTCIceTransportPolicy.all,
                //X_BindAddress = IPAddress.Any, // NOTE: Not reqd. Using this to filter out IPv6 addresses so can test with Pion.
            };

            _peerConnection = new RTCPeerConnection(pcConfiguration);

            //_peerConnection.GetRtpChannel().MdnsResolve = (hostname) => Task.FromResult(NetServices.InternetDefaultAddress);
            _peerConnection.GetRtpChannel().MdnsResolve = MdnsResolve;
            //_peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"STUN message received from {ep}, message class {msg.Header.MessageClass}.");

            var dataChannel = _peerConnection.createDataChannel(_dataChannelLabel, null);
            dataChannel.onDatamessage += DataChannel_onDatamessage;
            _dataChannels.Add(_dataChannelLabel, dataChannel);

            _peerConnection.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            _peerConnection.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnection.Close("remote disconnection");
                }
            };
            _peerConnection.OnReceiveReport += (ep, type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            _peerConnection.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            _peerConnection.onicecandidate += (candidate) =>
            {
                if (_peerConnection.signalingState == RTCSignalingState.have_local_offer ||
                    _peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                {
                    OnIce?.Invoke(new RTCIceCandidateInit()
                    {
                        candidate = candidate.ToString(),
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = candidate.sdpMLineIndex
                    });
                }
            };

            // Peer ICE connection state changes are for ICE events such as the STUN checks completing.
            _peerConnection.oniceconnectionstatechange += (state) =>
            {
                logger.LogDebug($"ICE connection state change to {state}.");
            };

            _peerConnection.ondatachannel += (dc) =>
            {
                dc.onopen += DataChannel_onopen;
                dc.onDatamessage += DataChannel_onDatamessage;
                logger.LogDebug($"Data channel opened by remote peer, label {dc.label}, stream ID {dc.id}.");
                _dataChannels.Add(dc.label, dc);
            };
        }

        private void DataChannel_onopen()
        {
            tcs.TrySetResult(true);
        }

        private void DataChannel_onDatamessage(byte[] obj)
        {
            var pieceNum = BitConverter.ToInt32(obj, 0);
            //logger.LogDebug($"{Name}: data channel ({_dataChannel.label}:{_dataChannel.id}): {pieceNum}.");
            logger.LogDebug($"Data channel receive: {pieceNum}.");
        }

        public void ProcessIce(RTCIceCandidateInit ice)
        {
            _peerConnection.addIceCandidate(ice);
        }

        public bool IsDataChannelReady(string label)
        {
            if (_dataChannels.ContainsKey(label))
            {
                return _dataChannels[label].IsOpened;
            }

            return false;
        }

        public void Send(string label, byte[] data)
        {
            if (_dataChannels.ContainsKey(label))
            {
                var dc = _dataChannels[label];
                if (dc.IsOpened)
                {
                    _dataChannels[label].send(data);
                }
                else
                {
                    logger.LogWarning($"Data channel {label} not yet open.");
                }
            }
        }

        public async Task ProcessAnswer(RTCSessionDescriptionInit answerInit)
        {
            bool isNew = false;
            if (_peerConnection == null)
            {
                Createpc(null);
                isNew = true;
            }

            var res = _peerConnection.setRemoteDescription(answerInit);
            if (res != SetDescriptionResultEnum.OK)
            {
                // No point continuing. Something will need to change and then try again.
                _peerConnection.Close("failed to set remote sdp");
            }

            if (isNew)
            {
                var answerSdp = _peerConnection.createAnswer(null);
                await _peerConnection.setLocalDescription(answerSdp);
                OnAnswer.Invoke(answerSdp);
            }
        }

        public Task AwaitConnection()
        {
            return tcs.Task;
        }

        private static async Task<IPAddress> MdnsResolve(string service)
        {
            logger.LogDebug($"MDNS resolve requested for {service}.");

            var query = new Message();
            query.Questions.Add(new Question { Name = service, Type = DnsType.ANY });
            var cancellation = new CancellationTokenSource(MDNS_TIMEOUT);

            using (var mdns = new MulticastService())
            {
                mdns.Start();
                var response = await mdns.ResolveAsync(query, cancellation.Token);

                var ans = response.Answers.Where(x => x.Type == DnsType.A || x.Type == DnsType.AAAA).FirstOrDefault();

                logger.LogDebug($"MDNS result {ans}.");

                switch (ans)
                {
                    case ARecord a:
                        return a.Address;
                    case AAAARecord aaaa:
                        return aaaa.Address;
                    default:
                        return null;
                };
            }
        }
    }
}
