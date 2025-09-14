//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// act as the server for a SIP call. This version adds a STUN client to determine
// the public IP address of the server and use that in the SDP answer to the UAC.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Sep 2025	Aaron Clauson	Created, based on UserAgentServer exmaple.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Usage:
// set STUN_URL=stun:stun.l.google.com:19302
// dotnet run
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery
{
    class Program
    {
        private const string STUN_URL_ENV_VAR = "STUN_URL";

        private const string DEFAULT_STUN_URL = "stun:stun.l.google.com:19302";

        private static int SIP_LISTEN_PORT = 5080;
        private static int SIPS_LISTEN_PORT = 5061;
        private static int ICE_SERVER_LOOKUP_TIMEOUT_SECONDS = 5;
        //private static int SIP_WEBSOCKET_LISTEN_PORT = 80;
        //private static int SIP_SECURE_WEBSOCKET_LISTEN_PORT = 443;
        private static string SIPS_CERTIFICATE_PATH = "localhost.pfx";

        private const string WELCOME_8K = "Sounds/hellowelcome8k.raw";
        private const string GOODBYE_16K = "Sounds/goodbye16k.raw";

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static IceServerResolver _iceServerResolver = new IceServerResolver();

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

            string stunServer = Environment.GetEnvironmentVariable(STUN_URL_ENV_VAR);
            if (string.IsNullOrWhiteSpace(stunServer))
            {
                stunServer = DEFAULT_STUN_URL;
            }

            _iceServerResolver.InitialiseIceServers(
                new List<RTCIceServer> { new RTCIceServer { urls = stunServer } },
                RTCIceTransportPolicy.all);

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();

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
                            //AudioExtrasSource extrasSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
                            //rtpSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = extrasSource });
                            rtpSession = new VoIPMediaSession();
                            rtpSession.AcceptRtpFromAny = true;

                            var rtpPublicEndPoint = await GetRtpPublicEndPoint(rtpSession);

                            if (rtpPublicEndPoint == null)
                            {
                                Log.LogWarning("RTP public end point for {private} could not be resolved.", rtpSession.AudioStream.GetRTPChannel().RTPLocalEndPoint);

                                SIPResponse noMatchingCodecResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptableHere, "STUN server failure");
                                await sipTransport.SendResponseAsync(noMatchingCodecResponse);
                            }
                            else
                            {
                                var audioRtpChannel = rtpSession.AudioStream.GetRTPChannel();

                                Log.LogInformation("RTP public end point for {private} resolved to {public}.", audioRtpChannel.RTPLocalEndPoint, rtpPublicEndPoint);

                                audioRtpChannel.RTPDynamicNATEndPoint = rtpPublicEndPoint;

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
                                    uas = new SIPServerUserAgent(sipTransport, null, uasTransaction, null);
                                    uas.CallCancelled += (uasAgent, canelReq) =>
                                    {
                                        rtpCts?.Cancel();
                                        rtpSession.Close(null);
                                    };
                                    rtpSession.OnRtpClosed += (reason) => uas?.Hangup(false);
                                    uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                                    await Task.Delay(100);
                                    uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
                                    await Task.Delay(100);

                                    var answerSdp = rtpSession.CreateAnswer(rtpPublicEndPoint?.Address);
                                    uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answerSdp.ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);

                                    await rtpSession.Start();
                                }
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
            Task.Run(async () =>
            {
                try
                {
                    while (!exitMre.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();

                        if (keyProps.KeyChar == 'w')
                        {
                            Console.WriteLine();
                            Console.WriteLine("Welcome requested by user...");

                            if (rtpSession?.IsAudioStarted == true &&
                                rtpSession?.IsClosed == false)
                            {
                                await rtpSession.AudioExtrasSource.SendAudioFromStream(new FileStream(WELCOME_8K, FileMode.Open), AudioSamplingRatesEnum.Rate8KHz);
                            }
                        }

                        if (keyProps.KeyChar == 'h' || keyProps.KeyChar == 'q')
                        {
                            Console.WriteLine();
                            Console.WriteLine("Hangup requested by user...");

                            if (rtpSession?.IsAudioStarted == true &&
                                rtpSession?.IsClosed == false)
                            {
                                await rtpSession.AudioExtrasSource.SendAudioFromStream(new FileStream(GOODBYE_16K, FileMode.Open), AudioSamplingRatesEnum.Rate16KHz);
                            }

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

        private static async Task<IPEndPoint> GetRtpPublicEndPoint(RTPSession rtpSession)
        {
            await _iceServerResolver.WaitForAllIceServersAsync(TimeSpan.FromSeconds(ICE_SERVER_LOOKUP_TIMEOUT_SECONDS));

            var iceServers = _iceServerResolver.IceServers;

            // Use first available STUN server to get the public IP address.
            if (iceServers.Count == 0 || iceServers.All(x => x.Value.ServerEndPoint == null))
            {
                Log.LogWarning("No ICE servers available to get public IP address.");
                return null;
            }

            var iceServerEndPoint = iceServers.First(x => x.Value.ServerEndPoint != null);

            Log.LogDebug($"Using ICE server {iceServerEndPoint.Key} -> {iceServerEndPoint.Value.ServerEndPoint} to get public IP address.");

            return await STUNClient.GetPublicIPEndPointForSocketAsync(iceServerEndPoint.Value.ServerEndPoint, rtpSession.AudioStream.GetRTPChannel());
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
