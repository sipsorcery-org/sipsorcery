using System.Net;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

const int SIP_LISTEN_PORT = 5060;
const string MUSIC_FILENAME = "music.raw";

Console.WriteLine("SIP Send Music");

AddConsoleLogger();

var sipTransport = new SIPTransport();
sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
//sipTransport.EnableTraceLogs();

var userAgent = new SIPUserAgent(sipTransport, null, true);
userAgent.ServerCallCancelled += (uas) => Console.WriteLine("Incoming call cancelled by remote party.");
userAgent.OnIncomingCall += async (ua, req) =>
{
    var sendOnlyMusic = new VoIPMediaSession(MUSIC_FILENAME, formats => formats.Codec == AudioCodecsEnum.PCMU);
    sendOnlyMusic.AcceptRtpFromAny = true;

    var uas = userAgent.AcceptCall(req);
    await userAgent.Answer(uas, sendOnlyMusic);
};

Console.WriteLine("press any key to exit...");
Console.Read();

/// <summary>
/// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
/// </summary>
void AddConsoleLogger()
{
    var serilogLogger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
        .WriteTo.Console()
        .CreateLogger();
    var factory = new SerilogLoggerFactory(serilogLogger);
    SIPSorcery.LogFactory.Set(factory);
}