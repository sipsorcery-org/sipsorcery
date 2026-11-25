//-----------------------------------------------------------------------------
// Filename: codec_ver.cs
//
// Description: Port of:
//  - codec/api/wels/codec_ver.h
// OpenH264 commit 78783f19 (v2.7.0-1). The version reported is the upstream
// version this port is bit-exact with.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice, see OPENH264_LICENSE.
//-----------------------------------------------------------------------------

namespace SIPSorcery.H264;

internal static class codec_ver
{
    public static readonly OpenH264Version g_stCodecVersion = new OpenH264Version { uMajor = 2, uMinor = 7, uRevision = 0, uReserved = 2610 };
    public const string g_strCodecVer = "OpenH264 version:2.7.0.2610";

    public const int OPENH264_MAJOR = 2;
    public const int OPENH264_MINOR = 7;
    public const int OPENH264_REVISION = 0;
    public const int OPENH264_RESERVED = 2610;

    /// <summary>The upstream commit the port tracks.</summary>
    public const string OPENH264_PORTED_COMMIT = "78783f19";
}
