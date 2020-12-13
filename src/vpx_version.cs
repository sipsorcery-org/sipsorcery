//-----------------------------------------------------------------------------
// Filename: vpx_version.cs
//
// Description: Port of:
//  - vpx_version.h (auto-generated)
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace Vpx.Net
{
    public class vpx_version
    {
        public const int VERSION_MAJOR = 1;
        public const int VERSION_MINOR = 9;
        public const int VERSION_PATCH = 0;
        public const string VERSION_EXTRA = "";
        public const int VERSION_PACKED = ((VERSION_MAJOR << 16) | (VERSION_MINOR << 8) | (VERSION_PATCH));
        public const string VERSION_STRING_NOSP = "v1.9.0";
        public const string VERSION_STRING = " v1.9.0";
    }
}
