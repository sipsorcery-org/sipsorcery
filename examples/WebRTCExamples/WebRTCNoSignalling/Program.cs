//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: This example is the same as the WebRTCTestPatternServer example
// except that it can be used without requiring a web socket signalling channel.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.Windows;

namespace WebRTCServer
{
    class Program
    {
        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("WebRTC No Signalling Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            var pc = CreatePeerConnection();

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP OFFER BELOW NEEDS TO BE PASTED INTO YOUR BROWSER");
            Console.WriteLine();

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            var offerSerialised = Newtonsoft.Json.JsonConvert.SerializeObject(offerSdp,
                 new Newtonsoft.Json.Converters.StringEnumConverter());
            var offerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(offerSerialised));

            Console.WriteLine(offerBase64);

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP ANSWER FROM THE BROWSER NEEDS TO PASTED BELOW");
            Console.WriteLine();

            string remoteAnswerB64 = null;
            while (string.IsNullOrWhiteSpace(remoteAnswerB64))
            {
                Console.Write("=> ");
                remoteAnswerB64 = Console.ReadLine();
            }

            if (remoteAnswerB64 == "q")
            {
                Console.WriteLine("Quitting.");
            }
            else
            {
                string remoteAnswer = Encoding.UTF8.GetString(Convert.FromBase64String(remoteAnswerB64));
                Console.WriteLine($"Remote answer: {remoteAnswer}");

                RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteAnswer);
                pc.setRemoteDescription(answerInit);

                // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
                exitMre.WaitOne();

                Console.WriteLine("Closing.");
                pc.Close("normal");

                Task.Delay(1000).Wait();
            }
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            var peerConnection = new RTCPeerConnection(null);

            var testPatternSource = new VideoTestPatternSource();
            WindowsVideoEndPoint windowsVideoEndPoint = new WindowsVideoEndPoint(new VpxVideoEncoder());

            MediaStreamTrack track = new MediaStreamTrack(windowsVideoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            peerConnection.addTrack(track);

            testPatternSource.OnVideoSourceRawSample += windowsVideoEndPoint.ExternalVideoSourceRawSample;
            windowsVideoEndPoint.OnVideoSourceEncodedSample += peerConnection.SendVideo;

            peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change {state}.");
            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.OnTimeout += (mediaType) => logger.LogWarning($"Timeout on {mediaType}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.closed)
                {
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;

                    await windowsVideoEndPoint.CloseVideo();
                    await testPatternSource.CloseVideo();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await testPatternSource.StartVideo();
                    await windowsVideoEndPoint.StartVideo();
                }
            };

            return peerConnection;
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
