//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An abbreviated example program of how to use the SIPSorcery 
// core library to place a SIP call. The example program depends on one audio 
// input and one audio output being available.
//
// Usage Examples:
// 22 Sep 2025  Aaron Clauson
//
// TURN:
// Note in order for the TURN client to be activated the REMOTE_PEER_IP must be set.
// This is so the TURN client can create a permission for the remote peer.
// If running locally with the UserAgentServer demo get your IP using: curl ifconfig.me
// and set that as the REMOTE_PEER_IP.
// BUT
// If both user agents are using a TURN relay the create permission needs to be for each
// other's relay end point. This means the REMOTE_PEER_IP needs to be the IP address of
// the TURN server (both agents can use the same TURN server in which case both should
// set the REMOTE_PEER_IP to the same TURN server IP).
//
// STUN:
// Currently the STUN client is wired up and if a STUN server URL is provided the SDP offer
// connection IP address will be set to the agent's server reflexive address from the STUN server.
// This is typically not very useful and will almost always cause more problems than it solves.
//
// set TURN_URL=turn:your.turn.server;user;password
// set REMOTE_PEER_IP=1.1.1.1
// set STUN_URL=stun:stun.l.google.com:19302 
// dotnet run -- 127.0.0.1:5080
//
// Author(s):
// Aaron Clauson  (aaron@sipsorcery.com)
// 
// History:
// 26 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 26 Feb 2020  Aaron Clauson   Switched RTP to use RtpAVSession.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Events;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

namespace demo;

class Program
{
    private const string TURN_SERVER_URL_ENV_VAR = "TURN_URL";
    private const string STUN_SERVER_URL_ENV_VAR = "STUN_URL";
    private const string REMOTE_PEER_IP_ENV_VAR = "REMOTE_PEER_IP";

    private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:music@iptel.org";
    private const int TURN_ALLOCATION_TIMEOUT_SECONDS = 5;

    private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

