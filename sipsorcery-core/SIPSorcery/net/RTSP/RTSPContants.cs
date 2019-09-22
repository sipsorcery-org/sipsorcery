//-----------------------------------------------------------------------------
// Filename: RTSPConstants.cs
//
// Description: RTSP constants.
//
// History:
// 09 Nov 2007	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net
{
    public class RTSPConstants
    {
        public const string CRLF = "\r\n";

        public const string RTSP_VERSION_STRING = "RTSP";
        public const int RTSP_MAJOR_VERSION = 1;
        public const int RTSP_MINOR_VERSION = 0;
        public const string RTSP_FULLVERSION_STRING = "RTSP/1.0";

        public const int DEFAULT_RTSP_PORT = 554;                       // RFC2326 9.2, default port for both TCP and UDP.
        public const string RTSP_RELIABLE_TRANSPORTID = "rtsp";
        public const string RTSP_UNRELIABLE_TRANSPORTID = "rtspu";

        public const int INITIAL_RTT_MILLISECONDS = 500;                // RFC2326 9.2, initial round trip time used for retransmits on unreliable transports.
        public const int RTSP_MAXIMUM_LENGTH = 4096;

    }

    public enum RTSPMessageTypesEnum
    {
        Unknown = 0,
        Request = 1,
        Response = 2,
    }

    public enum RTSPMethodsEnum
    {
        UNKNOWN = 0,
        ANNOUNCE = 1,
        DESCRIBE = 2,
        GET_PARAMETER = 3,
        OPTIONS = 4,
        PAUSE = 5,
        PLAY = 6,
        RECORD = 7,
        REDIRECT = 8,
        SETUP = 9,
        SET_PARAMETER = 10,
        TEARDOWN = 11,
    }

    public class RTSPMethods
    {
        public static RTSPMethodsEnum GetMethod(string method)
        {
            RTSPMethodsEnum rtspMethod = RTSPMethodsEnum.UNKNOWN;

            try
            {
                rtspMethod = (RTSPMethodsEnum)Enum.Parse(typeof(RTSPMethodsEnum), method, true);
            }
            catch { }

            return rtspMethod;
        }
    }

    public class RTSPHeaders
    {
        public const string RTSP_HEADER_ACCEPT = "Accept";
        public const string RTSP_HEADER_ACCEPTENCODING = "Accept-Encoding";
        public const string RTSP_HEADER_ACCEPTLANGUAGE = "Accept-Language";
        public const string RTSP_HEADER_AUTHORIZATION = "Authorization";
        public const string RTSP_HEADER_CONNECTION = "Connection";
        public const string RTSP_HEADER_CONTENTENCODING = "Content-Encoding";
        public const string RTSP_HEADER_CONTENTLANGUAGE = "Content-Language";
        public const string RTSP_HEADER_CONTENTLENGTH = "Content-Length";
        public const string RTSP_HEADER_CONTENTTYPE = "Content-Type";
        public const string RTSP_HEADER_CSEQ = "CSeq";
        public const string RTSP_HEADER_FROM = "From";
        public const string RTSP_HEADER_IFMODIFIEDSINCE = "If-Modified-Since";
        public const string RTSP_HEADER_LOCATION = "Location";
        public const string RTSP_HEADER_PROXYAUTHENTICATE = "Proxy-Authenticate";
        public const string RTSP_HEADER_RANGE = "Range";
        public const string RTSP_HEADER_REFER = "Referer";
        public const string RTSP_HEADER_RETRYAFTER = "Retry-After";
        public const string RTSP_HEADER_SERVER = "Server";
        public const string RTSP_HEADER_SESSION = "Session";
        public const string RTSP_HEADER_TRANSPORT = "Transport";
        public const string RTSP_HEADER_USERAGENT = "User-Agent";
        public const string RTSP_HEADER_VARY = "Vary";
        public const string RTSP_HEADER_WWWAUTHENTICATE = "WWW-Authenticate";
    }

    public class RTSPEntityHeaders
    {
        public const string RTSP_ENTITYHEADER_ALLOW = "Allow";
        public const string RTSP_ENTITYHEADER_CONTENTBASE = "Content-Base";
        public const string RTSP_ENTITYHEADER_CONTENTENCODING = "Content-Encoding";
        public const string RTSP_ENTITYHEADER_CONTENTLANGUAGE = "Content-Language";
        public const string RTSP_ENTITYHEADER_CONTENTLENGTH = "Content-Length";
        public const string RTSP_ENTITYHEADER_CONTENTLOCATION = "Content-Location";
        public const string RTSP_ENTITYHEADER_CONTENTTYPE = "Content-Type";
        public const string RTSP_ENTITYHEADER_EXPIRES = "Expires";
        public const string RTSP_ENTITYHEADER_LASTMODIFIED = "Last-Modified";
    }

    public enum RTSPResponseStatusCodesEnum
    {
        // 1xx Informational.
        Continue = 100,

        // 2xx Success.
        OK = 200,
        Created = 201,
        LowOnStorageSpace = 250,

        // 3xx Redirection.
        MultipleChoices = 300,
        MovedPermanently = 301,
        MovedTemporarily = 302,
        SeeOther = 303,
        UseProxy = 305,

        // 4xx Client Error.
        BadRequest = 400,
        Unauthorized = 401,
        PaymentRequired = 402,
        Forbidden = 403,
        NotFound = 404,
        MethodNotAllowed = 405,
        NotAcceptable = 406,
        ProxyAuthenticationRequired = 407,

        // 5xx Server Error.
        InternalServerError = 500,
        NotImplemented = 501,
        BadGateway = 502,
        ServiceUnavailable = 503,
        GatewayTimeOut = 504,
        RTSPVersionNotSupported = 505,
        OptionNotSupported = 551,
    }

    public class RTSPResponseStatusCodes
    {
        public static RTSPResponseStatusCodesEnum GetStatusTypeForCode(int statusCode)
        {
            return (RTSPResponseStatusCodesEnum)Enum.Parse(typeof(RTSPResponseStatusCodesEnum), statusCode.ToString(), true);
        }
    }
	
}
