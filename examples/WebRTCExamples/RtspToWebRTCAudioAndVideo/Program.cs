using RtspToWebRtcRestreamer;
using System.Net;
using static Org.BouncyCastle.Math.EC.ECCurve;

//globals
var _wsPort = 5300;
var cts = new CancellationTokenSource();

//Setup demuxer configuration
var config = new DemuxerConfig
{
    rtspUrl = "rtsp://admin:HelloWorld4@192.168.1.64:554/ISAPI/Streaming/Channels/101",
    vcodec = "h264",
    acodec = "pcm_alaw",
    audioPort = 5022,
    videoPort = 5020,
    audioSsrc = 50,
    videoSsrc = 40,
    serverIP = IPAddress.Loopback.MapToIPv4().ToString(),
    outputStream = StreamsEnum.videoAndAudio
};

// Run ffmpeg demuxer
if (config.outputStream != StreamsEnum.none)
{
    var demuxer = new FFmpegDemuxer(config);
    demuxer.Run();
}

// Create and run Listener
var ffmpegListener = new FFmpegListener(config);

Task.Run(() => ffmpegListener.Run(cts.Token));
while (!ffmpegListener.ready) { }

//Create and Run WebSocketServer
var wsServer = new WebSocketSignalingServer(ffmpegListener, _wsPort);
wsServer.Run();


// Waiting connection from webbrowserW
// exit loop
var running = true;
var readTask = new Task(() =>
{
    while (true)
    {
        var input = Console.ReadKey(true);
        if (input.KeyChar == 'q')
        {
            running = false;
            cts.Cancel();
            break;
        }
    }
});
readTask.Start();

while (running)
{

}
