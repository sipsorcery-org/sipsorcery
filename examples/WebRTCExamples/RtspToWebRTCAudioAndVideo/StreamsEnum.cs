using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RtspToWebRtcRestreamer
{
    internal enum StreamsEnum
    {
        video,
        audio,
        videoAndAudio,
        videoAndAudioUdp,
        none
    }
}
