## SIP User Agent

The @"SIPSorcery.SIP.App.SIPUserAgent" class is the highest level abstraction for dealing with SIP user agent client and server operations. It provides an interface that aims to make dealing with common signalling functions - such as making a call, putting the remote party on hold, hanging up and more - as easy as possible.

For non-server SIP applications the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) class will often be the main SIP related class that needs to be used.

### Usage

#### Initiating a Call

To place a SIP call takes only a small amount of code. The code snippet below is capable of successfully completing a call (it requires the [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media) nuget package for Windows audio/video support. The full example be found in [Getting Started Example](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStarted/Program.cs).

````csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;

string DESTINATION = "time@sipsorcery.com";

var sipTransport = new SIPTransport();
var userAgent = new SIPUserAgent(sipTransport, null);
var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, null);

bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
````

#### Accepting and Answering Calls

As well as initiating calls the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) class can also accept and subsequently answer an incoming call. Accepting a call lets the caller know their request has been received. A decision can then be made on whether to answer, reject or redirect the call.

````csharp
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

var sipTransport = new SIPTransport();
var userAgent = new SIPUserAgent(sipTransport, null);

// The invite request needs to be obtained from the sipTransport.SIPTransportRequestReceived event.
var incomingCall = userAgent.AcceptCall(inviteRequest);

// To answer the call and start the RTP media.
var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, null);
await userAgent.Answer(incomingCall, rtpSession);

// Or the incoming call request can be rejected.
// incomingCall.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);

// Or the incoming call request can be redirected.
// incomingCall.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
````

#### Established Call Actions

Once a call is established the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) instance can:

 - Hangup,
 - Place on and Off Hold,
 - Send DTMF Tones,
 - Blind Transfer, and
 - Attended Transfer.

#### Hanging Up

Hanging an established call up sends a SIP BYE request and closes the RTP session with the remote call party.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
userAgent.Hangup()
````

#### Sending DTMF Tones

A DTMF tone can be sent to the remote call party using the @"SIPSorcery.SIP.App.SIPUserAgent.SendDtmf(System.Byte)" method.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
await userAgent.SendDtmf(1);
await userAgent.SendDtmf(2);
await userAgent.SendDtmf(3);
````

#### Placing on and Off Hold

There are typically two mechanisms that are used to place a remote call party on hold:

 - Change the audio source from a capture device to music on hold. This approach does not require any SIP signalling but has the weakness that full audio streams continue to flow,
 - Use a SIP re-INVITE request to inform the remote call party that audio will no longer be sent by setting the media flow attribute from `sendrecv` to `sendonly` and then send a comfort noise or silence payload.

A 3rd mechanism is a combination of the two. A re-INVITE request is sent and the agent placing the call on hold streams music to the remote agent. This is the approach used in the @"SIPSorcery.SIP.App.SIPUserAgent.PutOnHold" and @"SIPSorcery.SIP.App.SIPUserAgent.TakeOffHold" methods.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
await userAgent.PutOnHold();
await userAgent.TakeOffHold();
````

#### Blind Transfer

A Blind Transfer is where the callee is sent a SIP REFER request (see [call flow](callholdtransfer.md#blind-transfer)) specifying a new destination for the call. The call party initiating the transfer does not interact with the transfer destination. The @"SIPSorcery.SIP.App.SIPUserAgent.BlindTransfer(SIPSorcery.SIP.SIPURI,System.TimeSpan,System.Threading.CancellationToken)" method is used to carry out a Blind Transfer on an established call.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

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
    Log.LogWarning($"Transfer to {TRANSFER_DESTINATION_SIP_URI} failed.");
}
````

#### Attended Transfer

An Attended Transfer is more complicated than a Blind Transfer and involves co-ordinating 3 call legs (see [call flow](attendedtransfer.md#call-flow)).

An Attended Transfer proceeds as follows:

 - The initial call is established,
 - The callee is placed on hold,
 - A second call to the transfer destination is established,
 - The original callee and the transferee are bridged together. The transferring call party has their call leg terminated.

 The @"SIPSorcery.SIP.App.SIPUserAgent.AttendedTransfer(SIPSorcery.SIP.SIPDialogue,System.TimeSpan,System.Threading.CancellationToken)" method is used to carry out an Attended Transfer on two established calls.

````csharp
bool callResult1 = await userAgent1.Call(DESTINATION, null, null, rtpSession);
await userAgent1.PutOnHold();

bool callResult2 = await userAgent2.Call(DESTINATION, null, null, rtpSession);

if (userAgent1.IsCallActive && userAgent2.IsCallActive)
{
    bool result = await userAgent2.AttendedTransfer(userAgent1.Dialogue, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
    if (!result)
    {
        Log.LogWarning($"Attended transfer failed.");
    }
}
else
{
    Log.LogWarning("There need to be two active calls before the attended transfer can occur.");
}
````
