using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    public class TextStream : MediaStream
    {
        protected static readonly ILogger logger = LogFactory.CreateLogger<TextStream>();

        /// <summary>
        /// A gap in sending longer than this is treated as an idle period, after which the marker
        /// bit is set on the next packet (RFC 4103 section 3). Senders transmit every 300ms, at most
        /// 500ms, while there is text or redundancy to send.
        /// </summary>
        private const int IDLE_PERIOD_MILLISECONDS = 1000;

        private DateTime _lastSendTime = DateTime.MinValue;

        protected bool rtpEventInProgress = false;

        private bool sendingFormatFound = false;

        /// <summary>
        /// The text format negotiated fir the text stream by the SDP offer/answer exchange.
        /// </summary>
        public SDPAudioVideoMediaFormat NegotiatedFormat { get; private set; }

        public Action<int, List<TextFormat>> OnTextFormatsNegotiatedByIndex { get; internal set; }

        /// <summary>
        /// Indicates whether this session is using text.
        /// </summary>
        public bool HasText
        {
            get
            {
                return LocalTrack != null && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  || RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }

        public void SendText(byte[] sample)
        {
            if (!sendingFormatFound)
            {
                NegotiatedFormat = GetSendingFormat();
                sendingFormatFound = true;
            }
            SendTextFrame(NegotiatedFormat.ID, sample);
        }

        private void SendTextFrame(int payloadTypeID, byte[] buffer)
        {
            if (CheckIfCanSendRtpRaw())
            {
                if (rtpEventInProgress)
                {
                    logger.LogWarning("An RTPEvent is in progress.");
                    return;
                }

                try
                {
                    // Get the current time
                    DateTime currentTime = DateTime.UtcNow;

                    // Calculate time elapsed since the last frame in milliseconds
                    uint elapsedMilliseconds = 0;

                    if (_lastSendTime != DateTime.MinValue)
                    {
                        elapsedMilliseconds = (uint)(currentTime - _lastSendTime).TotalMilliseconds;
                    }

                    // Update the timestamp with elapsed time
                    LocalTrack.Timestamp += elapsedMilliseconds;

                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = (index == 0) ? 0 : (index * RTPSession.RTP_MAX_PAYLOAD);
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;

                        // Set the marker bit for the first packet after idle or session start
                        int markerBit = index == 0 && (_lastSendTime == DateTime.MinValue || elapsedMilliseconds > IDLE_PERIOD_MILLISECONDS) ? 1 : 0;

                        byte[] payload = new byte[payloadLength];
                        Buffer.BlockCopy(buffer, offset, payload, 0, payloadLength);

                        // Send the RTP packet with the updated timestamp
                        SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    }

                    // Update the last send time
                    _lastSendTime = currentTime;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendT140Frame. {ErrorMessage}", sockExcp.Message);
                }
            }
        }

        public void CheckTextFormatsNegotiation()
        {
            if (LocalTrack != null && LocalTrack.Capabilities?.Count > 0)
            {
                OnTextFormatsNegotiatedByIndex?.Invoke(
                            Index,
                            LocalTrack.Capabilities
                            .Select(x => x.ToTextFormat()).ToList());
            }
        }

        public TextStream(RtpSessionConfig config, int index) : base(config, index)
        {
            MediaType = SDPMediaTypesEnum.text;
        }
    }
}
