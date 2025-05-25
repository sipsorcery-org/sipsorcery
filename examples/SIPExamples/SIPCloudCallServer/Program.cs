//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example SIP server program to accept and initiate calls.
// This example is derived from teh SIPCallServer example and is tuned to use
// in a Kuberenetes cluster.
//
// Author(s):
// Aaron Clauson  (aaron@sipsorcery.com)
// 
// History:
// 25 May 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
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
        private const string SIP_CONTACT_HOST_ENV_VAR = "SIP_CONTACT_HOST";
        private const string RTP_PORT_ENV_VAR = "RTP_PORT";
        private const string STUN_URL_ENV_VAR = "STUN_URL";

        private static int SIP_LISTEN_PORT = 5060;
        private static int SIPS_LISTEN_PORT = 5061;
        private static string SIPS_CERTIFICATE_PATH = "localhost.pfx";

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static SIPTransport _sipTransport;

        /// <summary>
        /// Keeps track of the current active calls. It includes both received and placed calls.
        /// </summary>
        private static ConcurrentDictionary<string, SIPUserAgent> _calls = new ConcurrentDictionary<string, SIPUserAgent>();

        private static int _rtpPort = 0;

        private static IPAddress _publicIpAddress = null;

        static void Main()
        {
            Console.WriteLine("SIPSorcery SIP Call Server example.");

            Log = AddConsoleLogger();

            // Set up a default SIP transport.
            _sipTransport = new SIPTransport();

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SIP_CONTACT_HOST_ENV_VAR)))
            {
                _sipTransport.ContactHost = Environment.GetEnvironmentVariable(SIP_CONTACT_HOST_ENV_VAR);
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RTP_PORT_ENV_VAR)))
            {
                int.TryParse(Environment.GetEnvironmentVariable(RTP_PORT_ENV_VAR), out _rtpPort);
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(STUN_URL_ENV_VAR)))
            {
                _publicIpAddress = STUNClient.GetPublicIPAddress(Environment.GetEnvironmentVariable(STUN_URL_ENV_VAR));
            }

            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, SIP_LISTEN_PORT)));
            _sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
            var localhostCertificate = new X509Certificate2(SIPS_CERTIFICATE_PATH);
            _sipTransport.AddSIPChannel(new SIPTLSChannel(localhostCertificate, new IPEndPoint(IPAddress.Any, SIPS_LISTEN_PORT)));
            // If it's desired to listen on a single IP address use the equivalent of:
            //_sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Parse("192.168.11.50"), SIP_LISTEN_PORT)));
            _sipTransport.EnableTraceLogs();

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            Log.LogInformation("Exiting...");

            if (_sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }
        }

        /// <summary>
        /// Example of how to create a basic RTP session object and hook up the event handlers.
        /// </summary>
        /// <param name="ua">The user agent the RTP session is being created for.</param>
        /// <param name="dst">THe destination specified on an incoming call. Can be used to
        /// set the audio source.</param>
        /// <returns>A new RTP session object.</returns>
        private static VoIPMediaSession CreateRtpSession(SIPUserAgent ua, string dst, int bindPort)
        {
            List<AudioCodecsEnum> codecs = new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU, AudioCodecsEnum.PCMA, AudioCodecsEnum.G722 };

            var audioSource = AudioSourcesEnum.SineWave;
            if (string.IsNullOrEmpty(dst) || !Enum.TryParse(dst, out audioSource))
            {
                audioSource = AudioSourcesEnum.Music;
            }

            Log.LogInformation($"RTP audio session source set to {audioSource}.");

            AudioExtrasSource audioExtrasSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = audioSource });
            audioExtrasSource.RestrictFormats(formats => codecs.Contains(formats.Codec));
            var rtpAudioSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioExtrasSource }, bindPort: bindPort);
            rtpAudioSession.AcceptRtpFromAny = true;

            // Wire up the event handler for RTP packets received from the remote party.
            rtpAudioSession.OnRtpPacketReceived += (ep, type, rtp) => OnRtpPacketReceived(ua, ep, type, rtp);

            rtpAudioSession.OnTimeout += (mediaType) =>
            {
                if (ua?.Dialogue != null)
                {
                    Log.LogWarning($"RTP timeout on call with {ua.Dialogue.RemoteTarget}, hanging up.");
                }
                else
                {
                    Log.LogWarning($"RTP timeout on incomplete call, closing RTP session.");
                }

                ua.Hangup();
            };

            return rtpAudioSession;
        }

        /// <summary>
        /// Event handler for receiving RTP packets.
        /// </summary>
        /// <param name="ua">The SIP user agent associated with the RTP session.</param>
        /// <param name="type">The media type of the RTP packet (audio or video).</param>
        /// <param name="rtpPacket">The RTP packet received from the remote party.</param>
        private static void OnRtpPacketReceived(SIPUserAgent ua, IPEndPoint remoteEp, SDPMediaTypesEnum type, RTPPacket rtpPacket)
        {
            // The raw audio data is available in rtpPacket.Payload.
            //Log.LogTrace($"OnRtpPacketReceived from {remoteEp}.");
        }

        /// <summary>
        /// Event handler for receiving a DTMF tone.
        /// </summary>
        /// <param name="ua">The user agent that received the DTMF tone.</param>
        /// <param name="key">The DTMF tone.</param>
        /// <param name="duration">The duration in milliseconds of the tone.</param>
        private static void OnDtmfTone(SIPUserAgent ua, byte key, int duration)
        {
            string callID = ua.Dialogue.CallId;
            Log.LogInformation($"Call {callID} received DTMF tone {key}, duration {duration}ms.");
        }

        /// <summary>
        /// Because this is a server user agent the SIP transport must start listening for client user agents.
        /// </summary>
        private static async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (sipRequest.Header.From != null &&
                sipRequest.Header.From.FromTag != null &&
                sipRequest.Header.To != null &&
                sipRequest.Header.To.ToTag != null)
                {
                    // This is an in-dialog request that will be handled directly by a user agent instance.
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    Log.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                    SIPUserAgent ua = new SIPUserAgent(_sipTransport, null);
                    ua.OnCallHungup += OnHangup;
                    ua.ServerCallCancelled += (uas, cancelReq) => Log.LogDebug("Incoming call cancelled by remote party.");
                    ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);
                    ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                    //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
                    ua.ServerCallRingTimeout += (uas) =>
                    {
                        Log.LogWarning($"Incoming call timed out in {uas.ClientTransaction.TransactionState} state waiting for client ACK, terminating.");
                        ua.Hangup();
                    };
                    var uas = ua.AcceptCall(sipRequest);
                    var rtpSession = CreateRtpSession(ua, sipRequest.URI.User, _rtpPort);

                    await ua.Answer(uas, rtpSession, _publicIpAddress);

                    if (ua.IsCallActive)
                    {
                        await rtpSession.Start();
                        _calls.TryAdd(ua.Dialogue.CallId, ua);
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    await _sipTransport.SendResponseAsync(byeResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                {
                    SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    await _sipTransport.SendResponseAsync(notAllowededResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await _sipTransport.SendResponseAsync(optionsResponse);
                }
            }
            catch (Exception reqExcp)
            {
                Log.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
            }
        }

        /// <summary>
        /// Remove call from the active calls list.
        /// </summary>
        /// <param name="dialogue">The dialogue that was hungup.</param>
        private static void OnHangup(SIPDialogue dialogue)
        {
            if (dialogue != null)
            {
                string callID = dialogue.CallId;
                if (_calls.ContainsKey(callID))
                {
                    if (_calls.TryRemove(callID, out var ua))
                    {
                        // This app only uses each SIP user agent once so here the agent is 
                        // explicitly closed to prevent is responding to any new SIP requests.
                        ua.Close();
                    }
                }
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
