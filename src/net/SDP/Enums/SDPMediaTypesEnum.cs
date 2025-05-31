//-----------------------------------------------------------------------------
// Filename: SDPMediaTypesEnum.cs
//
// Description: Enum for the different SDP media types.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30MMAy 2025  Aaron Clauson	Refactored from SDPTypes.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net;

public enum SDPMediaTypesEnum
{
    invalid = 0,
    audio = 1,
    video = 2,
    application = 3,
    data = 4,
    control = 5,
    image = 6,
    message = 7,
    text = 8
}
