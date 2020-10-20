//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// act as the server for a SIP call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 26 Feb 2020  Aaron Clauson   Switched RTP to use RtpAVSession.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// This example can be used with the automated SIP test tool [SIPp] (https://github.com/SIPp/sipp)
// and its inbuilt User Agent Client scenario.
// Note: SIPp doesn't support IPv6.
//
// To install on WSL:
// $ sudo apt install sip-tester
//
// Running tests (press the '+' key while test is running to increase the call rate):
// For UDP testing: sipp -sn uac 127.0.0.1
// For TCP testing: sipp -sn uac localhost -t t1
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Media files:
// The "Simplicity" audio used in this example is from an artist called MACROFORM
// and can be downloaded directly from: https://www.jamendo.com/track/579315/simplicity?language=en
// The use of the audio is licensed under the Creative Commons 
// https://creativecommons.org/licenses/by-nd/2.0/
// The audio is free for personal use but a license may be required for commercial use.
// If it sounds familiar this particular file is also included as part of Asterisk's 
// (asterisk.org) music on hold.
//
// ffmpeg can be used to convert the mp3 file into the required format for placing directly 
// into the RTP packets. Currently this example supports two audio formats: G711.ULAW (or PCMU)
// and G722.
//
// ffmpeg -i Macroform_-_Simplicity.mp3 -ac 1 -ar 8k -ab 64k -f mulaw Macroform_-_Simplicity.ulaw
// ffmpeg -i Macroform_-_Simplicity.mp3 -ar 16k -acodec g722 Macroform_-_Simplicity.g722
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;
        private static int SIPS_LISTEN_PORT = 5061;
        //private static int SIP_WEBSOCKET_LISTEN_PORT = 80;
        //private static int SIP_SECURE_WEBSOCKET_LISTEN_PORT = 443;
        private static string SIPS_CERTIFICATE_PATH = "localhost.pfx";

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery user agent server example.");
            Console.WriteLine("Press h to hangup a call or ctrl-c to exit.");

            Log = AddConsoleLogger();

            IPAddress listenAddress = IPAddress.Any;
            IPAddress listenIPv6Address = IPAddress.IPv6Any;
            if (args != null && args.Length > 0)
            {
                if (!IPAddress.TryParse(args[0], out var customListenAddress))
                {
                    Log.LogWarning($"Command line argument could not be parsed as an IP address \"{args[0]}\"");
                    listenAddress = IPAddress.Any;
                }
                else
                {
                    if (customListenAddress.AddressFamily == AddressFamily.InterNetwork)
                    {
                        listenAddress = customListenAddress;
                    }
                    if (customListenAddress.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        listenIPv6Address = customListenAddress;
                    }
                }
            }

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();

            var localhostCertificate = new X509Certificate2(SIPS_CERTIFICATE_PATH);

            // IPv4 channels.
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(listenAddress, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(listenAddress, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTLSChannel(localhostCertificate, new IPEndPoint(listenAddress, SIPS_LISTEN_PORT)));
            //sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.Any, SIP_WEBSOCKET_LISTEN_PORT));
            //sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.Any, SIP_SECURE_WEBSOCKET_LISTEN_PORT, localhostCertificate));

            // IPv6 channels.
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(listenIPv6Address, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(listenIPv6Address, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTLSChannel(localhostCertificate, new IPEndPoint(listenIPv6Address, SIPS_LISTEN_PORT)));
            //sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.IPv6Any, SIP_WEBSOCKET_LISTEN_PORT));
            //sipTransport.AddSIPChannel(new SIPWebSocketChannel(IPAddress.IPv6Any, SIP_SECURE_WEBSOCKET_LISTEN_PORT, localhostCertificate));

            EnableTraceLogs(sipTransport);

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            // To keep things a bit simpler this example only supports a single call at a time and the SIP server user agent
            // acts as a singleton
            SIPServerUserAgent uas = null;
            CancellationTokenSource rtpCts = null; // Cancellation token to stop the RTP stream.
            VoIPMediaSession rtpSession = null;

            // Because this is a server user agent the SIP transport must start listening for client user agents.
            sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                try
                {
                    if (sipRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        Log.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                        // Check there's a codec we support in the INVITE offer.
                        var offerSdp = SDP.ParseSDPDescription(sipRequest.Body);
                        IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);

                        if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaFormats.Any(x => x.Key == (int)SDPWellKnownMediaFormatsEnum.PCMU)))
                        {
                            Log.LogDebug($"Client offer contained PCMU audio codec.");
                            AudioExtrasSource extrasSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
                            rtpSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = extrasSource });
                            rtpSession.AcceptRtpFromAny = true;

                            var setResult = rtpSession.SetRemoteDescription(SdpType.offer, offerSdp);

                            if (setResult != SetDescriptionResultEnum.OK)
                            {
                                // Didn't get a match on the codecs we support.
                                SIPResponse noMatchingCodecResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptableHere, setResult.ToString());
                                await sipTransport.SendResponseAsync(noMatchingCodecResponse);
                            }
                            else
                            {
                                // If there's already a call in progress hang it up. Of course this is not ideal for a real softphone or server but it 
                                // means this example can be kept simpler.
                                if (uas?.IsHungup == false)
                                {
                                    uas?.Hangup(false);
                                }
                                rtpCts?.Cancel();
                                rtpCts = new CancellationTokenSource();

                                UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                                uas = new SIPServerUserAgent(sipTransport, null, null, null, SIPCallDirection.In, null, null, uasTransaction);
                                uas.CallCancelled += (uasAgent) =>
                                {
                                    rtpCts?.Cancel();
                                    rtpSession.Close(null);
                                };
                                rtpSession.OnRtpClosed += (reason) => uas?.Hangup(false);
                                uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                                await Task.Delay(100);
                                uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
                                await Task.Delay(100);

                                var answerSdp = rtpSession.CreateAnswer(null);
                                uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answerSdp.ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);

                                await rtpSession.Start();
                            }
                        }
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.BYE)
                    {
                        Log.LogInformation("Call hungup.");
                        SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        await sipTransport.SendResponseAsync(byeResponse);
                        uas?.Hangup(true);
                        rtpSession?.Close(null);
                        rtpCts?.Cancel();
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                    {
                        SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        await sipTransport.SendResponseAsync(notAllowededResponse);
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                    {
                        SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        await sipTransport.SendResponseAsync(optionsResponse);
                    }
                    }
                catch (Exception reqExcp)
                {
                    Log.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
                }
            };

            ManualResetEvent exitMre = new ManualResetEvent(false);

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                Log.LogInformation("Exiting...");

                Hangup(uas).Wait();

                rtpSession?.Close(null);
                rtpCts?.Cancel();

                if (sipTransport != null)
                {
                    Log.LogInformation("Shutting down SIP transport...");
                    sipTransport.Shutdown();
                }

                exitMre.Set();
            };

            // Task to handle user key presses.
            Task.Run(() =>
            {
                try
                {
                    while (!exitMre.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();
                        if (keyProps.KeyChar == 'h' || keyProps.KeyChar == 'q')
                        {
                            Console.WriteLine();
                            Console.WriteLine("Hangup requested by user...");

                            Hangup(uas).Wait();

                            rtpSession?.Close(null);
                            rtpCts?.Cancel();
                        }

                        if (keyProps.KeyChar == 'q')
                        {
                            Log.LogInformation("Quitting...");

                            if (sipTransport != null)
                            {
                                Log.LogInformation("Shutting down SIP transport...");
                                sipTransport.Shutdown();
                            }

                            exitMre.Set();
                        }
                    }
                }
                catch (Exception excp)
                {
                    Log.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            exitMre.WaitOne();
        }

        /// <summary>
        /// Hangs up the current call.
        /// </summary>
        /// <param name="uas">The user agent server to hangup the call on.</param>
        private static async Task Hangup(SIPServerUserAgent uas)
        {
            try
            {
                if (uas?.IsHungup == false)
                {
                    uas?.Hangup(false);

                    // Give the BYE or CANCEL request time to be transmitted.
                    Log.LogInformation("Waiting 1s for call to hangup...");
                    await Task.Delay(1000);
                }
            }
            catch (Exception excp)
            {
                Log.LogError($"Exception Hangup. {excp.Message}");
            }
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Log.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Log.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
