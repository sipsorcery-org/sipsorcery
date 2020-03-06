## SIP User Agent

The [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) class is the highest level abstraction for dealing with SIP user agent client and server operations. It provides an interface that aims to make dealing with common signalling functions - such as making a call, putting the remote party on hold, hanging up and more - as easy as possible.

For non-server SIP applications the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) class will often be the main SIP related class that needs to be used.

### Usage

#### Initiating a Call

To place a SIP call takes only a small amount of code. The code snippet below is capable of successfully completing a call but it will not do anything with the RTP audio packets it receives. Capturing or rendering media takes additional code an example of which can be seen in the [Getting Started Example](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStarted/Program.cs).

````csharp
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

string DESTINATION = "time@sipsorcery.com";

var sipTransport = new SIPTransport();
var userAgent = new SIPUserAgent(sipTransport, null);
var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) , AddressFamily.InterNetwork);

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
var incomingCall = userAgent1.AcceptCall(inviteRequest);

// To answer the call and start the RTP media.
var rtpMediaSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);
await userAgent1.Answer(incomingCall, rtpMediaSession);

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

A DTMF tone can be sent to the remote call party using the `SendDTMF` method.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
userAgent.SendDTMF(1);
userAgent.SendDTMF(2);
userAgent.SendDTMF(3);
````

#### Placing on and Off Hold

There are typically two mechanisms that are used to place a remote call party on hold:

 - Change the audio source from a capture device to music on hold. This approach does not require any SIP signalling,
 - Use a SIP re-INVITE request to inform the remote call party that audio will no longer be sent by setting the media flow attribute from `sendrecv` to `sendonly` and then send a comfort noise or silence payload.

If the music on hold approach is used the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) instance does not need to get involved, the audio source can simply be switched.

To use the SIP re-INVITE approach the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) `PutOnHold` and `TakeOffHold` instance methods can be used.

````csharp
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
userAgent.PutOnHold();
userAgent.TakeOffHold();
````

#### Blind Transfer

A Blind Transfer is where the callee is sent a SIP REFER request (see [call flow](callholdtransfer.md#blind-transfer)) specifying a new destination for the call. The transferee does not interact with the transfer destination.

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

An Attend Transfer is more complicated than a Blind Transfer and involves co-ordinating 3 call legs (see [call flow](attendedtransfer.md#call-flow)).

An Attended Transfer proceeds as follows:

 - The initial call is established,
 - The callee is placed on hold,
 - A second call to the transfer destination is established,
 - The original callee and the transferee are bridged together. The transferring call party has their call leg terminated.

````csharp
bool callResult1 = await userAgent1.Call(DESTINATION, null, null, rtpSession);
userAgent1.PutOnHold();

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

