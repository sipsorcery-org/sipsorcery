//-----------------------------------------------------------------------------
// Filename: WindowsVideoEndPoint.cs
//
// Description: Implements a video source and sink for Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Aug 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows.Codecs;

namespace SIPSorceryMedia.Windows
{
    public class WindowsVideoEndPoint : IVideoSource, IVideoSink, IDisposable
    {
        private const int VIDEO_TIMESTAMP_SPACING = 3000;

        public static ILogger logger = NullLogger.Instance;

        private IVideoSource _externalSource;
        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private bool _forceKeyFrame = false;
        private VideoCodecsEnum _selectedSinkFormat = VideoCodecsEnum.VP8;
        private VideoCodecsEnum _selectedSourceFormat = VideoCodecsEnum.VP8;
        private byte[] _currVideoFrame = new byte[65536];
        private int _currVideoFramePosn = 0;
        private bool _isStarted;
        private bool _isClosed;

        /// <summary>
        /// This video source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("This video source only generates encoded samples. No raw video samples will be supplied to this event.")]
        public event RawVideoSampleDelegate OnVideoSourceRawSample { add { } remove { } }

        public event VideoEncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        /// <param name="options">The options for the video source. If null then this end point will
        /// act as a video sink only.</param>
        public WindowsVideoEndPoint(IVideoSource externalSource = null)
        {
            if (externalSource != null)
            {
                _externalSource = externalSource;
                _externalSource.OnVideoSourceRawSample += ExternalSource_OnVideoSourceRawSample;
            }
            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();
        }

        private void ExternalSource_OnVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] rgb24Sample)
        {
            if (_vp8Encoder == null)
            {
                _vp8Encoder = new Vp8Codec();
                _vp8Encoder.InitialiseEncoder((uint)width, (uint)height);
            }

            if (OnVideoSourceEncodedSample != null)
            {
                byte[] encodedBuffer = null;

                if (_selectedSourceFormat == VideoCodecsEnum.VP8)
                {
                    byte[] i420Buffer = PixelConverter.RGBtoI420(rgb24Sample, width, height);
                    encodedBuffer = _vp8Encoder.Encode(i420Buffer, _forceKeyFrame);
                }
                //else if (VIDEO_CODEC == SDPMediaFormatsEnum.H264)
                //{
                //    var i420Frame = _videoFrameConverter.Convert(sampleBuffer);

                //    _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

                //    i420Frame.key_frame = _forceKeyFrame ? 1 : 0;
                //    i420Frame.pts = _presentationTimestamp;

                //    encodedBuffer = _ffmpegEncoder.Encode(i420Frame);
                //}
                else
                {
                    throw new ApplicationException($"Video codec is not supported.");
                }

                if (encodedBuffer != null)
                {
                    //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                    OnVideoSourceEncodedSample.Invoke(_selectedSourceFormat, VIDEO_TIMESTAMP_SPACING, encodedBuffer);
                }

                if (_forceKeyFrame)
                {
                    _forceKeyFrame = false;
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            //logger.LogDebug($"rtp video, seqnum {seqnum}, ts {timestamp}, marker {marker}, payload {payload.Length}.");
            if (_currVideoFramePosn + payload.Length >= _currVideoFrame.Length)
            {
                // Something has gone very wrong. Clear the buffer.
                _currVideoFramePosn = 0;
            }

            // New frames must have the VP8 Payload Descriptor Start bit set.
            // The tracking of the current video frame position is to deal with a VP8 frame being split across multiple RTP packets
            // as per https://tools.ietf.org/html/rfc7741#section-4.4.
            if (_currVideoFramePosn > 0 || (payload[0] & 0x10) > 0)
            {
                RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(payload);

                Buffer.BlockCopy(payload, vp8Header.Length, _currVideoFrame, _currVideoFramePosn, payload.Length - vp8Header.Length);
                _currVideoFramePosn += payload.Length - vp8Header.Length;

                if (marker)
                {
                    DateTime startTime = DateTime.Now;

                    List<byte[]> decodedFrames = _vp8Decoder.Decode(_currVideoFrame, _currVideoFramePosn, out var width, out var height);

                    if (decodedFrames == null)
                    {
                        logger.LogWarning("VPX decode of video sample failed.");
                    }
                    else
                    {
                        foreach (var decodedFrame in decodedFrames)
                        {
                            byte[] rgb = PixelConverter.I420toRGB(decodedFrame, (int)width, (int)height);
                            //Console.WriteLine($"VP8 decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");
                            OnVideoSinkDecodedSample(rgb, width, height, (int)(width * 3));
                        }
                    }

                    _currVideoFramePosn = 0;
                }
            }
            else
            {
                logger.LogWarning("Discarding RTP packet, VP8 header Start bit not set.");
                logger.LogWarning($"rtp video, seqnum {seqnum}, ts {timestamp}, marker {marker}, payload {payload.Length}.");
            }
        }

        public Task PauseVideo()
        {
            _externalSource?.PauseVideo();
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _externalSource?.ResumeVideo();
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _externalSource?.StartVideo();
            }
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _externalSource?.CloseVideo();
            }
            return Task.CompletedTask;
        }

        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            return new List<VideoCodecsEnum> { VideoCodecsEnum.VP8 };
        }

        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            _selectedSourceFormat = videoFormat;
        }

        public List<VideoCodecsEnum> GetVideoSinkFormats()
        {
            return new List<VideoCodecsEnum> { VideoCodecsEnum.VP8};
        }

        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat)
        {
            _selectedSinkFormat = videoFormat;
        }

        public void Dispose()
        {
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
        }
    }
}