    static async Task Main(string[] args)
    {
        Console.WriteLine("SIPSorcery client user agent example.");
        Console.WriteLine("Press ctrl-c to exit.");

        // Plumbing code to facilitate a graceful exit.
        ManualResetEvent exitMre = new ManualResetEvent(false);
        bool preferIPv6 = false;
        bool isCallHungup = false;
        bool hasCallFailed = false;
        IPAddress remotePeerIPAddress = IPAddress.None;

        Log = AddConsoleLogger(LogEventLevel.Debug);

        var turnServerUrl = Environment.GetEnvironmentVariable(TURN_SERVER_URL_ENV_VAR);
        TurnClient turnClient = !string.IsNullOrWhiteSpace(turnServerUrl) ?
            new TurnClient(turnServerUrl) : null;

        var stunServerUrl = Environment.GetEnvironmentVariable(STUN_SERVER_URL_ENV_VAR);
        STUNClient stunClient = !string.IsNullOrWhiteSpace(stunServerUrl) ?
            new STUNClient(stunServerUrl) : null;

        var remoteIP = Environment.GetEnvironmentVariable(REMOTE_PEER_IP_ENV_VAR);
        if (!string.IsNullOrWhiteSpace(remoteIP))
        {
            if (IPAddress.TryParse(remoteIP.Trim(), out var parsedIP))
            {
                remotePeerIPAddress = parsedIP;
                Log.LogInformation("Using remote peer IP address {ip}.", remotePeerIPAddress);
            }
            else
            {
                Log.LogWarning($"The remote peer IP address provided in the {REMOTE_PEER_IP_ENV_VAR} environment variable could not be parsed as a valid IP address.");
            }
        }

        SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
        if (args?.Length > 0)
        {
            if (!SIPURI.TryParse(args[0], out callUri))
            {
                Log.LogWarning($"Command line argument could not be parsed as a SIP URI {args[0]}");
            }
        }
        if (args?.Length > 1 && args[1] == "ipv6")
        {
            preferIPv6 = true;
        }

        if (preferIPv6)
        {
            Log.LogInformation($"Call destination {callUri}, preferencing IPv6.");
        }
        else
        {
            Log.LogInformation($"Call destination {callUri}.");
        }

        // Set up a default SIP transport.
        var sipTransport = new SIPTransport();
        sipTransport.PreferIPv6NameResolution = preferIPv6;
        sipTransport.EnableTraceLogs();

        var audioSession = new WindowsAudioEndPoint(new AudioEncoder());
        //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA || x.Codec == AudioCodecsEnum.PCMU);
        //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA);
        var rtpSession = new VoIPMediaSession(audioSession.ToMediaEndPoints());

        if (turnClient != null && remotePeerIPAddress != IPAddress.None)
        {
            await rtpSession.AudioStream.UseTurn(turnClient, remotePeerIPAddress, default, TURN_ALLOCATION_TIMEOUT_SECONDS);
        }
        else if (stunClient != null)
        {
            await rtpSession.AudioStream.UseStun(stunClient, default, Log);
        }

        var offerSDP = rtpSession.CreateOffer(preferIPv6 ? IPAddress.IPv6Any : IPAddress.Any);

        // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
        var uac = new SIPClientUserAgent(sipTransport);
        uac.CallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
        uac.CallRinging += async (uac, resp) =>
        {
            Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            if (resp.Status == SIPResponseStatusCodesEnum.SessionProgress)
            {
                if (resp.Body != null)
                {
                    var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                    if (result == SetDescriptionResultEnum.OK)
                    {
                        await rtpSession.Start();
                        Log.LogInformation($"Remote SDP set from in progress response. RTP session started.");
                    }
                }
            }
        };
        uac.CallFailed += (uac, err, resp) =>
        {
            Log.LogWarning($"Call attempt to {uac.CallDescriptor.To} Failed: {err}");
            hasCallFailed = true;
        };
        uac.CallAnswered += async (iuac, resp) =>
        {
            if (resp.Status == SIPResponseStatusCodesEnum.Ok)
            {
                Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

                if (resp.Body != null)
                {
                    var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                    if (result == SetDescriptionResultEnum.OK)
                    {
                        await rtpSession.Start();
                    }
                    else
                    {
                        Log.LogWarning($"Failed to set remote description {result}.");
                        uac.Hangup();
                    }
                }
                else if (!rtpSession.IsAudioStarted)
                {
                    Log.LogWarning($"Failed to set get remote description in session progress or final response.");
                    uac.Hangup();
                }
            }
            else
            {
                Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
            }
        };

        // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
        sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(okResponse);

                if (uac.IsUACAnswered)
                {
                    Log.LogInformation("Call was hungup by remote server.");
                    isCallHungup = true;
                    exitMre.Set();
                }
            }
        };

        // Start the thread that places the call.
        SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
            SIPConstants.SIP_DEFAULT_USERNAME,
            null,
            callUri.ToString(),
            SIPConstants.SIP_DEFAULT_FROMURI,
            callUri.CanonicalAddress,
            null, null, null,
            SIPCallDirection.Out,
            SDP.SDP_MIME_CONTENTTYPE,
            offerSDP.ToString(),
            null);

        uac.Call(callDescriptor, null);

        // Ctrl-c will gracefully exit the call at any point.
        Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            exitMre.Set();
        };

        // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
        exitMre.WaitOne();

        Log.LogInformation("Exiting...");

        rtpSession.Close(null);

        if (!isCallHungup && uac != null)
        {
            if (uac.IsUACAnswered)
            {
                Log.LogInformation($"Hanging up call to {uac.CallDescriptor.To}.");
                uac.Hangup();
            }
            else if (!hasCallFailed)
            {
                Log.LogInformation($"Cancelling call to {uac.CallDescriptor.To}.");
                uac.Cancel();
            }

            // Give the BYE or CANCEL request time to be transmitted.
            Log.LogInformation("Waiting 1s for call to clean up...");
            Task.Delay(1000).Wait();
        }

        if (sipTransport != null)
        {
            Log.LogInformation("Shutting down SIP transport...");
            sipTransport.Shutdown();
        }
    }

    /// <summary>
    /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
    /// </summary>
    private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger(
        LogEventLevel logLevel = LogEventLevel.Debug)
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console()
            .CreateLogger();
        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<Program>();
    }
}
