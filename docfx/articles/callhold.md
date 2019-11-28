## Call Hold

The [CallHold](https://github.com/sipsorcery/sipsorcery/tree/master/examples/CallHold) program contains an example of how to place an established call on and off hold.

There are a number of different ways to put a SIP call on hold. This example uses SIP re-INVITE requests with the RTP flow attribute modified to indicate the call hold status.

The majority of the code in the example is to set up the initial the call. The interesting pieces of code as far as putting the call on hold arein the `ReinviteRequestReceived` method and in the `Task` handling the key presses ('h' is used to put the remote party on hold). Those two blocks are shown below.

````csharp
/// <summary>
/// Event handler for receiving a re-INVITE request on an established call.
/// In call requests can be used for multitude of different purposes. In this  
/// example program we're only concerned with re-INVITE requests being used 
/// to place a call on/off hold.
/// </summary>
/// <param name="uasTransaction">The user agent server invite transaction that
/// was created for the request. It needs to be used for sending responses 
/// to ensure reliable delivery.</param>
private static void ReinviteRequestReceived(UASInviteTransaction uasTransaction)
{
	SIPRequest reinviteRequest = uasTransaction.TransactionRequest;

	// Re-INVITEs can also be changing the RTP end point. We can update this each time.
	IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(reinviteRequest.Body);
	_remoteRtpEndPoint = dstRtpEndPoint;

	// If the RTP callfow attribute has changed it's most likely due to being placed on/off hold.
	SDP newSDP = SDP.ParseSDPDescription(reinviteRequest.Body);
	if (GetRTPStatusAttribute(newSDP) == RTP_ATTRIBUTE_SENDONLY)
	{
		Log.LogInformation("Remote call party has placed us on hold.");
		_holdStatus = HoldStatus.RemotePutOnHold;

		_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_RECVONLY);
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
	else if (GetRTPStatusAttribute(newSDP) == RTP_ATTRIBUTE_SENDRECV && _holdStatus != HoldStatus.None)
	{
		Log.LogInformation("Remote call party has taken us off hold.");
		_holdStatus = HoldStatus.None;

		_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
	else
	{
		Log.LogWarning("Not sure what the remote call party wants us to do...");

		// We'll just reply Ok and hope eveything is good.
		var okResponse = SIPTransport.GetResponse(reinviteRequest, SIPResponseStatusCodesEnum.Ok, null);
		okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
		okResponse.Body = _ourSDP.ToString();
		uasTransaction.SendFinalResponse(okResponse);
	}
}
````

The task handling user key presses is shown below.

````csharp

// At this point the call has been initiated and everything will be handled in an event handler.
Task.Run(() =>
{
	try
	{
		while (!exitCts.Token.WaitHandle.WaitOne(0))
		{
			var keyProps = Console.ReadKey();
			if (keyProps.KeyChar == 'h')
			{
				// Place call on/off hold.
				if (userAgent.IsAnswered)
				{
					if (_holdStatus == HoldStatus.None)
					{
						Log.LogInformation("Placing the remote call party on hold.");
						_holdStatus = HoldStatus.WePutOnHold;
						_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDONLY);
						userAgent.SendReInviteRequest(_ourSDP);
					}
					else if (_holdStatus == HoldStatus.WePutOnHold)
					{
						Log.LogInformation("Removing the remote call party from hold.");
						_holdStatus = HoldStatus.None;
						_ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
						userAgent.SendReInviteRequest(_ourSDP);
					}
					else
					{
						Log.LogInformation("Sorry we're already on hold by the remote call party.");
					}
				}
			}
			else if (keyProps.KeyChar == 'q')
			{
				// Quit application.
				exitCts.Cancel();
			}
		}
	}
	catch (Exception excp)
	{
		SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
	}
});
````

In the case of the `ReinviteRequestReceived` the remote party is placing the call on and off hold. In the second case handlng it's the example program putting the call on and off hold.

Each call hold is done by changing a single attribute on the SDP and sending it to the remote party via a re-INVITE request.

For example the original SDP sent to establish the call will look something like the payload below. The important attribute is the last one `a=sendrecv`.

````
v=0
o=- 49809 0 IN IP4 192.168.11.50
s=sipsorcery
c=IN IP4 192.168.11.50
t=0 0
m=audio 48000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv
````

The remote call party can be put on hold using.

````
v=0
o=- 49735 0 IN IP4 192.168.11.50
s=sipsorcery
c=IN IP4 192.168.11.50
t=0 0
m=audio 48000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendonly
````

They will respond with an SDP payload along the lines of what's shown below. Again the line changing is the last attribute `a=recvonly`.

````
v=0
o=- 1667266393 3 IN IP4 192.168.11.50
s=Bria 4 release 4.8.1 stamp 84929
c=IN IP4 192.168.11.50
t=0 0
m=audio 59228 RTP/AVP 0
a=recvonly
````

To take the call off hold it's a matter of setting the RTP flow attribute back to `sendrecv` as shown in the original SDP.




