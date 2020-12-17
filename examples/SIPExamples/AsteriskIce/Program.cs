//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A demo program to place a SIP call to Asterisk and use an RTP 
// channel that supports ICE.
//
// The key point for this demo is the RTCPeerConnection instance passed to
// the SIPUserAgent to manage the RTP channels and media. Typically a 
// VoIPMediaSession or RTPSession is used for SIP. In this demo because an
// ICE connection is desired on the RTP channel the RTCPeerConnection, which
// is more commonly used for WebRTC, is employed.
//
// Note: As of 18 Nov 2020 this demo program is able to establish an RTP
// ICE connected channel with Asterisk 18.0.1 BUT the audio and video 
// streams do not get through to the recipient on the other end of the 
// Asterisk bridge. 
//
// The relevant sections from /etc/asterisk/pjsip.conf:
//
//[joeb]
//type = aor
//max_contacts = 5
//remove_existing = yes
//
//[joeb]
//type = auth
//auth_type = userpass
//username = joeb
//password = password
//
//[joeb]
//type = endpoint
//aors = joeb
//auth = joeb
//dtls_auto_generate_cert = yes
//webrtc = yes
//context = default
//allow = all
//direct_media = no
//ice_support = yes
//
// For /etc/astersik/rtp.conf:
//icesupport=true 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Encoders;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "sip:7001@192.168.0.48";
        private static string USERNAME = "joeb";
        private static string PASSWORD = "password";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Asterisk + ICE Demo");

            AddConsoleLogger();
            CancellationTokenSource exitCts = new CancellationTokenSource();

            var sipTransport = new SIPTransport();

            EnableTraceLogs(sipTransport);

            var userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.ClientCallFailed += (uac, error, sipResponse) => Console.WriteLine($"Call failed {error}.");
            userAgent.OnCallHungup += async (dialog) =>
            {
                // Give time for the BYE response before exiting.
                await Task.Delay(1000);
                exitCts.Cancel();
            };

            var audioExtras = new AudioExtrasSource();
            audioExtras.SetSource(AudioSourcesEnum.PinkNoise);
            var testPattern = new VideoTestPatternSource(new VpxVideoEncoder());

            var pc = new RTCPeerConnection(null);
            pc.OnAudioFormatsNegotiated += (formats) => audioExtras.SetAudioSourceFormat(formats.First());
            pc.OnVideoFormatsNegotiated += (formats) => testPattern.SetVideoSourceFormat(formats.First());

            var audioTrack = new MediaStreamTrack(audioExtras.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);
            audioExtras.OnAudioSourceEncodedSample += pc.SendAudio;

            var videoTrack = new MediaStreamTrack(testPattern.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);
            testPattern.OnVideoSourceEncodedSample += pc.SendVideo;

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) => Console.WriteLine($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => Console.WriteLine($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");

            // ICE connection state handler.
            pc.onconnectionstatechange += (state) =>
            {
                Console.WriteLine($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    audioExtras.StartAudio();
                    testPattern.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    if (userAgent.IsCallActive)
                    {
                        Console.WriteLine("ICE connection failed, hanging up active call.");
                        userAgent.Hangup();
                    }
                }
            };

            // Place the call and wait for the result.
            var callTask = userAgent.Call(DESTINATION, USERNAME, PASSWORD, pc);

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                if (userAgent != null)
                {
                    if (userAgent.IsCalling || userAgent.IsRinging)
                    {
                        Console.WriteLine("Cancelling in progress call.");
                        userAgent.Cancel();
                    }
                    else if (userAgent.IsCallActive)
                    {
                        Console.WriteLine("Hanging up established call.");
                        userAgent.Hangup();
                    }
                };

                exitCts.Cancel();
            };

            Console.WriteLine("press ctrl-c to exit...");

            bool callResult = await callTask;

            if (callResult)
            {
                Console.WriteLine($"Call to {DESTINATION} succeeded.");
                exitCts.Token.WaitHandle.WaitOne();
            }
            else
            {
                Console.WriteLine($"Call to {DESTINATION} failed.");
            }

            Console.WriteLine("Exiting...");

            if (userAgent?.IsHangingUp == true)
            {
                Console.WriteLine("Waiting 1s for the call hangup or cancel to complete...");
                await Task.Delay(1000);
            }

            // Clean up.
            sipTransport.Shutdown();
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request received: {localEP}<-{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request sent: {localEP}->{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response received: {localEP}<-{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response sent: {localEP}->{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Console.WriteLine($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Console.WriteLine($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
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
