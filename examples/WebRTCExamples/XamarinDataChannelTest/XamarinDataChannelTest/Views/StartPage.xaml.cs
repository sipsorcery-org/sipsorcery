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
        public const string DATA_CHANNEL_LABEL = "xdc";

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
                        _peer = new WebRTCPeer("peer1", DATA_CHANNEL_LABEL);
                       
                        _peer.PeerConnection.oniceconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"ICE connection state {state}.");
                        _peer.PeerConnection.onconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"Peer connection state {state}.");
                        _peer.OnDataChannelMessage += (msg_) => Device.BeginInvokeOnMainThread(() => this._dataChannelMessages.Text += $"\n{msg_}");

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

        async void OnSendMessageButtonClicked(object sender, EventArgs args)
        {
            await _peer.Send(DATA_CHANNEL_LABEL, _sendMessage.Text);
        }
    }
}