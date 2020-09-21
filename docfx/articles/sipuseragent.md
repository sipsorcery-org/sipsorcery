# SIP User Agent

The @"SIPSorcery.SIP.App.SIPUserAgent" class is the highest level abstraction for dealing with SIP user agent client and server operations. It aims to make dealing with common SIP functions - such as making a call, putting the remote party on hold, hanging up and more - as easy as possible.

For SIP client applications the @"SIPSorcery.SIP.App.SIPUserAgent" class will typically be the main class to use.

## Initiating a Call

To place a SIP call takes only a small amount of code. The code snippet below is capable of successfully completing a call. It requires two nuget packages:

 - [SIPSorcery](https://www.nuget.org/packages/SIPSorcery) - the core library that provides the real-time communications feature set.
 - [SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows) - a Windows specific library that provides access to the system audio and video devices.

 For non-Windows platforms an alternative is:

  - [SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg) - it is intended to provide the same capabilities as the Windows specific package for `Linux` and `macOS`. At the time of writing it is still a work in progress.

The full example for the code snippet below can be be found in [Getting Started Example](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPExamples/GetStarted).

````csharp
using System;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        static string DESTINATION = "time@sipsorcery.com";

        static async Task Main()
        {
            var userAgent = new SIPUserAgent();
            var winAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
            var voipMediaSession = new VoIPMediaSession(winAudioEndPoint.ToMediaEndPoints());

            bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }
    }
}
````

**Step 1:** Create a @"SIPSorcery.SIP.App.SIPUserAgent" to handle the SIP signaling.

````csharp
var userAgent = new SIPUserAgent();
````

**Step 2:** The `userAgent ` will be able to place a call and get exchange `RTP` media packets that contain audio and video payloads but it does not know what to do with them. The next line creates a Windows media end point that can obtain audio samples from a microphone and the reverse of playing received audio `RTP` samples on a speaker. For non-Windows platforms it is possible to use a different class as long as it implements the [IAudioSource](https://github.com/sipsorcery/SIPSorceryMedia.Abstractions/blob/a7fbd2e069ed3ca3925644ff80dd1ad8b47c5804/src/V1/MediaEndPoints.cs#L83) and [IAudioSink](https://github.com/sipsorcery/SIPSorceryMedia.Abstractions/blob/a7fbd2e069ed3ca3925644ff80dd1ad8b47c5804/src/V1/MediaEndPoints.cs#L112) interfaces.

````csharp
var winAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
````

**Step 3:** The `userAgent` and the `winAudioEndPoint` now need to be connected. It's possible to do this manually by adding media tracks, wiring up the different events and handlers etc. The @"SIPSorcery.Media.VoIPMediaSession" does this job automatically.

````csharp
var voipMediaSession = new VoIPMediaSession(winAudioEndPoint.ToMediaEndPoints());
````

**Step 4:** Finally place the call making sure to provide the media session object as a parameter.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
````

## Accepting and Answering Calls

As well as initiating calls the @"SIPSorcery.SIP.App.SIPUserAgent" class can also accept and subsequently answer an incoming call. Accepting a call lets the caller know their request has been received. A decision can then be made on whether to answer, reject or redirect the call.

The example below shows a minimal example of how to automatically answer an incoming call using the default Windows audio devices. 

````csharp
using System;
using System.Net;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        static void Main()
        {
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 5060)));
            var userAgent = new SIPUserAgent(sipTransport, null, true);

            userAgent.OnIncomingCall += async (ua, req) =>
            {
                Console.WriteLine($"Incoming call from {req.RemoteSIPEndPoint}.");
                var uas = userAgent.AcceptCall(req);

                WindowsAudioEndPoint winAudioEP = new WindowsAudioEndPoint(new AudioEncoder());
                VoIPMediaSession voipMediaSession = new VoIPMediaSession(winAudioEP.ToMediaEndPoints());
                voipMediaSession.AcceptRtpFromAny = true;

                await userAgent.Answer(uas, voipMediaSession);
            };

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();

            sipTransport.Shutdown();
        }
    }
}
````

**Step 1:** Create a @"SIPSorcery.SIP.SIPTransport" instance and add a @"SIPSorcery.SIP.SIPChannel" to it. This step is generally required if the application needs to listen on a specific protocol, address or port. This is relevant for incoming calls since the caller needs to know how to reach the application.

````csharp
var sipTransport = new SIPTransport();
sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 5060)));
````

**Step 2:** Create a @"SIPSorcery.SIP.App.SIPUserAgent" using the `SIPTransport` instance. The `SIPUserAgent` class will monitor the transport for incoming call requests.

````csharp
var userAgent = new SIPUserAgent(sipTransport, null, true);
````

**Step 3:** Create an event handler for incoming calls received.

````csharp
userAgent.OnIncomingCall += async (ua, req) =>
{
    // Incoming call event handling logic.
}
````

