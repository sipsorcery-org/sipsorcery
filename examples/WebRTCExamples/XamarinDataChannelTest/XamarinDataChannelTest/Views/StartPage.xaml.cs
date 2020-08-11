using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using XamarinDataChannelTest.Models;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;
using Xamarin.Forms.PlatformConfiguration;

namespace XamarinDataChannelTest.Views
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(false)]
    public partial class StartPage : ContentPage
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;
        private WebRTCPeer _peer;

        public StartPage()
        {
            InitializeComponent();
        }

        void OnCloseButtonClicked(object sender, EventArgs args)
        {
            _peer?.PeerConnection.Close("user initiated");

            this._closeButton.IsVisible = false;
            this._connectButton.IsVisible = true;

            this._status.Text = "Ready";
        }

        async void OnConnectButtonClicked(object sender, EventArgs args)
        {
            logger.LogDebug($"Attempting to connection to web socket at {this._webSocketURL.Text}.");

            var clientWebSocket = new ClientWebSocket();

            if (!Uri.TryCreate(this._webSocketURL.Text, UriKind.Absolute, out var uri))
            {
                this._status.Text = "Invalid web socket URI.";
            }
            else
            {
                this._connectButton.IsVisible = false;
                this._closeButton.IsVisible = true;

                this._status.Text = "Attempting to connect to web socket server.";

                await clientWebSocket.ConnectAsync(uri, CancellationToken.None);

                var buffer = WebSocket.CreateClientBuffer(8192, 8192);
                int attempts = 0;

                while (true && attempts < 10)
                {
                    WebSocketReceiveResult response = await clientWebSocket.ReceiveAsync(buffer, CancellationToken.None);

                    if (response.EndOfMessage)
                    {
                        _peer = new WebRTCPeer("peer1", "dc1");
                       
                        _peer.PeerConnection.oniceconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"ICE connection state {state}.");
                        _peer.PeerConnection.onconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"Peer connection state {state}.");

                        var options = new JsonSerializerOptions();
                        options.Converters.Add(new JsonStringEnumConverter());
                        var init = JsonSerializer.Deserialize<RTCSessionDescriptionInit>(buffer.Take(response.Count).ToArray(), options);
                        _peer.PeerConnection.setRemoteDescription(init);

                        var answer = _peer.PeerConnection.createAnswer(null);
                        await _peer.PeerConnection.setLocalDescription(answer);

                        var answerJson = JsonSerializer.Serialize<RTCSessionDescriptionInit>(answer, options);
                        await clientWebSocket.SendAsync(
                            new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(answerJson)),
                            WebSocketMessageType.Text, true, CancellationToken.None);

                        attempts = 10;
                        while (_peer.PeerConnection.connectionState == RTCPeerConnectionState.connecting && attempts < 10)
                        {
                            await Task.Delay(1000);
                            attempts++;
                        }

                        break;
                    }
                    else
                    {
                        logger.LogWarning("Failed to get full web socket message from server.");

                        this._status.Text = "Web socket message exchange failed.";
                    }

                    attempts++;
                }
            }
        }

        async Task RunUdpSendReceiveTest()
        {
            IPEndPoint listenEP = new IPEndPoint(IPAddress.IPv6Any, 13333);

            var sock1 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            sock1.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            sock1.DualMode = true;
            sock1.Bind(listenEP);

            UdpReceiver receiver1 = new UdpReceiver(sock1);

            receiver1.OnPacketReceived += (recv, port, rep, pkt) => logger.LogDebug($"UdpReceiver data received on {port} from {rep} data {Encoding.ASCII.GetString(pkt)}.");
            receiver1.BeginReceiveFrom();

            var sock2 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            sock2.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            sock2.DualMode = true;
            //var sock2 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            //sock2.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);

            //IPEndPoint sendToEP1 = new IPEndPoint(IPAddress.Loopback, listenEP.Port);
            //IPEndPoint sendToEP2 = new IPEndPoint(IPAddress.IPv6Loopback, listenEP.Port);

            IPEndPoint sendToEP1 = new IPEndPoint(IPAddress.Parse("fe80::15:b2ff:fe00:0%3"), listenEP.Port);
            IPEndPoint sendToEP2 = new IPEndPoint(IPAddress.Parse("192.168.232.2"), listenEP.Port);

            _ = Task.Run(async () =>
            {
                var buffer = Encoding.ASCII.GetBytes("sendsendsend");
                IPEndPoint dstEndPoint4 = new IPEndPoint(IPAddress.Parse("192.168.232.2"), listenEP.Port + 1);
                IPEndPoint dstEndPoint6 = new IPEndPoint(IPAddress.Parse("fe80::15:b2ff:fe00:0%3"), listenEP.Port + 1);

                for (int i = 0; i < 1000; i++)
                {
                    IPEndPoint dstEndPoint = dstEndPoint4;

                    if (i % 2 == 0)
                    {
                        dstEndPoint = dstEndPoint6;
                    }

                    logger.LogDebug($"Attempting socket send to from s1 to {dstEndPoint}.");

                    sock1.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, dstEndPoint, EndSendTo, sock1);

                    await Task.Delay(10);
                }
            });

            logger.LogDebug($"Attempting socket send to {sendToEP1}.");
            sock2.SendTo(Encoding.ASCII.GetBytes("hello1"), sendToEP1);
            await Task.Delay(1000);

            logger.LogDebug($"Attempting socket send to {sendToEP2}.");
            sock2.SendTo(Encoding.ASCII.GetBytes("hello2"), sendToEP2);
            await Task.Delay(1000);

            logger.LogDebug($"Attempting socket send to {sendToEP1}.");
            sock2.SendTo(Encoding.ASCII.GetBytes("hello3"), sendToEP1);
            await Task.Delay(1000);

            logger.LogDebug("Test complete.");
        }

        async Task RunDataChannelTest()
        {
            var peerA = new WebRTCPeer("PeerA", "dcx");
            var peerB = new WebRTCPeer("PeerB", "dcy");

            // Exchange the SDP offer/answers. ICE Host candidates are included in the SDP.
            var offer = peerA.PeerConnection.createOffer(null);
            await peerA.PeerConnection.setLocalDescription(offer);

            if (peerB.PeerConnection.setRemoteDescription(offer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException("Couldn't set remote description.");
            }
            var answer = peerB.PeerConnection.createAnswer(null);
            await peerB.PeerConnection.setLocalDescription(answer);

            if (peerA.PeerConnection.setRemoteDescription(answer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException("Couldn't set remote description.");
            }

            // Wait for the peers to connect. Should take <1s if the peers are on the same host.
            while (peerA.PeerConnection.connectionState != RTCPeerConnectionState.connected &&
                peerB.PeerConnection.connectionState != RTCPeerConnectionState.connected)
            {
                logger.LogDebug("Waiting for WebRTC peers to connect...");
                await Task.Delay(1000);
            }

            var taskList = new List<Task>();

            taskList.Add(Task.Run(async () =>
            {
                string sendLabel = "dcx";

                while (!peerA.IsDataChannelReady(sendLabel))
                {
                    Console.WriteLine($"Waiting 1s for data channel {sendLabel} to open.");
                    await Task.Delay(1000);
                }

                try
                {
                    logger.LogDebug($"Data channel send on {sendLabel}.");

                    var data = new byte[4];
                    var num = BitConverter.GetBytes(1);
                    Buffer.BlockCopy(num, 0, data, 0, num.Length);
                    peerA.Send(sendLabel, data);
                    //await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    logger.LogError("ClientA:" + ex.ToString());
                }
            }));
        }

        private void EndSendTo(IAsyncResult ar)
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
                logger.LogWarning($"SocketException RTPChannel EndSendTo ({sockExcp.ErrorCode}). {sockExcp.Message}");
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception RTPChannel EndSendTo. {excp.Message}");
            }
        }
    }
}