//-----------------------------------------------------------------------------
// Filename: AudioVideoWellKnown.cs
//
// Description: Standard audio and video format definitions.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Refactored from MediaEndPoints.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;

namespace SIPSorceryMedia.Abstractions;

public static class AudioVideoWellKnown
{
    public static Dictionary<SDPWellKnownMediaFormatsEnum, AudioFormat> WellKnownAudioFormats =
        new Dictionary<SDPWellKnownMediaFormatsEnum, AudioFormat> {
                { SDPWellKnownMediaFormatsEnum.PCMU,     new AudioFormat(AudioCodecsEnum.PCMU, 0, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.GSM,      new AudioFormat(AudioCodecsEnum.GSM,  3, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.G723,     new AudioFormat(AudioCodecsEnum.G723, 4, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.DVI4,     new AudioFormat(AudioCodecsEnum.DVI4, 5, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.DVI4_16K, new AudioFormat(AudioCodecsEnum.DVI4, 6, 16000, 1)},
                { SDPWellKnownMediaFormatsEnum.LPC,      new AudioFormat(AudioCodecsEnum.LPC,  7, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.PCMA,     new AudioFormat(AudioCodecsEnum.PCMA, 8, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.G722,     new AudioFormat(AudioCodecsEnum.G722, 9, 16000, 8000, 1, null)},
                { SDPWellKnownMediaFormatsEnum.L16_2,    new AudioFormat(AudioCodecsEnum.L16,  10, 44100, 2)},
                { SDPWellKnownMediaFormatsEnum.L16,      new AudioFormat(AudioCodecsEnum.L16,  11, 44100, 1)},
                { SDPWellKnownMediaFormatsEnum.QCELP,    new AudioFormat(AudioCodecsEnum.QCELP,12, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.CN,       new AudioFormat(AudioCodecsEnum.CN,   13, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.MPA,      new AudioFormat(AudioCodecsEnum.MPA,  14, 90000, 1)},
                { SDPWellKnownMediaFormatsEnum.G728,     new AudioFormat(AudioCodecsEnum.G728, 15, 8000, 1)},
                { SDPWellKnownMediaFormatsEnum.DVI4_11K, new AudioFormat(AudioCodecsEnum.DVI4, 16, 11025, 1)},
                { SDPWellKnownMediaFormatsEnum.DVI4_22K, new AudioFormat(AudioCodecsEnum.DVI4, 17, 22050, 1)},
                { SDPWellKnownMediaFormatsEnum.G729,     new AudioFormat(AudioCodecsEnum.G729, 18, 8000, 1)},
        };

    public static Dictionary<SDPWellKnownMediaFormatsEnum, VideoFormat> WellKnownVideoFormats =
       new Dictionary<SDPWellKnownMediaFormatsEnum, VideoFormat> {
                { SDPWellKnownMediaFormatsEnum.CELB,     new VideoFormat(VideoCodecsEnum.CELB, 24, 90000)},
                { SDPWellKnownMediaFormatsEnum.JPEG,     new VideoFormat(VideoCodecsEnum.JPEG, 26, 90000)},
                { SDPWellKnownMediaFormatsEnum.NV,       new VideoFormat(VideoCodecsEnum.NV,   28, 90000)},
                { SDPWellKnownMediaFormatsEnum.H261,     new VideoFormat(VideoCodecsEnum.H261, 31, 90000)},
                { SDPWellKnownMediaFormatsEnum.MPV,      new VideoFormat(VideoCodecsEnum.MPV,  32, 90000)},
                { SDPWellKnownMediaFormatsEnum.MP2T,     new VideoFormat(VideoCodecsEnum.MP2T, 33, 90000)},
                { SDPWellKnownMediaFormatsEnum.H263,     new VideoFormat(VideoCodecsEnum.H263, 34, 90000)}
       };
}