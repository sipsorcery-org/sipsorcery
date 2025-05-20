//-----------------------------------------------------------------------------
// Filename: AudioStream.cs
//
// Description: Define an Audio media stream (which inherits MediaStream) to focus an Audio specific treatment
// The goal is to simplify RTPSession class
//
// Author(s):
// Christophe Irles
//
// History:
// 05 Apr 2022	Christophe Irles        Created (based on existing code from previous RTPSession class)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net;

public class AudioStream : MediaStream
{
    private const uint DEFAULT_AUDIO_SAMPLE_DURATION_MILLISECONDS = 20;

    protected static ILogger logger = Log.Logger;
    protected bool rtpEventInProgress;

    private bool sendingFormatFound;

    private bool _rtpPreviousTimestampSet;

    /// <summary>
    /// The RTP timestamp for the previously received RTP packet. Used to calculate the
    /// duration of the RTP packet in RTP timestamp units.
    /// </summary>
    private uint _rtpPreviousTimestamp;

    /// <summary>
    /// The audio format negotiated for the audio stream by the SDP offer/answer exchange.
    /// </summary>
    public SDPAudioVideoMediaFormat NegotiatedFormat { get; private set; }

    /// <summary>
    /// Gets fired when the remote SDP is received and the set of common audio formats is set.
    /// </summary>
    public event Action<int, List<AudioFormat>>? OnAudioFormatsNegotiatedByIndex;

    public event Action<EncodedAudioFrame>? OnAudioFrameReceived;

