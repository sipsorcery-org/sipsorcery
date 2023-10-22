using System.Diagnostics;
using DataChannelBandwidth;
using Microsoft.Extensions.Logging;
using SIPSorcery;
using SIPSorcery.Net;

ILoggerFactory logs = LoggerFactory.Create(
    builder => builder.AddFilter(level => level >= LogLevel.Error).AddConsole());

LogFactory.Set(logs);

var server = new RTCPeerConnection();
var client = new RTCPeerConnection();

long clientReceived = 0;
long serverReceived = 0;
double clientRate = 0;
double serverRate = 0;

client.ondatachannel += ch =>
{
    new Thread(() => SendRecv(ch, ref clientReceived, ref clientRate)) {
        IsBackground = true,
        Name = "client",
    }.Start();
};


var serverCH = await server.createDataChannel("test");
serverCH.onopen += () =>
{
    new Thread(() => SendRecv(serverCH, ref serverReceived, ref serverRate))
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

while (true)
{
    long recvC = Interlocked.Read(ref clientReceived);
    long recvS = Interlocked.Read(ref serverReceived);
    double rateC = Volatile.Read(ref clientRate);
    double rateS = Volatile.Read(ref serverRate);
    Console.WriteLine($"client: {rateC:F1}MB/s server: {rateS:F1}MB/s", recvC, recvS);
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

void SendRecv(RTCDataChannel channel, ref long received, ref double rate)
{
    var stream = new DataChannelStream(channel);
    var sender = new Thread(() => Send(channel))
    {
        IsBackground = true,
    };
    sender.Start();
    var stopwatch = Stopwatch.StartNew();
    byte[] buffer = new byte[200_000];
    while (true)
    {
        Interlocked.Add(ref received, stream.Read(buffer));
        Interlocked.Exchange(ref rate, received / 1024 / 1024 / stopwatch.Elapsed.TotalSeconds);
    }
}