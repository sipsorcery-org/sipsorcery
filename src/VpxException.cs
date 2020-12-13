//-----------------------------------------------------------------------------
// Filename: VpxExceptioncs
//
// Description: This exception class is intended to fulfill the same role as the 
// vpx_internal_error function from the original C source. That function uses
// a longjmp call to unwind the stack when errors occur.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace Vpx.Net
{
    public class VpxException : Exception
    {
        public vpx_codec_err_t ErrorCode { get; private set;}

        public VpxException(vpx_codec_err_t errorCode, string errorMessage) :
            base(errorMessage)
        {
            ErrorCode = errorCode;
        }
    }
}
