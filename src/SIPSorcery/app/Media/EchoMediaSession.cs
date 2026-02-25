//-----------------------------------------------------------------------------
// Filename: EchoMediaSession.cs
//
// Description:  A media session that can be used for diagnostics and testing.
// It is not intended to connect to any physical devices. It is typically used to
// check RTP packet flows. It will echo back any RTP it receives.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 07 Sep 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media;

/// <summary>
/// A media session that can be used for diagnostics and testing. It is not intended to connect
/// to any physical devices. It is typically used to check RTP packet flows. It will echo back
/// any RTP it receives.
/// </summary>
public class EchoMediaSession : RTPSession, IMediaSession
{
    private const int SILENCE_SAMPLE_PERIOD_MILLISECONDS = 20;

    private AudioEncoder _audioEncoder = new AudioEncoder();

    public EchoMediaSession() : base(false, false, false)
    {
        base.OnAudioFormatsNegotiated += AudioFormatsNegotiated;
        base.OnVideoFormatsNegotiated += VideoFormatsNegotiated;
        base.OnTextFormatsNegotiated += TextFormatsNegotiated;
    }

    private void AudioFormatsNegotiated(List<AudioFormat> audoFormats)
    {
        // IMPTORTANT NOTE: The audio sink format cannot be set here as it is not known until the first RTP packet
        // is received from the remote party. All we know at this stage is which audio formats are supported but NOT
        // which one the remote party has chosen to use. Generally it seems the sending and receiving formats should be the same but
        // the standard is very fuzzy in that area. See https://datatracker.ietf.org/doc/html/rfc3264#section-7 and note the "SHOULD" in the text.

        var audioFormat = audoFormats.First();
        logger.LogSettingAudioSourceFormatAndClockRate(audioFormat.FormatID, audioFormat.Codec, audioFormat.ClockRate, audioFormat.RtpClockRate);

        if (AudioStream?.LocalTrack?.NoDtmfSupport == false)
        {
            logger.LogAudioTrackDtmfNegotiated(AudioStream.NegotiatedRtpEventPayloadID);
        }

        if(AudioStream != null && AudioStream.RemoteTrack != null && AudioStream.LocalTrack != null)
        {
            AudioStream.OnAudioFrameReceived += (audioFrame) =>
            {
                // Echo the received audio frame back to the sender.
                AudioStream?.SendAudio(audioFrame.DurationMilliSeconds, audioFrame.EncodedAudio.Span);
            };
        }
    }

    private void VideoFormatsNegotiated(List<VideoFormat> videoFormats)
    {
        // IMPTORTANT NOTE: The video sink format cannot be set here as it is not known until the first RTP packet
        // is received from the remote party. All we know at this stage is which audio formats are supported but NOT
        // which one the remote party has chosen to use. Generally it seems the sending and receiving formats should be the same but
        // the standard is very fuzzy in that area. See https://datatracker.ietf.org/doc/html/rfc3264#section-7 and note the "SHOULD" in the text.

        var videoFormat = videoFormats.First();
        logger.LogSettingVideoSinkAndSourceFormat(videoFormat.FormatID, videoFormat.Codec);

        if (VideoStream != null && VideoStream.RemoteTrack != null && VideoStream.LocalTrack != null)
        {
            VideoStream.OnVideoFrameReceivedByIndex += (index, from, ts, payload, format) =>
            {
                // TODO.
                logger.LogVideoEchoNotImplemented();
            };
        }
    }

    private void TextFormatsNegotiated(List<TextFormat> textFormats)
    {
        var textFormat = textFormats.First();
        logger.LogSettingTextSinkAndSourceFormat(textFormat.FormatID, textFormat.Codec);

        if (TextStream != null && TextStream.RemoteTrack != null && TextStream.LocalTrack != null)
        {
            TextStream.OnRtpPacketReceivedByIndex += (int index, IPEndPoint from, SDPMediaTypesEnum mediaType, RTPPacket pkt) =>
            {
                TextStream.SendText(pkt.Payload.Span);
            };
        }
    }

    /// <summary>
    /// This overload sends a single silence audio packet when the session is started. The goal is to open a NAT binding if the calling application
    /// is on a NATted network. After that any audio received from the remote party will be echoed back.
    /// </summary>
    public async override Task Start()
    {
        await base.Start().ConfigureAwait(false);

       if(AudioStream != null)
        {
            var sdpAudioFormat = AudioStream.GetSendingFormat();
            var audioFormat = sdpAudioFormat.ToAudioFormat();
            logger.LogStartingAudioFormat(nameof(EchoMediaSession), sdpAudioFormat.ID, audioFormat.Codec);

            short[] silencePcm = new short[audioFormat.ClockRate / 1000 * SILENCE_SAMPLE_PERIOD_MILLISECONDS];

            EncodeAndSend(silencePcm, audioFormat);
        }
    }

    private void EncodeAndSend(short[] pcm, AudioFormat audioFormat)
    {
        if (pcm.Length > 0)
        {
            using var buffer = new ArrayPoolBufferWriter<byte>(8192);
            _audioEncoder.EncodeAudio(pcm, audioFormat, buffer);
            var encodedSample = buffer.WrittenSpan;

            uint rtpUnits = RtpTimestampExtensions.ToRtpUnits(SILENCE_SAMPLE_PERIOD_MILLISECONDS, audioFormat.RtpClockRate);

            AudioStream?.SendAudio(rtpUnits, encodedSample);
        }
    }
}
