using System.Diagnostics;

using DataChannelBandwidth;

using Microsoft.Extensions.Logging;

using SIPSorcery.Net;

ILoggerFactory logs = LoggerFactory.Create(
    builder => builder.AddFilter(level => level >= LogLevel.Debug).AddConsole());

// SIPSorcery.LogFactory.Set(logs);

var rtcConfig = new RTCConfiguration
{
    iceServers = new List<RTCIceServer> {
        new() { urls = "stun:stun.l.google.com:19302" },
    },
};

var server = new RTCPeerConnection(rtcConfig);
var client = new RTCPeerConnection(rtcConfig);

long clientReceived = 0;
long serverReceived = 0;

client.ondatachannel += ch =>
{
    new Thread(() => SendRecv(ch, ref clientReceived))
    {
        IsBackground = true,
        Name = "client",
    }.Start();
};


var serverCH = await server.createDataChannel("test");
serverCH.onopen += () =>
{
    new Thread(() => SendRecv(serverCH, ref serverReceived))
    {
        IsBackground = true,
        Name = "server",
    }.Start();
};
var offer = server.createOffer();
await server.setLocalDescription(offer);
client.setRemoteDescription(offer);
var answer = client.createAnswer();
server.setRemoteDescription(answer);
await client.setLocalDescription(answer);

var stopwatch = Stopwatch.StartNew();

while (true)
{
    long recvC = Interlocked.Read(ref clientReceived);
    long recvS = Interlocked.Read(ref serverReceived);
    double rateC = recvC / 1024 / 1024 / stopwatch.Elapsed.TotalSeconds;
    double rateS = recvS / 1024 / 1024 / stopwatch.Elapsed.TotalSeconds;
    // almost 2000 MB/s with TCP over loopback
    Console.Title = $"client: {rateC:F1}MB/s server: {rateS:F1}MB/s total: {rateC + rateS:F1}MB/s";
    Thread.Sleep(1000);
}

void Send(RTCDataChannel channel)
{
    byte[] sample = new byte[180_000];
    while (true)
    {
        channel.send(sample);
    }
}

void SendRecv(RTCDataChannel channel, ref long received)
{
    var stream = new DataChannelStream(channel);
    var sender = new Thread(() => Send(channel))
    {
        IsBackground = true,
    };
    sender.Start();
    
    byte[] buffer = new byte[200_000];
    while (true)
    {
        Interlocked.Add(ref received, stream.Read(buffer));
    }
}