## Sending DTMF

The [SendDtmf](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SendDtmf) contains an example of how to send DTMF tones using RTP events as specified in [RFC2833](https://tools.ietf.org/html/rfc2833).

The send DTMF example is based on the [UserAgentClient example](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentClient). The difference is that the RTP stream from is now hard coded to send silence with 3 DTMF tones interspersed. The mechanism used to send the DTMF tones is to add them to a queue which is monitored by the thread generating the RTP stream.

The code that adds the DTMF events to the queue is shown below. The first byte in the `RTPEvent` constructor is the DTMF tone to send. The fourth parameter is the duration of the tone. The duration is not critical but is useful to allow multiple RTP event packets to be generated and minimise the likelihood of the event being lost on an unreliable transport such as UDP.

````csharp
// Add some DTMF events to the queue. These will be transmitted by the SendRtp thread.
_dtmfEvents.Enqueue(new RTPEvent(0x05, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
Task.Delay(2000, rtpCts.Token).Wait();
_dtmfEvents.Enqueue(new RTPEvent(0x09, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
Task.Delay(2000, rtpCts.Token).Wait();
_dtmfEvents.Enqueue(new RTPEvent(0x02, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
Task.Delay(2000, rtpCts.Token).Wait();
````

The `SendRtp` method shown below is what takes the events off the queue and hands them over to the `RTPSession` for transmitting. It's important to note that for the duration of the event it's the only thing being sent. The original media stream is interrupted. In this example that's not noticeable since the original media stream is silence.

````csharp
private static async void SendRtp(Socket rtpSocket, RTPSession rtpSendSession, CancellationTokenSource cts)
{
	int samplingFrequency = RTPPayloadTypes.GetSamplingFrequency(rtpSendSession.PayloadType);
	uint rtpTimestampStep = (uint)(samplingFrequency * SILENCE_SAMPLE_PERIOD / 1000);
	uint bufferSize = (uint)SILENCE_SAMPLE_PERIOD;
	uint rtpSendTimestamp = 0;
	uint packetSentCount = 0;
	uint bytesSentCount = 0;

	while (cts.IsCancellationRequested == false)
	{
		if (_remoteRtpEndPoint != null)
		{
			if (!_dtmfEvents.IsEmpty)
			{
				// Check if there are any DTMF events to send.
				_dtmfEvents.TryDequeue(out var rtpEvent);
				if(rtpEvent != null)
				{
					await rtpSendSession.SendDtmfEvent(rtpSocket, _remoteRtpEndPoint, rtpEvent, rtpSendTimestamp, (ushort)SILENCE_SAMPLE_PERIOD, (ushort)rtpTimestampStep, cts);
				}
				rtpSendTimestamp += rtpEvent.TotalDuration + rtpTimestampStep;
			}
			else
			{
				// If there are no DTMF events to send we'll send silence.

				byte[] sample = new byte[bufferSize / 2];
				int sampleIndex = 0;

				for (int index = 0; index < bufferSize; index += 2)
				{
					sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
					sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
				}

				rtpSendSession.SendAudioFrame(rtpSocket, _remoteRtpEndPoint, rtpSendTimestamp, sample);
				rtpSendTimestamp += rtpTimestampStep;
				packetSentCount++;
				bytesSentCount += (uint)sample.Length;
			}
		}

		await Task.Delay(SILENCE_SAMPLE_PERIOD);
	}
}
````

If you have an [Asterisk](https://www.asterisk.org/) server available a handy dialplan that has been tested with the send DTMF example program is shown below. If all goes well when you run the sample you should hear the DTMF digits read back to you.

````bash
exten => *63,1(start),Gotoif($[ "${LEN(${extensao})}" < "5"]?collect:bye)
exten => *63,n(collect),Read(digito,,1)
exten => *63,n,SayDigits(${digito})
exten => *63,n,Set(extensao=${extensao}${digito})
exten => *63,n,GoTo(start)
exten => *63,n(bye),Playback("vm-goodbye")
exten => *63,n,hangup()
````