**Step 4:** When an incoming call is received the first action to take is to accept it (note that accepted means the answer process has started rather than actually answering). Once a call is accepted different various actions can be taken such as:

 - Display a prompt on the user interface and allow the User to choose an action,
 - Automatically reject the call,
 - Automatically answer the call,
 - Forward teh call to an alternative destination.

````csharp
var uas = userAgent.AcceptCall(req);
````

**Step 5:** To answer the call create a `WindowsAudioEndPoint` and @"SIPSorcery.Media.VoIPMediaSession" to interface with the system audio devices and decode any received `RTP` packets. See the [#Initiating-a-Call] section for more detail. 

````csharp
WindowsAudioEndPoint winAudioEP = new WindowsAudioEndPoint(new AudioEncoder());
VoIPMediaSession voipMediaSession = new VoIPMediaSession(winAudioEP.ToMediaEndPoints());
voipMediaSession.AcceptRtpFromAny = true;
````

**Step 6:** Finally answer the call.

````csharp
await userAgent.Answer(uas, voipMediaSession);
````

## Established Call Actions

Once a call is established the @"SIPSorcery.SIP.App.SIPUserAgent" instance can perform various additional actions such as:

 - Hangup,
 - Place on and Off Hold,
 - Send and receive DTMF Tones,
 - Blind Transfer, and
 - Attended Transfer.

## Hanging Up

Hanging an established call up sends a SIP BYE request and closes the RTP session with the remote call party.

````csharp
userAgent.Hangup()
````

## DTMF Tones

A DTMF tone can be sent to the remote call party using the @"SIPSorcery.SIP.App.SIPUserAgent.SendDtmf(System.Byte)" method.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
await userAgent.SendDtmf(1);
await userAgent.SendDtmf(2);
await userAgent.SendDtmf(3);
````

To receive DTMF tones add an event handler for the @"SIPSorcery.SIP.App.SIPUserAgent.OnDtmfTone" event.

````csharp
ua.OnDtmfTone += (byte tone, int duration) => Console.WriteLine($"DTMF tone received {tone}, duration {duration}ms.");
````

## Placing on and Off Hold

There are typically two mechanisms that are used to place a remote call party on hold:

 - Change the audio source from a capture device to music on hold. This approach does not require any SIP signalling but has the weakness that full audio streams continue to flow,
 - Use a SIP re-INVITE request to inform the remote call party that audio will no longer be sent by setting the media flow attribute from `sendrecv` to `sendonly` and then send a comfort noise or silence payload.

A 3rd mechanism is a combination of the two. A re-INVITE request is sent and the agent placing the call on hold streams music to the remote agent. This is the approach used in the @"SIPSorcery.SIP.App.SIPUserAgent.PutOnHold" and @"SIPSorcery.SIP.App.SIPUserAgent.TakeOffHold" methods.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
await userAgent.PutOnHold();
await userAgent.TakeOffHold();
````

## Blind Transfer

A Blind Transfer is where the callee is sent a SIP REFER request (see [call flow](callholdtransfer.md#blind-transfer)) specifying a new destination for the call. The call party initiating the transfer does not interact with the transfer destination. The @"SIPSorcery.SIP.App.SIPUserAgent.BlindTransfer(SIPSorcery.SIP.SIPURI,System.TimeSpan,System.Threading.CancellationToken)" method is used to carry out a Blind Transfer on an established call.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);

var transferURI = SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI);
bool result = await userAgent.BlindTransfer(transferURI, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
if (result)
{
    // If the transfer was accepted the original call will already have been hungup.
    // Wait a second for the transfer NOTIFY request to arrive.
    await Task.Delay(1000);
}
else
{
    Console.WriteLine($"Transfer to {TRANSFER_DESTINATION_SIP_URI} failed.");
}
````

## Attended Transfer

An Attended Transfer is more complicated than a Blind Transfer as it involves coordinating 3 call legs (see [call flow](attendedtransfer.md#call-flow)).

An Attended Transfer proceeds as follows:

 - The initial call is established,
 - The callee is placed on hold,
 - A second call to the transfer destination is established,
 - The original callee and the transferee are bridged together. The transferring call party has their call leg terminated.

 The @"SIPSorcery.SIP.App.SIPUserAgent.AttendedTransfer(SIPSorcery.SIP.SIPDialogue,System.TimeSpan,System.Threading.CancellationToken)" method is used to carry out an Attended Transfer on two established calls.

````csharp
bool callResult1 = await userAgent1.Call(DESTINATION, null, null, voipMediaSession1);
await userAgent1.PutOnHold();

bool callResult2 = await userAgent2.Call(DESTINATION, null, null, voipMediaSession2);

if (userAgent1.IsCallActive && userAgent2.IsCallActive)
{
    bool result = await userAgent2.AttendedTransfer(userAgent1.Dialogue, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
    if (!result)
    {
        Console.WriteLine($"Attended transfer failed.");
    }
}
else
{
    Console.WriteLine("There need to be two active calls before the attended transfer can occur.");
}
````
