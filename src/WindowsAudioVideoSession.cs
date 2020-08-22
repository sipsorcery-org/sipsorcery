//-----------------------------------------------------------------------------
// Filename: WindowsAudioVideoSession.cs
//
// Description: Extends the WindowsAudioSession to include video
// encoding and decoding capabilities.
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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows.Codecs;

namespace SIPSorceryMedia.Windows
{
    public delegate void OnVideoSampleReadyDelegate(byte[] bmp, uint width, uint height, int stride);

    public enum VideoSourcesEnum
    {
        None = 0,
        Webcam = 1,
        TestPattern = 2,
        ExternalBitmap = 3, // For example audio scope visualisations.
    }

    public class VideoSourceOptions
    {
        public const int DEFAULT_FRAME_RATE = 30;

        /// <summary>
        /// The type of video source to use.
        /// </summary>
        public VideoSourcesEnum VideoSource;

        /// <summary>
        /// IF using a video test pattern this is the base image source file.
        /// </summary>
        public string SourceFile;

        /// <summary>
        /// The frame rate to apply to request for the video source. May not be
        /// applied for certain sources such as a live webcam feed.
        /// </summary>
        public int SourceFramesPerSecond = DEFAULT_FRAME_RATE;

        //public IBitmapSource BitmapSource;
    }

    public class WindowsAudioVideoSession : WindowsAudioSession
    {
        private Vp8Codec _vp8Codec;

        public event OnVideoSampleReadyDelegate OnVideoSampleReady;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioVideoSession()
        { }

        public override void GotRemoteVideoFrame(int payloadID, int timestampDuration, byte[] frame)
        {
            //RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(frame);

            unsafe
            {
                fixed (byte* p = frame)
                {
                    uint width = 0, height = 0;
                    byte[] i420 = null;

                    //Console.WriteLine($"Attempting vpx decode {_currVideoFramePosn} bytes.");

                    //int decodeResult = _vpxDecoder.Decode(p, 0, ref i420, ref width, ref height);

                    //if (decodeResult != 0)
                    //{
                    //    Log.LogWarning("VPX decode of video sample failed.");
                    //}
                    //else
                    //{
                    //    if (OnVideoSampleReady != null)
                    //    {
                    //        fixed (byte* r = i420)
                    //        {
                    //            byte[] bmp = null;
                    //            int stride = 0;
                    //            int convRes = _imgConverter.ConvertYUVToRGB(r, VideoSubTypesEnum.I420, (int)width, (int)height, VideoSubTypesEnum.BGR24, ref bmp, ref stride);

                    //            if (convRes == 0)
                    //            {
                    //                OnVideoSampleReady?.Invoke(bmp, width, height, stride);
                    //            }
                    //            else
                    //            {
                    //                Log.LogWarning("Pixel format conversion of decoded sample failed.");
                    //            }
                    //        }
                    //    }
                    //}
                }
            }
        }
    }
}
