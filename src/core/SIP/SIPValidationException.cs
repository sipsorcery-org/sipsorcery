//-----------------------------------------------------------------------------
// Filename: SIPValidationException.cs
//
// Description: Exception class for SIP validation errors.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 15 Mar 2009	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.SIP
{
    public enum SIPValidationFieldsEnum
    {
        Unknown,
        Request,
        Response,
        URI,
        Headers,
        ContactHeader,
        FromHeader,
        RouteHeader,
        ToHeader,
        ViaHeader,
        NoSIPString,
        ReferToHeader,
    }

    public class SIPValidationException : Exception
    {
        public SIPValidationFieldsEnum SIPErrorField;
        public SIPResponseStatusCodesEnum SIPResponseErrorCode;

        public SIPValidationException(SIPValidationFieldsEnum sipErrorField, string message)
            : base(message)
        {
            SIPErrorField = sipErrorField;
            SIPResponseErrorCode = SIPResponseStatusCodesEnum.BadRequest;
        }

        public SIPValidationException(SIPValidationFieldsEnum sipErrorField, SIPResponseStatusCodesEnum responseErrorCode, string message)
            : base(message)
        {
            SIPErrorField = sipErrorField;
            SIPResponseErrorCode = responseErrorCode;
        }
    }
}
