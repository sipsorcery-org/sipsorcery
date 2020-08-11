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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
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
        public RTCPeerConnection PeerConnection { get; private set; }
        private const int MDNS_TIMEOUT = 2000;
        private string _dataChannelLabel;
        public event Action<RTCIceCandidateInit> OnIceCandidateAvailable;
        public string _peerName;
        private Action<WebRTCPeer, byte[]> _onData;
        public Action<WebRTCPeer, byte[]> OnData
        {
            get
            {
                return _onData;
            }
            set
            {
                if (_onData != value)
                {
                    _onData = value;
                }
            }
        }

        private Dictionary<string, RTCDataChannel> _dataChannels = new Dictionary<string, RTCDataChannel>();

        public WebRTCPeer(string peerName, string dataChannelLabel)
        {
            _peerName = peerName;
            _dataChannelLabel = dataChannelLabel;

            PeerConnection = Createpc();
        }

        private RTCPeerConnection Createpc()
        {           
            List<RTCCertificate> presetCertificates = null;
            if (File.Exists(LOCALHOST_CERTIFICATE_PATH))
            {
                var localhostCert = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH, (string)null, X509KeyStorageFlags.Exportable);
                presetCertificates = new List<RTCCertificate> { new RTCCertificate { Certificate = localhostCert } };
            }

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = presetCertificates,
            };

            var pc = new RTCPeerConnection(pcConfiguration);

            pc.GetRtpChannel().MdnsResolve = MdnsResolve;
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"{_peerName}: STUN message received from {ep}, message class {msg.Header.MessageClass}.");

            var dataChannel = pc.createDataChannel(_dataChannelLabel, null);
            dataChannel.onDatamessage += DataChannel_onDatamessage;
            _dataChannels.Add(_dataChannelLabel, dataChannel);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"{_peerName}: Error adding remote ICE candidate. {error} {candidate}");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"{_peerName}: ICE connection state change to {state}.");
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"{_peerName}: Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    pc.Close("remote disconnection");
                }
            };

            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    OnIceCandidateAvailable?.Invoke(new RTCIceCandidateInit()
                    {
                        candidate = candidate.ToString(),
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = candidate.sdpMLineIndex
                    });
                }
            };

            pc.ondatachannel += (dc) =>
            {
                dc.onopen += () => logger.LogDebug($"{_peerName}: Data channel now open label {dc.label}, stream ID {dc.id}.");
                dc.onDatamessage += DataChannel_onDatamessage;
                logger.LogDebug($"{_peerName}: Data channel created by remote peer, label {dc.label}, stream ID {dc.id}.");
                _dataChannels.Add(dc.label, dc);
            };

            return pc;
        }

        private void DataChannel_onDatamessage(byte[] obj)
        {
            OnData?.Invoke(this, obj);
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
                    logger.LogWarning($"{_peerName}: Data channel {label} not yet open.");
                }
            }
        }

        public async Task SendAsync(string label, byte[] data)
        {
            if (_dataChannels.ContainsKey(label))
            {
                var dc = _dataChannels[label];
                if (dc.IsOpened)
                {
                    await _dataChannels[label].sendasync(data).ConfigureAwait(false);
                }
                else
                {
                    logger.LogWarning($"{_peerName}: Data channel {label} not yet open.");
                }
            }
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