    /// <summary>
    /// Indicates whether this session is using audio.
    /// </summary>
    public bool HasAudio
    {
        get
        {
            return (LocalTrack is { } && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
              || (RemoteTrack is { } && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive);
        }
    }

    public AudioStream(RtpSessionConfig config, int index) : base(config, index)
    {
        MediaType = SDPMediaTypesEnum.audio;
    }

    /// <summary>
    /// Sends an audio sample to the remote peer.
    /// </summary>
    /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
    /// value is added to the previous RTP timestamp when building the RTP header.</param>
    /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
    public void SendAudio(uint durationRtpUnits, ReadOnlySpan<byte> sample)
    {
        if (!sendingFormatFound)
        {
            NegotiatedFormat = GetSendingFormat();
            sendingFormatFound = true;
        }
        SendAudioFrame(durationRtpUnits, NegotiatedFormat.ID, sample);
    }

    /// <summary>
    /// Sends an audio sample to the remote peer.
    /// </summary>
    /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
    /// value is added to the previous RTP timestamp when building the RTP header.</param>
    /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
    public void SendAudio(uint durationRtpUnits, byte[] sample)
    {
        SendAudio(durationRtpUnits, new ArraySegment<byte>(sample));
    }

    /// <summary>
    /// Sends an encoded audio frame to the remote peer.
    /// </summary>
    /// <param name="encodedAudioFrame">The encoded audio frame containing the audio data, format, and duration information.</param>
    public void SendAudio(EncodedAudioFrame encodedAudioFrame)
    {
        if (encodedAudioFrame?.AudioFormat is null || encodedAudioFrame.AudioFormat.IsEmpty())
        {
            throw new ArgumentException("EncodedAudioFrame must have a valid audio format.", nameof(encodedAudioFrame));
        }

        // Convert duration from milliseconds to RTP timestamp units manually
        // RTP timestamp units = milliseconds * (clock_rate / 1000)
        var durationRtpUnits = (uint)Math.Round(encodedAudioFrame.DurationMilliSeconds * encodedAudioFrame.AudioFormat.RtpClockRate / 1000.0);

        // Get the format ID for the audio format from our capabilities
        var format = GetSendingFormat(encodedAudioFrame.AudioFormat);
        if (format.IsEmpty())
        {
            throw new InvalidOperationException($"Audio format {encodedAudioFrame.AudioFormat.Codec} is not supported or negotiated for sending.");
        }

        // Temporarily store the current negotiated format and set it to the frame's format
        var previousNegotiatedFormat = NegotiatedFormat;
        var previousSendingFormatFound = sendingFormatFound;

        try
        {
            NegotiatedFormat = format;
            sendingFormatFound = true;

            // Use the existing SendAudio method with ReadOnlySpan<byte>
            SendAudio(durationRtpUnits, encodedAudioFrame.EncodedAudio.Span);
        }
        finally
        {
            // Restore the previous state
            NegotiatedFormat = previousNegotiatedFormat;
            sendingFormatFound = previousSendingFormatFound;
        }
    }

    /// <summary>
    /// Attempts to get the sending format that matches the specified audio format.
    /// </summary>
    /// <param name="audioFormat">The audio format to find a match for.</param>
    /// <returns>The compatible SDP media format for the specified audio format.</returns>
    private SDPAudioVideoMediaFormat GetSendingFormat(AudioFormat audioFormat)
    {
        if (LocalTrack is null && RemoteTrack is null)
        {
            throw new SipSorceryException($"Cannot get the {MediaType} sending format, missing both local and remote {MediaType} track.");
        }

        var capabilities = (LocalTrack?.Capabilities ?? RemoteTrack?.Capabilities) ?? throw new SipSorceryException($"Cannot get the {MediaType} sending format, no capabilities available.");

        // Find a format that matches the audio format
        foreach (var capability in capabilities)
        {
            var capabilityAudioFormat = capability.ToAudioFormat();
            if (!capabilityAudioFormat.IsEmpty() && capabilityAudioFormat.Codec == audioFormat.Codec)
            {
                // Check if clock rates match (if specified)
                if (audioFormat.ClockRate == 0 || capabilityAudioFormat.ClockRate == audioFormat.ClockRate)
                {
                    return capability;
                }
            }
        }

        return SDPAudioVideoMediaFormat.Empty;
    }

    /// <summary>
    /// Sends an audio packet to the remote party.
    /// </summary>
    /// <param name="duration">The duration of the audio payload in timestamp units. This value
    /// gets added onto the timestamp being set in the RTP header.</param>
    /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
    /// <param name="sample">The audio payload to send.</param>
    public void SendAudioFrame(uint duration, int payloadTypeID, ReadOnlySpan<byte> sample)
    {
        if (CheckIfCanSendRtpRaw())
        {
            if (rtpEventInProgress)
            {
                //logger.LogWarning(nameof(SendAudioFrame) + " an RTPEvent is in progress.");
                return;
            }

            try
            {
                // Basic RTP audio formats (such as G711, G722) do not have a concept of frames. The payload of the RTP packet is
                // considered a single frame. This results in a problem is the audio frame being sent is larger than the MTU. In 
                // that case the audio frame must be split across mutliple RTP packets. Unlike video frames there's no way to 
                // indicate that a series of RTP packets are correlated to the same timestamp. For that reason if an audio buffer
                // is supplied that's larger than MTU it will be split and the timestamp will be adjusted to best fit each RTP 
                // payload.
                // See https://github.com/sipsorcery/sipsorcery/issues/394.

                var maxPayload = RTPSession.RTP_MAX_PAYLOAD;
                var totalPackets = (sample.Length + maxPayload - 1) / maxPayload;

                uint totalIncrement = 0;
                Debug.Assert(LocalTrack is { });
                var startTimestamp = LocalTrack.Timestamp; // Keep track of where we started.

                for (var index = 0; index < totalPackets; index++)
                {
                    var offset = index * maxPayload;
                    var payloadLength = Math.Min(maxPayload, sample.Length - offset);

                    var fraction = (double)payloadLength / sample.Length;
                    var packetDuration = (uint)Math.Round(fraction * duration);

                    // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                    // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                    // in a frame.
                    var markerBit = 0;

                    // Send this packet at the current LocalTrack.Timestamp
                    SendRtpRaw(sample.Slice(offset, payloadLength), LocalTrack.Timestamp, markerBit, payloadTypeID, true);

                    // After sending, increment the timestamp by this packet's portion.
                    // This ensures the timestamp increments for the next packet, including the first one.
                    LocalTrack.Timestamp += packetDuration;
                    totalIncrement += packetDuration;
                }

                // After all packets are sent, correct if we haven't incremented exactly by `duration`.
                if (totalIncrement != duration)
                {
                    // Add or subtract the difference so total increment equals duration.
                    LocalTrack.Timestamp += (duration - totalIncrement);
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogRtpSocketExceptionSendAudioFrame(sockExcp.Message, sockExcp);
            }
        }
    }

    /// <summary>
    /// Sends an audio packet to the remote party.
    /// </summary>
    /// <param name="duration">The duration of the audio payload in timestamp units. This value
    /// gets added onto the timestamp being set in the RTP header.</param>
    /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
    /// <param name="buffer">The audio payload to send.</param>
    public void SendAudioFrame(uint duration, int payloadTypeID, byte[] buffer)
    {
        SendAudioFrame(duration, payloadTypeID, new ArraySegment<byte>(buffer));
    }

    /// <summary>
    /// Sends an RTP event for a DTMF tone as per RFC2833. Sending the event requires multiple packets to be sent.
    /// This method will hold onto the socket until all the packets required for the event have been sent. The send
    /// can be cancelled using the cancellation token.
    /// </summary>
    /// <param name="rtpEvent">The RTP event to send.</param>
    /// <param name="cancellationToken">CancellationToken to allow the operation to be cancelled prematurely.</param>
    /// <param name="clockRate">To send an RTP event the clock rate of the underlying stream needs to be known.</param>
    /// <param name="samplePeriod">The sample period in milliseconds being used for the media stream that the event 
    /// is being inserted into. Should be set to 50ms if main media stream is dynamic or sample period is unknown.</param>
    public async Task SendDtmfEvent(RTPEvent rtpEvent, CancellationToken cancellationToken, int clockRate = RTPSession.DEFAULT_AUDIO_CLOCK_RATE, int samplePeriod = RTPSession.RTP_EVENT_DEFAULT_SAMPLE_PERIOD_MS)
    {
        if (CheckIfCanSendRtpRaw())
        {
            if (rtpEventInProgress)
            {
                logger.LogDtmfEventInProgress();
                return;
            }

            try
            {
                rtpEventInProgress = true;

                // The RTP timestamp step corresponding to the sampling period. This can change depending
                // on the codec being used. For example using PCMU with a sampling frequency of 8000Hz and a sample period of 50ms
                // the timestamp step is 400 (8000 / (1000 / 50)). For a sample period of 20ms it's 160 (8000 / (1000 / 20)).
                var rtpTimestampStep = (ushort)(clockRate * samplePeriod / 1000);
                // If only the minimum number of packets are being sent then they are both the start and end of the event.
                rtpEvent.EndOfEvent = (rtpEvent.TotalDuration <= rtpTimestampStep);
                // The DTMF tone is generally multiple RTP events. Each event has a duration of the RTP timestamp step.
                rtpEvent.Duration = rtpTimestampStep;

                SendStartOfEventPackets(rtpEvent, cancellationToken);

                await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);

                if (!rtpEvent.EndOfEvent)
                {
                    // Send the progressive event packets 
                    while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                    {
                        SendProgressiveEventPacket(rtpEvent, rtpTimestampStep);

                        await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);
                    }

                    SendEndOfEventPackets(rtpEvent, cancellationToken);
                }

                Debug.Assert(LocalTrack is { });
                LocalTrack.Timestamp += rtpEvent.TotalDuration;
            }
            catch (SocketException sockExcp)
            {
                logger.LogRtpSocketExceptionSendDtmfEvent(sockExcp.Message, sockExcp);
            }
            catch (TaskCanceledException)
            {
                logger.LogDtmfEventCancelled();
            }
            finally
            {
                rtpEventInProgress = false;
            }
        }

        void SendStartOfEventPackets(RTPEvent rtpEvent, CancellationToken cancellationToken)
        {
            Span<byte> buffer = stackalloc byte[RTPEvent.DTMF_PACKET_LENGTH];
            for (var i = 0; i < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; i++)
            {
                rtpEvent.WriteEventPayload(buffer);
                var markerBit = (i == 0) ? 1 : 0;
                Debug.Assert(LocalTrack is { });
                SendRtpRaw(buffer, LocalTrack.Timestamp, markerBit, rtpEvent.PayloadTypeID, true);
            }
        }

        void SendProgressiveEventPacket(RTPEvent rtpEvent, ushort rtpTimestampStep)
        {
            Span<byte> buffer = stackalloc byte[RTPEvent.DTMF_PACKET_LENGTH];
            rtpEvent.Duration += rtpTimestampStep;
            rtpEvent.WriteEventPayload(buffer);
            Debug.Assert(LocalTrack is { });
            SendRtpRaw(buffer, LocalTrack.Timestamp, 0, rtpEvent.PayloadTypeID, true);
        }

        void SendEndOfEventPackets(RTPEvent rtpEvent, CancellationToken cancellationToken)
        {
            Span<byte> buffer = stackalloc byte[RTPEvent.DTMF_PACKET_LENGTH];
            for (var j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
            {
                rtpEvent.EndOfEvent = true;
                rtpEvent.Duration = rtpEvent.TotalDuration;
                rtpEvent.WriteEventPayload(buffer);
                Debug.Assert(LocalTrack is { });
                SendRtpRaw(buffer, LocalTrack.Timestamp, 0, rtpEvent.PayloadTypeID, true);
            }
        }
    }

    /// <summary>
    /// Sends a DTMF tone as an RTP event to the remote party.
    /// </summary>
    /// <param name="key">The DTMF tone to send.</param>
    /// <param name="ct">RTP events can span multiple RTP packets. This token can
    /// be used to cancel the send.</param>
    public virtual Task SendDtmf(byte key, CancellationToken ct)
    {
        var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, NegotiatedRtpEventPayloadID);
        return SendDtmfEvent(dtmfEvent, ct);
    }

