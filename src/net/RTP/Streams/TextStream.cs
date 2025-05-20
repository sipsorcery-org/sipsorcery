using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net;

public class TextStream : MediaStream
{
    protected static ILogger logger = Log.Logger;

    private DateTime _lastSendTime = DateTime.MinValue;

    protected bool rtpEventInProgress;

    private bool sendingFormatFound;

    /// <summary>
    /// The text format negotiated fir the text stream by the SDP offer/answer exchange.
    /// </summary>
    public SDPAudioVideoMediaFormat NegotiatedFormat { get; private set; }

    public Action<int, List<TextFormat>>? OnTextFormatsNegotiatedByIndex { get; internal set; }

    /// <summary>
    /// Indicates whether this session is using text.
    /// </summary>
    public bool HasText
    {
        get
        {
            return LocalTrack is { } && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
              || RemoteTrack is { } && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
        }
    }

    public void SendText(ReadOnlySpan<byte> sample)
    {
        if (!sendingFormatFound)
        {
            NegotiatedFormat = GetSendingFormat();
            sendingFormatFound = true;
        }
        SendTextFrame(NegotiatedFormat.ID, sample);
    }

    private void SendTextFrame(int payloadTypeID, ReadOnlySpan<byte> buffer)
    {
        if (CheckIfCanSendRtpRaw())
        {
            if (rtpEventInProgress)
            {
                logger.LogRtpEventInProgress();
                return;
            }

            try
            {
                // Get the current time
                var currentTime = DateTime.UtcNow;

                // Calculate time elapsed since the last frame in milliseconds
                uint elapsedMilliseconds = 0;

                if (_lastSendTime != DateTime.MinValue)
                {
                    elapsedMilliseconds = (uint)(currentTime - _lastSendTime).TotalMilliseconds;
                }

                // Update the timestamp with elapsed time
                Debug.Assert(LocalTrack is { });
                LocalTrack.Timestamp += elapsedMilliseconds;

                for (var index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    var offset = (index == 0) ? 0 : (index * RTPSession.RTP_MAX_PAYLOAD);
                    var payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;

                    // Set the marker bit for the first packet after idle or session start
                    var markerBit = _lastSendTime == DateTime.MinValue ? 1 : 0;

                    // Send the RTP packet with the updated timestamp
                    SendRtpRaw(buffer.Slice(offset, payloadLength), LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                }

                // Update the last send time
                _lastSendTime = currentTime;
            }
            catch (SocketException sockExcp)
            {
                logger.LogSendT140FrameSocketError(sockExcp.Message, sockExcp);
            }
        }
    }

    public void CheckTextFormatsNegotiation()
    {
        if (OnTextFormatsNegotiatedByIndex is not { } onTextFormatsNegotiatedByIndex
            || LocalTrack?.Capabilities is not { Count: > 0 } capabilities)
        {
            return;
        }

        var textFormats = new List<TextFormat>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            textFormats.Add(capability.ToTextFormat());
        }

        onTextFormatsNegotiatedByIndex(Index, textFormats);
    }

    public TextStream(RtpSessionConfig config, int index) : base(config, index)
    {
        MediaType = SDPMediaTypesEnum.text;
    }
}
