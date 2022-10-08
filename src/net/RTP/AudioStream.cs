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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.net.RTP
{
    public class AudioStream : MediaStream
    {
        protected static ILogger logger = Log.Logger;

        protected Boolean rtpEventInProgress = false;

        #region EVENTS

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common audio formats is set.
        /// </summary>
        public event Action<int, List<AudioFormat>> OnAudioFormatsNegotiatedByIndex;

        #endregion EVENTS

        #region PROPERTIES

        /// <summary>
        /// Indicates whether this session is using audio.
        /// </summary>
        public bool HasAudio
        {
            get
            {
                return LocalTrack != null && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive
                  && RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;
            }
        }

        #endregion PROPERTIES

        #region SEND PACKET

        /// <summary>
        /// Sends an audio sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the audio sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The audio sample to set as the RTP packet payload.</param>
        public void SendAudio(uint durationRtpUnits, byte[] sample)
        {
            var audioFormat = GetSendingFormat();
            SendAudioFrame(durationRtpUnits, audioFormat.ID, sample);
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
            if (CheckIfCanSendRtpRaw())
            {
                if (rtpEventInProgress)
                {
                    logger.LogWarning($"A RTPEvent is in progress.");
                    return;
                }

                try
                {
                    // Basic RTP audio formats (such as G711, G722) do not have a concept of frames. The payload of the RTP packet is
                    // considered a single frame. This results in a problem is the audio frame being sent is larger than the MTU. In 
                    // that case the audio frame must be split across mutliple RTP packets. Unlike video frames theres no way to 
                    // indicate that a series of RTP packets are correlated to the same timestamp. For that reason if an audio buffer
                    // is supplied that's larger than MTU it will be split and the timestamp will be adjusted to best fit each RTP 
                    // paylaod.
                    // See https://github.com/sipsorcery/sipsorcery/issues/394.

                    uint payloadDuration = 0;

                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = (index == 0) ? 0 : (index * RTPSession.RTP_MAX_PAYLOAD);
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;
                        LocalTrack.Timestamp += payloadDuration;
                        byte[] payload = new byte[payloadLength];

                        Buffer.BlockCopy(buffer, offset, payload, 0, payloadLength);

                        // RFC3551 specifies that for audio the marker bit should always be 0 except for when returning
                        // from silence suppression. For video the marker bit DOES get set to 1 for the last packet
                        // in a frame.
                        int markerBit = 0;

                        SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                        //logger.LogDebug($"send audio { audioRtpChannel.RTPLocalEndPoint}->{AudioDestinationEndPoint}.");

                        payloadDuration = (uint)(((decimal)payloadLength / buffer.Length) * duration); // Get the percentage duration of this payload.
                    }

                    LocalTrack.Timestamp += duration;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError("SocketException SendAudioFrame. " + sockExcp.Message);
                }
            }
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
                    logger.LogWarning($"A RTPEvent is in progress.");
                    return;
                }

                try
                {
                    rtpEventInProgress = true;
                    // The RTP timestamp step corresponding to the sampling period. This can change depending
                    // on the codec being used. For example using PCMU with a sampling frequency of 8000Hz and a sample period of 50ms
                    // the timestamp step is 400 (8000 / (1000 / 50)). For a sample period of 20ms it's 160 (8000 / (1000 / 20)).
                    ushort rtpTimestampStep = (ushort)(clockRate * samplePeriod / 1000);

                    // If only the minimum number of packets are being sent then they are both the start and end of the event.
                    rtpEvent.EndOfEvent = (rtpEvent.TotalDuration <= rtpTimestampStep);
                    // The DTMF tone is generally multiple RTP events. Each event has a duration of the RTP timestamp step.
                    rtpEvent.Duration = rtpTimestampStep;

                    // Send the start of event packets.
                    for (int i = 0; i < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; i++)
                    {
                        byte[] buffer = rtpEvent.GetEventPayload();

                        int markerBit = (i == 0) ? 1 : 0;  // Set marker bit for the first packet in the event.

                        SendRtpRaw(buffer, LocalTrack.Timestamp, markerBit, rtpEvent.PayloadTypeID, true);
                    }

                    await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);

                    if (!rtpEvent.EndOfEvent)
                    {
                        // Send the progressive event packets 
                        while ((rtpEvent.Duration + rtpTimestampStep) < rtpEvent.TotalDuration && !cancellationToken.IsCancellationRequested)
                        {
                            rtpEvent.Duration += rtpTimestampStep;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpRaw(buffer, LocalTrack.Timestamp, 0, rtpEvent.PayloadTypeID, true);

                            await Task.Delay(samplePeriod, cancellationToken).ConfigureAwait(false);
                        }

                        // Send the end of event packets.
                        for (int j = 0; j < RTPEvent.DUPLICATE_COUNT && !cancellationToken.IsCancellationRequested; j++)
                        {
                            rtpEvent.EndOfEvent = true;
                            rtpEvent.Duration = rtpEvent.TotalDuration;
                            byte[] buffer = rtpEvent.GetEventPayload();

                            SendRtpRaw(buffer, LocalTrack.Timestamp, 0, rtpEvent.PayloadTypeID, true);
                        }
                    }
                    LocalTrack.Timestamp += rtpEvent.TotalDuration;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError("SocketException SendDtmfEvent. " + sockExcp.Message);
                }
                catch (TaskCanceledException)
                {
                    logger.LogWarning("SendDtmfEvent was cancelled by caller.");
                }
                finally
                {
                    rtpEventInProgress = false;
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
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        #endregion SEND PACKET

        public void CheckAudioFormatsNegotiation()
        {
            if (LocalTrack != null &&
                        LocalTrack.Capabilities.Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE).Count() > 0)
            {
                OnAudioFormatsNegotiatedByIndex?.Invoke(
                            Index,
                            LocalTrack.Capabilities
                            .Where(x => x.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE)
                            .Select(x => x.ToAudioFormat()).ToList());
            }
        }

        public AudioStream(RtpSessionConfig config, int index) : base(config, index)
        {
            MediaType = SDPMediaTypesEnum.audio;
        }
    }
}
