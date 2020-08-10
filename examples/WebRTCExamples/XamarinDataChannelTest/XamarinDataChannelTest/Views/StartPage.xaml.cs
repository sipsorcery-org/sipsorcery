using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
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
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        public StartPage()
        {
            InitializeComponent();
        }

        async void OnStartButtonClicked(object sender, EventArgs args)
        {
            (sender as Button).IsEnabled = false;
            await RunDataChannelTest();
            (sender as Button).IsEnabled = true;
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
    }
}