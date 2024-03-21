using System.Diagnostics;
using System.Runtime.CompilerServices;

using DataChannelBandwidth;

using Microsoft.Extensions.Logging;

using SIPSorcery.Net;

ILoggerFactory logs = LoggerFactory.Create(
    builder => builder.AddFilter(level => level >= LogLevel.Debug).AddConsole());

var log = logs.CreateLogger("Bandwidth");

// SIPSorcery.LogFactory.Set(logs);

var rtcConfig = new RTCConfiguration
{
    iceServers = [
        new() { urls = "stun:stun.l.google.com:19302" },
    ],
};

long clientReceived = 0;
long serverReceived = 0;

bool closed = false;

for (int i = 0; i < 1; i++)
{
    var launch = Stopwatch.StartNew();
    var server = new RTCPeerConnection(rtcConfig);
    var client = new RTCPeerConnection(rtcConfig);

    client.onconnectionstatechange += state =>
    {
        if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed)
        {
            log.LogInformation("client connection {State}", state);
            //Volatile.Write(ref closed, true);
        }
    };

    client.ondatachannel += ch =>
    {
        new Thread(() => SendRecv(ch, ref clientReceived))
        {
            IsBackground = true,
            Name = $"client {i}",
        }.Start();
        if (!ch.IsOpened)
        {
            log.LogInformation("client channel never opened");
            Volatile.Write(ref closed, true);
        }
        ch.onclose += () =>
        {
            log.LogInformation("client channel closed");
            Volatile.Write(ref closed, true);
        };
        ch.onerror += (s) =>
        {
            log.LogInformation("client channel error: {Message}", s);
            Volatile.Write(ref closed, true);
        };
    };


    var serverCH = await server.createDataChannel("test");
    serverCH.onopen += () =>
    {
        new Thread(() => SendRecv(serverCH, ref serverReceived))
        {
            IsBackground = true,
            Name = $"server {i}",
        }.Start();
    };
    serverCH.onclose += () =>
    {
        log.LogInformation("server channel closed");
        Volatile.Write(ref closed, true);
    };
    serverCH.onerror += (s) =>
    {
        log.LogInformation("server channel error: {Message}", s);
        Volatile.Write(ref closed, true);
    };
    var offer = server.createOffer();
    await server.setLocalDescription(offer);
    client.setRemoteDescription(offer);
    var answer = client.createAnswer();
    server.setRemoteDescription(answer);
    await client.setLocalDescription(answer);

    Console.WriteLine($"launched in {launch.ElapsedMilliseconds}ms");
}

var stopwatch = Stopwatch.StartNew();
Interlocked.Exchange(ref clientReceived, 0);
Interlocked.Exchange(ref serverReceived, 0);

while (!Volatile.Read(ref closed))
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

void SendRecv(RTCDataChannel channel, ref long received,
              [CallerArgumentExpression(nameof(channel))] string name = "")
{
    var stream = new DataChannelStream(channel);
    var sender = new Thread(() => Send(channel))
    {
        IsBackground = true,
        Name = $"{name} sender",
    };
    sender.Start();

    byte[] buffer = new byte[200_000];
    while (true)
    {
        Interlocked.Add(ref received, stream.Read(buffer));
    }
}