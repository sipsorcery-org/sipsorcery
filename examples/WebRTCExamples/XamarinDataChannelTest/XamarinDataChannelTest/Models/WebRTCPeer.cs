using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using System.Threading;

namespace XamarinDataChannelTest.Models
{
    public class WebRTCPeer
    {
        private const string DUMMY_CERTIFICATE_BASE64 =
"MIIJagIBAzCCCSYGCSqGSIb3DQEHAaCCCRcEggkTMIIJDzCCBZAGCSqGSIb3DQEHAaCCBYEEggV9MIIFeTCCBXUGCyqGSIb3DQE" +
"MCgECoIIE7jCCBOowHAYKKoZIhvcNAQwBAzAOBAjSBlPXK93x7wICB9AEggTIbyXDPx/6WkNQv9hXoQIwn3F9pvrmg8+SyBbwTr" +
"Fpr50GJQQqA5qpihvQ38s5fWv3rv8WpVttAAvCtSE6Q7o8HZmMNYdA7dU/8SAx/Cy41qWjUytMLEL07lH/d24l8ek9nZpY+u08R" +
"5h5tlTcjfQ2Ta9jJHb2VSHa40dn5ufv4QicbcjLnSa6T9i+wpiUsf8V4A8SD0zdU/Vs9VvYlrFS+5eYkPHNHVX6fjl4l/Xk5XV+" +
"m4Nry0IwAepdi5KENf5+ajamSEIDqJXwR0wXfZ/eJp12Eyviq3kG880jzVeQ+eXTXLPtMGIbPW1qOS1owFWd5XYkn0QqyIcC73V" +
"jxhr2UzCM2Y86tbIALexO2kZdLHqhqD3ZnMVoHSOY2fG9U+Icbx0077zoWEhPjxQ8Trurg1gochqb2MME2ksuk7MF6n9Hw/Asp/" +
"iig90wZZfFXuP3fWmZjbqDDxxVHxo2mxQWiV/LhmV2BuFgyA++Fhx8TbIssDrUHsaIl8pp0iTl2c+D/n48WTDeuiSJ/S04B8G1D" +
"bsQmyT6Ym/w5P/D4xfg7JWNW/ZFBDPTmAh4AD7eHxIfLmUMnyGQgDDiLf4dy/fYHhJ1ukIrPdhb5dAuDQwOdLPMA34VksJiDL67" +
"/0hjHxJOcptUbKmFtvOYC+bBF9Womg+K9V1USF8QN4yeP3n8BBrUDNq078YXEObvITMRGPEd6FLyS+ImQ9rNxLH/i23j4ap46Cd" +
"7kWLfTKODH8931BzhodpSzuH6QEZPGL98xoefBxAEqUZgx4SGDzSH7Trxpl/xoKkT0TTZlPHE1H2Q6Y5RlH9T3hRxqHEvP5yILO" +
"NCZ1S4YbnfG4wY07z88Ok7m9ZUwLh+Xd5Tgdq+rEP8p84arHh1TyOivUJfLmXA6CMHqQ2WMzQmxQm67w34GCL5psEumVcSfbvoD" +
"8D6xQjEQYp/DzKTKcjjMA8BKWQh0PnYVxR18hzkNONyBVHvik74flAbVa8W/9/NoURZP2lGUO0SCpHHbH+je0VRBH6IuiGhqRZ4" +
"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";

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<WebRTCPeer>();
        public RTCPeerConnection PeerConnection { get; private set; }
        private string _dataChannelLabel;
        public event Action<RTCIceCandidateInit> OnIceCandidateAvailable;
        public string _peerName;
        private Uri _webSocketServerUri;

        private Dictionary<string, RTCDataChannel> _dataChannels = new Dictionary<string, RTCDataChannel>();

        public event Action<string> OnDataChannelMessage;

        static WebRTCPeer()
        {
            // DNS lookups on Xamarin don't seem to work. The reason to add the hard coded
            // DNS servers is to prevent he exception when the DnsClient attempts to find
            // /etc/resolv.conf.
            RtpIceChannel.DefaultNameServers = new List<DnsClient.NameServer> {
                DnsClient.NameServer.GooglePublicDnsIPv6,
                DnsClient.NameServer.GooglePublicDns2IPv6,
                DnsClient.NameServer.GooglePublicDns,
                DnsClient.NameServer.GooglePublicDns2 };
        }

        public WebRTCPeer(string peerName, string dataChannelLabel, Uri webSocketServerUri)
        {
            _peerName = peerName;
            _dataChannelLabel = dataChannelLabel;
            _webSocketServerUri = webSocketServerUri;
        }

        public Task Connect(CancellationToken ct)
        {
            WebRTCWebSocketClient wsClient = new WebRTCWebSocketClient(_webSocketServerUri.ToString(), CreatePeerConnection);
            return wsClient.Start(ct);
        }

        public Task<RTCPeerConnection> CreatePeerConnection()
        {
            List<RTCCertificate> presetCertificates = null;
            byte[] dummyCertBytes = Convert.FromBase64String(DUMMY_CERTIFICATE_BASE64);
            var dummyCert = new X509Certificate2(dummyCertBytes);
            presetCertificates = new List<RTCCertificate> { new RTCCertificate { Certificate = dummyCert } };

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = presetCertificates,
                //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = "stun:stun.l.google.com:19302" } }
                X_BindAddress = IPAddress.Any
            };

            var pc = new RTCPeerConnection(pcConfiguration);
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"{_peerName}: STUN message received from {ep}, message class {msg.Header.MessageClass}.");

            var dataChannel = pc.createDataChannel(_dataChannelLabel, null);
            dataChannel.onDatamessage += DataChannel_onDatamessage;
            dataChannel.onmessage += DataChannel_onmessage;
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
                dc.onmessage += DataChannel_onmessage;
                logger.LogDebug($"{_peerName}: Data channel created by remote peer, label {dc.label}, stream ID {dc.id}.");
                _dataChannels.Add(dc.label, dc);
            };

            PeerConnection = pc;

            return Task.FromResult(pc);
        }

        private void DataChannel_onmessage(string message)
        {
            OnDataChannelMessage?.Invoke(message);
        }

        private void DataChannel_onDatamessage(byte[] obj)
        {
            var pieceNum = BitConverter.ToInt32(obj, 0);
            //logger.LogDebug($"{Name}: data channel ({_dataChannel.label}:{_dataChannel.id}): {pieceNum}.");
            logger.LogDebug($"{_peerName}: Data channel receive: {pieceNum}, length {obj.Length}.");
        }

        public bool IsDataChannelReady(string label)
        {
            if (_dataChannels.ContainsKey(label))
            {
                return _dataChannels[label].IsOpened;
            }

            return false;
        }

        public async Task Send(string label, string message)
        {
            if (_dataChannels.ContainsKey(label))
            {
                var dc = _dataChannels[label];
                if (dc.IsOpened)
                {
                    logger.LogDebug($"Sending data channel message on channel {label}.");
                    await _dataChannels[label].sendasync(message);
                }
                else
                {
                    logger.LogWarning($"{_peerName}: Data channel {label} not yet open.");
                }
            }
        }
    }
}
