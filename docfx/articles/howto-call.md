## How to Place a Call

The [GetStarted](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStarted/Program.cs) contains an example of how to place a SIP call from a Console application.

The key snippet of the code is shown below with an explanation afterwards.

````csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

string DESTINATION = "time@sipsorcery.com";

var sipTransport = new SIPTransport();
var userAgent = new SIPUserAgent(sipTransport, null);
var rtpSession = new RtpAVSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);

// Place the call and wait for the result.
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

if(callResult)
{
    // Start the audio capture and playback.
    rtpSession.Start();
    Console.WriteLine("Call attempt successful.");
}
else
{
    Console.WriteLine("Call attempt failed.");
}
````

### Explanation

The first step is to create a [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) to allocate a transport layer that can be used to send and receive SIP requests and responses. The [SIPTransport](xref:SIPSorcery.SIP.SIPTransport) class supports a number of different protocols and is described in this [article](transport.md)

````csharp
var sipTransport = new SIPTransport();
````

Once the SIP transport layer is available a [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) (which is capable of acting as both a client and server user agent) can be instantiated. It can be used to place and manage calls and is described further in this [article](sipuseragent.md).

````csharp
var userAgent = new SIPUserAgent(sipTransport, null);
````

The [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) can handle the signalling to set up a call but it cannot deal with the RTP media packets. To deal with them an [RTPSession](xref:SIPSorcery.Net.RTPSession) is needed. The specific type of session created here is an [RtpAVSession](https://github.com/sipsorcery/sipsorcery/blob/master/examples/SIPSorcery.RtpAVSession/RtpAVSession.cs) which is a Windows specific example of how The [RTPSession](xref:SIPSorcery.Net.RTPSession) class can be connected to audio and video devices.

````csharp
var rtpSession = new RtpAVSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);
````

Once the SIP and RTP instances are ready a call can be placed.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
````

If the call succeeds then a voice should announce the time on your default system speaker.

