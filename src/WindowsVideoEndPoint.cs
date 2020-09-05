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
using System.Linq;
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
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;

        public static ILogger logger = NullLogger.Instance;

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8
        };

        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private bool _forceKeyFrame = false;
        private VideoCodecsEnum _selectedSinkFormat = VideoCodecsEnum.VP8;
        private VideoCodecsEnum _selectedSourceFormat = VideoCodecsEnum.VP8;
        private bool _isStarted;
        private bool _isClosed;
        private List<VideoCodecsEnum> _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);

        /// <summary>
        /// This video source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("This video source only generates encoded samples. No raw video samples will be supplied to this event.")]
        public event RawVideoSampleDelegate OnVideoSourceRawSample { add { } remove { } }

        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        public WindowsVideoEndPoint()
        {
            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();
        }

        public void ForceKeyFrame()
        {
            _forceKeyFrame = true;
        }

        /// <summary>
        /// Requests that the video sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedCodecs" /> list can be 
        /// used.
        /// </summary>
        /// <param name="codecs">The list of codecs to restrict advertised support to.</param>
        public void RestrictCodecs(List<VideoCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<VideoCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x == codec))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] rgb24Sample)
        {
            if (!_isClosed)
            {
                if (_vp8Encoder == null)
                {
                    _vp8Encoder = new Vp8Codec();
                    _vp8Encoder.InitialiseEncoder((uint)width, (uint)height);
                }

                if (OnVideoSourceEncodedSample != null)
                {
                    byte[] i420Buffer = PixelConverter.RGBtoI420(rgb24Sample, width, height);
                    byte[] encodedBuffer = _vp8Encoder.Encode(i420Buffer, _forceKeyFrame);

                    if (encodedBuffer != null)
                    {
                        //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                        uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                    }

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
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
            throw new ApplicationException("The Windows Video End Point requires full video frames rather than individual RTP packets.");
        }

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame)
        {
            if (!_isClosed)
            {
                //DateTime startTime = DateTime.Now;

                List<byte[]> decodedFrames = _vp8Decoder.Decode(frame, frame.Length, out var width, out var height);

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
            }
        }

        public Task PauseVideo()
        {
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
            }
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                Dispose();
            }
            return Task.CompletedTask;
        }

        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            if (videoFormat != VideoCodecsEnum.VP8)
            {
                throw new ApplicationException($"The Windows Video Source End Point does not support video codec {videoFormat}.");
            }

            _selectedSourceFormat = videoFormat;
        }

        public List<VideoCodecsEnum> GetVideoSinkFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat)
        {
            if (videoFormat != VideoCodecsEnum.VP8)
            {
                throw new ApplicationException($"The Windows Video Sink End Point does not support video codec {videoFormat}.");
            }

            _selectedSinkFormat = videoFormat;
        }

        public void Dispose()
        {
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
        }
    }
}
