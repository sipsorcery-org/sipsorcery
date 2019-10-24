//-----------------------------------------------------------------------------
// Filename: SDPMediaTypes.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public enum SDPMediaTypesEnum
    {
        audio = 1,
        video = 2,
        application = 3,
        data = 4,
        control = 5,
    }

    public class SDPMediaTypes
    {
        public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
        {
            return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
        }
    }
}
