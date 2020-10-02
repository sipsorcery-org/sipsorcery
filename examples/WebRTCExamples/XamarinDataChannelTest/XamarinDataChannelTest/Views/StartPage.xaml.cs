using System;
using System.ComponentModel;
using System.Net.WebSockets;
using System.Threading;
using Xamarin.Forms;
using XamarinDataChannelTest.Models;
using SIPSorcery.Net;
using Microsoft.Extensions.Logging;

namespace XamarinDataChannelTest.Views
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(false)]
    public partial class StartPage : ContentPage
    {
        public const string DATA_CHANNEL_LABEL = "xdc";

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<StartPage>();
        private WebRTCPeer _peer;
        private bool _isWebRTCConnected;

        public StartPage()
        {
            InitializeComponent();
        }

        void OnCloseButtonClicked(object sender, EventArgs args)
        {
            _peer?.PeerConnection.Close("user initiated");

            this._closeButton.IsVisible = false;
            this._connectButton.IsVisible = true;
            _isWebRTCConnected = false;

            this._status.Text = "Ready";
        }

        async void OnConnectButtonClicked(object sender, EventArgs args)
        {
            logger.LogDebug($"Attempting to connection to web socket at {this._webSocketURL.Text}.");

            if (!Uri.TryCreate(this._webSocketURL.Text, UriKind.Absolute, out var uri))
            {
                this._status.Text = "Invalid web socket URI.";
            }
            else
            {
                this._connectButton.IsVisible = false;
                this._closeButton.IsVisible = true;

                this._status.Text = "Attempting to connect to web socket server.";

                try
                {
                    _peer = new WebRTCPeer("peer1", DATA_CHANNEL_LABEL, uri);
                    await _peer.Connect(CancellationToken.None);

                    if (_peer.PeerConnection.connectionState == RTCPeerConnectionState.connected)
                    {
                        _isWebRTCConnected = true;
                        this._status.Text = "WebRTC peer connection successfully established.";
                        _peer.PeerConnection.onconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"Peer connection state {state}.");
                    }
                    else
                    {
                        this._status.Text = "Web socket connection successful, WebRTC peer connecting...";

                        _peer.PeerConnection.onconnectionstatechange += (connState) =>
                        {
                            if (_peer.PeerConnection.connectionState == RTCPeerConnectionState.connected)
                            {
                                if (!_isWebRTCConnected)
                                {
                                    _isWebRTCConnected = true;

                                    this.Dispatcher.BeginInvokeOnMainThread(() => this._status.Text = "WebRTC peer connection successfully established.");

                                    _peer.PeerConnection.onconnectionstatechange += (state) => Device.BeginInvokeOnMainThread(() => this._status.Text = $"Peer connection state {state}.");
                                    _peer.OnDataChannelMessage += (msg_) => Device.BeginInvokeOnMainThread(() => this._dataChannelMessages.Text += $"\n{msg_}");
                                }
                            }
                            else if (_peer.PeerConnection.connectionState == RTCPeerConnectionState.failed ||
                                _peer.PeerConnection.connectionState == RTCPeerConnectionState.closed)
                            {
                                this.Dispatcher.BeginInvokeOnMainThread(() => this._status.Text = $"WebRTC peer connection attempt failed in state {_peer.PeerConnection.connectionState}.");
                            }
                        };
                    }
                }
                catch (Exception excp)
                {
                    this._status.Text = $"Error connecting. {excp.Message}";

                    this._closeButton.IsVisible = false;
                    this._connectButton.IsVisible = true;
                }
            }
        }

        async void OnSendMessageButtonClicked(object sender, EventArgs args)
        {
            await _peer.Send(DATA_CHANNEL_LABEL, _sendMessage.Text);
        }
    }
}