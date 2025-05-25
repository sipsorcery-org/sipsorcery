//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An abbreviated example program of how to use the SIPSorcery 
// core library to place a SIP call. The example program depends on one audio 
// input and one audio output being available.
//
// This example is based on teh UserAgentClient example but has been tuned
// to work with the SIPCloudCallServer example deployed in a Kubernetes cluster.
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

namespace demo;

class Program
{
    private const string STUN_URL_ENV_VAR = "STUN_URL";

    private static int ICE_SERVER_LOOKUP_TIMEOUT_SECONDS = 5;
    private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:music@iptel.org";

    private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

    private static IceServerResolver _iceServerResolver = new IceServerResolver();

    static async Task Main(string[] args)
    {
        Console.WriteLine("SIPSorcery client user agent example.");
        Console.WriteLine("Press ctrl-c to exit.");

        // Plumbing code to facilitate a graceful exit.
        ManualResetEvent exitMre = new ManualResetEvent(false);
        bool preferIPv6 = false;
        bool isCallHungup = false;
        bool hasCallFailed = false;

        Log = AddConsoleLogger(LogEventLevel.Debug);

        SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
        if (args?.Length > 0)
        {
            if (!SIPURI.TryParse(args[0], out callUri))
            {
                Log.LogWarning($"Command line argument could not be parsed as a SIP URI {args[0]}");
            }
        }
        if(args?.Length > 1 && args[1] == "ipv6")
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

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(STUN_URL_ENV_VAR)))
        {
            _iceServerResolver.InitialiseIceServers(
                new List<RTCIceServer> { new RTCIceServer { urls = Environment.GetEnvironmentVariable(STUN_URL_ENV_VAR) } },
                RTCIceTransportPolicy.all);
        }

        // Set up a default SIP transport.
        var sipTransport = new SIPTransport();
        sipTransport.PreferIPv6NameResolution = preferIPv6;
        sipTransport.EnableTraceLogs();

        var audioSession = new WindowsAudioEndPoint(new AudioEncoder());
        //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA || x.Codec == AudioCodecsEnum.PCMU);
        //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA);
        var rtpSession = new VoIPMediaSession(audioSession.ToMediaEndPoints());
        rtpSession.AcceptRtpFromAny = true;

        var rtpPublicEndPoint = await GetRtpPublicEndPoint(rtpSession);

        if (rtpPublicEndPoint != null)
        {
            var audioRtpChannel = rtpSession.AudioStream.GetRTPChannel();

            Log.LogInformation("RTP public end point for {private} resolved to {public}.", audioRtpChannel.RTPLocalEndPoint, rtpPublicEndPoint);

            audioRtpChannel.RTPDynamicNATEndPoint = rtpPublicEndPoint;
        }
        else
        {
            Log.LogWarning("RTP public end point for {private} could not be resolved.", rtpSession.AudioStream.GetRTPChannel().RTPLocalEndPoint);
        }

        var offerSDP = rtpSession.CreateOffer(rtpPublicEndPoint?.Address ?? (preferIPv6 ? IPAddress.IPv6Any : IPAddress.Any));

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
                else if(!rtpSession.IsStarted)
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

    private static async Task<IPEndPoint> GetRtpPublicEndPoint(RTPSession rtpSession)
    {
        await _iceServerResolver.WaitForAllIceServersAsync(TimeSpan.FromSeconds(ICE_SERVER_LOOKUP_TIMEOUT_SECONDS));

        var iceServers = _iceServerResolver.IceServers;

        // Use first availab STUN server to get the public IP address.
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