    public void CheckAudioFormatsNegotiation()
    {
        if (OnAudioFormatsNegotiatedByIndex is not { } onAudioFormatsNegotiatedByIndex
            || LocalTrack?.Capabilities is not { Count: > 0 } capabilities)
        {
            return;
        }

        var audioFormats = new List<AudioFormat>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            var name = capability.Name();
            if (!string.Equals(name, SDP.TELEPHONE_EVENT_ATTRIBUTE, StringComparison.CurrentCultureIgnoreCase))
            {
                audioFormats.Add(capability.ToAudioFormat());
            }
        }

        if (audioFormats.Count == 0)
        {
            return;
        }

        onAudioFormatsNegotiatedByIndex(Index, audioFormats);
    }

    protected override void ProcessRtpPacket(IPEndPoint remoteEndPoint, RTPPacket rtpPacket, SDPAudioVideoMediaFormat format)
    {
        var audioFormat = format.ToAudioFormat();

        if (!audioFormat.IsEmpty())
        {
            var durationMilliseconds = _rtpPreviousTimestampSet switch
            {
                true => RtpTimestampExtensions.ToDurationMillisecondsInt(rtpPacket.Header.GetTimestampDelta(_rtpPreviousTimestamp), audioFormat.RtpClockRate),
                false => DEFAULT_AUDIO_SAMPLE_DURATION_MILLISECONDS
            };

            OnAudioFrameReceived?.Invoke(new EncodedAudioFrame(Index, format.ToAudioFormat(), durationMilliseconds, rtpPacket.Payload));
        }

        RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);

        _rtpPreviousTimestamp = rtpPacket.Header.Timestamp;

        if (!_rtpPreviousTimestampSet)
        {
            _rtpPreviousTimestampSet = true;
        }
    }
}
