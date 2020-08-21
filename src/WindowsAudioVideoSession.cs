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
        public event OnVideoSampleReadyDelegate OnVideoSampleReady;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsAudioVideoSession()
        { }
    }
}
