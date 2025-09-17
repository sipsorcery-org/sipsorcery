//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// act as the server for a SIP call with a TURN server reservation used for the
// media path.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 16 Aug 2025	Aaron Clauson	Created, Wexford, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
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
        private const string TURN_SERVER_URL_ENV_VAR = "TURN_URL";

        private static int SIP_LISTEN_PORT = 5080;

        private const string WELCOME_8K = "Sounds/hellowelcome8k.raw";
        private const string GOODBYE_16K = "Sounds/goodbye16k.raw";

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static IceServerResolver _iceServerResolver = new IceServerResolver();

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery user agent server example.");
            Console.WriteLine("Press h to hangup a call or ctrl-c to exit.");

            Log = AddConsoleLogger();

            var turnServerUrl = Environment.GetEnvironmentVariable(TURN_SERVER_URL_ENV_VAR);
            if (!string.IsNullOrWhiteSpace(turnServerUrl))
            {
                var turnServer = IceServer.ParseIceServer(turnServerUrl);
                Log.LogInformation($"Using TURN server {turnServer.Uri}");

                _iceServerResolver.InitialiseIceServers([RTCIceServer.Parse(turnServerUrl)], RTCIceTransportPolicy.all);
            }

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();

            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            //sipTransport.EnableTraceLogs();

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

                        UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                        uas = new SIPServerUserAgent(sipTransport, null, uasTransaction, null);
                        uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);

                        uas.CallCancelled += (uasAgent, canelReq) =>
                        {
                            rtpCts?.Cancel();
                            rtpSession.Close(null);
                        };

                        // Check there's a codec we support in the INVITE offer.
                        var offerSdp = SDP.ParseSDPDescription(sipRequest.Body);
                        IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);

                        //if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaFormats.Any(x => x.Key == (int)SDPWellKnownMediaFormatsEnum.PCMU)))
                        //{
                        Log.LogDebug($"Client offer contained PCMU audio codec.");

                        uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                        rtpSession = new VoIPMediaSession();
                        rtpSession.OnRtpClosed += (reason) => uas?.Hangup(false);
                        rtpCts?.Cancel();
                        rtpCts = new CancellationTokenSource();

                        IPEndPoint relayEndPoint = null;

                        //if (_iceServerResolver.IceServers.Any())
                        //{
                        //    var turnClient = new TurnClient(_iceServerResolver.IceServers.Values.First(), rtpSession.AudioStream.GetRTPChannel());
                        //    relayEndPoint = await turnClient.GetRelayEndPoint(15000, rtpCts.Token);
                        //}

                        var setResult = rtpSession.SetRemoteDescription(SdpType.offer, offerSdp);

                        if (setResult != SetDescriptionResultEnum.OK)
                        {
                            // Didn't get a match on the codecs we support.
                            SIPResponse noMatchingCodecResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptableHere, setResult.ToString());
                            await sipTransport.SendResponseAsync(noMatchingCodecResponse);
                        }
                        else
                        {
                            var answerSdp = rtpSession.CreateAnswer(null);
                            uas.Answer(SDP.SDP_MIME_CONTENTTYPE, answerSdp.ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);

                            await rtpSession.Start();
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
