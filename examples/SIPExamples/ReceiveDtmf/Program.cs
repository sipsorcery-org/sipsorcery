//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Sample program of how to receive DTMF tones using RTP events
// as specified in RFC2833.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using TinyJson;

namespace demo
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static SIPTransport _sipTransport;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Receive DTMF Demo");

            Log = AddConsoleLogger();

            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            var userAgent = new SIPUserAgent(_sipTransport, null, true);
            userAgent.ServerCallCancelled += (uas, cancelReq) => Log.LogDebug("Incoming call cancelled by remote party.");
            userAgent.OnCallHungup += (dialog) => Log.LogDebug("Call hungup.");
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                Log.LogDebug($"SDP Offer:\r\n{req.Body}");

                VoIPMediaSession voipMediaSession = new VoIPMediaSession();
                voipMediaSession.AcceptRtpFromAny = true;

                var uas = userAgent.AcceptCall(req);

                await userAgent.Answer(uas, voipMediaSession);

                var sdpAnswer = voipMediaSession.CreateAnswer(IPAddress.Loopback);

                Log.LogDebug($"SDP Answer:\r\n{sdpAnswer.ToString()}");
            };
            userAgent.OnDtmfTone += (tone, duration) => Log.LogInformation($"DTMF tone {tone} of duration {duration}ms received.");

            Console.WriteLine("press any key to exit...");
            Console.Read();

            // Clean up.
            _sipTransport.Shutdown();
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
