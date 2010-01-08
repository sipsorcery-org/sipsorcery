//-----------------------------------------------------------------------------
// Filename: SIPConstants.cs
//
// Description: SIP constants.
// 
// History:
// 17 Sep 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using SIPSorcery.Sys;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	public class SIPConstants
	{
		public const string CRLF = "\r\n";
		
		public const string SIP_VERSION_STRING = "SIP";
		public const int SIP_MAJOR_VERSION = 2;
		public const int SIP_MINOR_VERSION = 0;
		public const string SIP_FULLVERSION_STRING = "SIP/2.0";

		public const int NONCE_TIMEOUT_MINUTES = 5;							// Length of time an issued nonce is valid for.
		public const int SIP_MAXIMUM_LENGTH = 2048;							// Any SIP messages over this size will generate an error.
        public const string SIP_USERAGENT_STRING = "www.sipsorcery.com";
        public const string SIP_SERVER_STRING = "www.sipsorcery.com";
		public const string SIP_REQUEST_REGEX = @"^\w+ .* SIP/.*";			// bnf:	Request-Line = Method SP Request-URI SP SIP-Version CRLF
		public const string SIP_RESPONSE_REGEX = @"^SIP/.* \d{3}";			// bnf: Status-Line = SIP-Version SP Status-Code SP Reason-Phrase CRLF
		public const string SIP_BRANCH_MAGICCOOKIE = "z9hG4bK";
		public const string SIP_DEFAULT_USERNAME = "Anonymous";
		public const string SIP_DEFAULT_FROMURI = "sip:thisis@anonymous.invalid";
		public const string SIP_REGISTER_REMOVEALL = "*";					// The value in a REGISTER request id a UA wishes to remove all REGISTER bindings.
		public const string SIP_LOOSEROUTER_PARAMETER = "lr";
        public const string SIP_REMOTEHANGUP_CAUSE = "remote end hungup";      
        public const char HEADER_DELIMITER_CHAR = ':';

		public const int DEFAULT_MAX_FORWARDS = 70;
		public const int DEFAULT_REGISTEREXPIRY_SECONDS = 600;
		public const int DEFAULT_SIP_PORT = 5060;
        public const int DEFAULT_SIP_TLS_PORT = 5061;

		public const int DEFAULT_STARTRTP_PORT = 10000;
		public const int DEFAULT_ENDRTP_PORT = 20000;

		public const int MAX_PORT = 65535;

		public const string MWI_CONTENT_TYPE = "application/simple-message-summary";
        public const string NAT_SENDKEEPALIVES_VALUE = "y";

        public const string SIP_REFER_NOTIFY_EVENT = "refer";                   // The value that must be set for the Event header on a NOTIFY request when processing a REFER (RFC 3515).
        public const string SIP_REFER_NOTIFY_CONTENTTYPE = "message/sipfrag";   // The content type that must be set for a NOTIFY request when processing a REFER (RFC 3515).
	}

	public enum SIPMessageTypesEnum
	{
		Unknown = 0,
		Request = 1,
		Response = 2,
	}

	public class SIPTimings
	{
        public const int T1 = 500;                      // Value of the SIP defined timer T1 in milliseconds and is the time for the first retransmit.
        public const int T2 = 4000;                     // Value of the SIP defined timer T2 in milliseconds and is the maximum time between retransmits.
		public const int T6 = 64 * T1;                  // Value of the SIP defined timer T6 in milliseconds and is the period after which a transaction has timed out.
        public const int MAX_RING_TIME = 180000;        // The number of milliseconds a transaction can stay in the proceeding state (i.e. an INVITE will ring for) before the call is given up and timed out.     
	}

    public enum SIPSchemesEnum
    {
        sip = 1,
        sips = 2,
    }

    public class SIPSchemesType
    {
        public static SIPSchemesEnum GetSchemeType(string schemeType)
        {
            return (SIPSchemesEnum)Enum.Parse(typeof(SIPSchemesEnum), schemeType, true);
        }

        public static bool IsAllowedScheme(string schemeType)
        {
            try
            {
                Enum.Parse(typeof(SIPSchemesEnum), schemeType, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public enum SIPProtocolsEnum
    {
        udp = 1,
        tcp = 2,
        tls = 3,
        // sctp = 4,    // Not supported.
    }

    public class SIPProtocolsType
    {
        public static SIPProtocolsEnum GetProtocolType(string protocolType)
        {
            return (SIPProtocolsEnum)Enum.Parse(typeof(SIPProtocolsEnum), protocolType, true);
        }

        public static SIPProtocolsEnum GetProtocolTypeFromId(int protocolTypeId)
        {
            return (SIPProtocolsEnum)Enum.Parse(typeof(SIPProtocolsEnum), protocolTypeId.ToString(), true);
        }

        public static bool IsAllowedProtocol(string protocol)
        {
            try
            {
                Enum.Parse(typeof(SIPProtocolsEnum), protocol, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

	public class SIPHeaders
	{
		// SIP Header Keys.
        public const string SIP_HEADER_ACCEPT = "Accept";
        public const string SIP_HEADER_ACCEPTENCODING = "Accept-Encoding";
        public const string SIP_HEADER_ACCEPTLANGUAGE = "Accept-Language";
        public const string SIP_HEADER_ALERTINFO = "Alert-Info";
        public const string SIP_HEADER_ALLOW = "Allow";
        public const string SIP_HEADER_AUTHENTICATIONINFO = "Authentication-Info";
        public const string SIP_HEADER_AUTHORIZATION = "Authorization";
		public const string SIP_HEADER_CALLID = "Call-ID";
        public const string SIP_HEADER_CALLINFO = "Call-Info";
        public const string SIP_HEADER_CONTACT = "Contact";
        public const string SIP_HEADER_CONTENT_DISPOSITION = "Content-Disposition";
        public const string SIP_HEADER_CONTENT_ENCODING = "Content-Encoding";
        public const string SIP_HEADER_CONTENT_LANGUAGE = "Content-Language";
        public const string SIP_HEADER_CONTENTLENGTH = "Content-Length";
        public const string SIP_HEADER_CONTENTTYPE = "Content-Type";
		public const string SIP_HEADER_CSEQ = "CSeq";
        public const string SIP_HEADER_DATE = "Date";
        public const string SIP_HEADER_ERROR_INFO = "Error-Info";
        public const string SIP_HEADER_EXPIRES = "Expires";
		public const string SIP_HEADER_FROM = "From";
        public const string SIP_HEADER_IN_REPLY_TO = "In-Reply-To";
        public const string SIP_HEADER_MAXFORWARDS = "Max-Forwards";
        public const string SIP_HEADER_MINEXPIRES = "Min-Expires";
        public const string SIP_HEADER_MIME_VERSION = "MIME-Version";
        public const string SIP_HEADER_ORGANIZATION = "Organization";
        public const string SIP_HEADER_PRIORITY = "Priority";
        public const string SIP_HEADER_PROXYAUTHENTICATION = "Proxy-Authenticate";
        public const string SIP_HEADER_PROXYAUTHORIZATION = "Proxy-Authorization";
        public const string SIP_HEADER_PROXY_REQUIRE = "Proxy-Require";
        public const string SIP_HEADER_REASON = "Reason";
        public const string SIP_HEADER_RECORDROUTE = "Record-Route";
        public const string SIP_HEADER_REPLY_TO = "Reply-To";
        public const string SIP_HEADER_REQUIRE = "Require";
        public const string SIP_HEADER_RETRY_AFTER = "Retry-After";
        public const string SIP_HEADER_ROUTE = "Route";
        public const string SIP_HEADER_SERVER = "Server";
        public const string SIP_HEADER_SUBJECT = "Subject";
        public const string SIP_HEADER_SUPPORTED = "Supported";
        public const string SIP_HEADER_TIMESTAMP = "Timestamp";
		public const string SIP_HEADER_TO = "To";
        public const string SIP_HEADER_UNSUPPORTED = "Unsupported";
        public const string SIP_HEADER_USERAGENT = "User-Agent";
		public const string SIP_HEADER_VIA = "Via";
        public const string SIP_HEADER_WARNING = "Warning";
        public const string SIP_HEADER_WWWAUTHENTICATE = "WWW-Authenticate";

		// SIP Compact Header Keys.
		public const string SIP_COMPACTHEADER_CALLID = "i";
		public const string SIP_COMPACTHEADER_CONTACT = "m";
		public const string SIP_COMPACTHEADER_FROM = "f";
		public const string SIP_COMPACTHEADER_TO = "t";
		public const string SIP_COMPACTHEADER_CONTENTLENGTH = "l";
		public const string SIP_COMPACTHEADER_CONTENTTYPE = "c";
		public const string SIP_COMPACTHEADER_VIA = "v";
        public const string SIP_COMPACTHEADER_SUPPORTED = "k";
        public const string SIP_COMPACTHEADER_SUBJECT = "s";

        // SIP Header extensions from RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_HEADER_REFERREDBY = "Referred-By";
        public const string SIP_HEADER_REFERTO = "Refer-To";
        public const string SIP_COMPACTHEADER_REFERTO = "r";

		// SIP Header Extensions for SIP Event Package RFC 3265.
		public const string SIP_HEADER_EVENT = "Event";
		public const string SIP_HEADER_SUBSCRIPTIONSTATE = "Subscription-State";

        // Custom SIP headers to allow proxy to communicate network info to internal servers.
        public const string SIP_HEADER_PROXY_RECEIVEDON = "Proxy-ReceivedOn";
        public const string SIP_HEADER_PROXY_RECEIVEDFROM = "Proxy-ReceivedFrom";
        public const string SIP_HEADER_PROXY_SENDFROM = "Proxy-SendFrom";
	}

	public class SIPHeaderAncillary
	{
        // Header parameters used in the core SIP protocol.
		public const string SIP_HEADERANC_TAG = "tag";
		public const string SIP_HEADERANC_BRANCH = "branch";
		public const string SIP_HEADERANC_RECEIVED = "received";
        public const string SIP_HEADERANC_TRANSPORT = "transport";

        // Via header parameter, documented in RFC 3581 "An Extension to the Session Initiation Protocol (SIP) 
        // for Symmetric Response Routing".
        public const string SIP_HEADERANC_RPORT = "rport";

        // SIP header parameter from RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_REFER_REPLACES = "Replaces";
	}

	/// <summary>
	/// Authorization Headers
	/// </summary>
	public class AuthHeaders
	{
		public const string AUTH_DIGEST_KEY = "Digest";
		public const string AUTH_REALM_KEY = "realm";
		public const string AUTH_NONCE_KEY = "nonce";
		public const string AUTH_USERNAME_KEY = "username";
		public const string AUTH_RESPONSE_KEY = "response";
		public const string AUTH_URI_KEY = "uri";
		public const string AUTH_ALGORITHM_KEY = "algorithm";
        public const string AUTH_CNONCE_KEY = "cnonce";
        public const string AUTH_NONCECOUNT_KEY = "nc";
        public const string AUTH_QOP_KEY = "qop";
        public const string AUTH_OPAQUE_KEY = "opaque";
	}

	public enum SIPMethodsEnum
	{
		NONE = 0,
		UNKNOWN = 1,
		
        // Core.
        REGISTER = 2,
		INVITE = 3,
		BYE = 4,
		ACK = 5,
		CANCEL = 6,
		OPTIONS = 7,

		INFO = 8,
		NOTIFY = 9,
		SUBSCRIBE = 10,
		PUBLISH = 11,
		PING = 13,
		REFER = 14,         // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method"
        MESSAGE = 15,
	}

	public class SIPMethods
	{
		public static SIPMethodsEnum GetMethod(string method)
		{
			SIPMethodsEnum sipMethod = SIPMethodsEnum.UNKNOWN;

			try
			{
				sipMethod = (SIPMethodsEnum)Enum.Parse(typeof(SIPMethodsEnum), method, true);
			}
			catch{}

			return sipMethod;
		}
	}
		
	public enum SIPResponseStatusCodesEnum
	{
		None = 0,
        
        // Informational
		Trying = 100,
		Ringing = 180,
		CallIsBeingForwarded = 181,
		Queued = 182,
		SessionProgress = 183,
		
		// Success
		Ok = 200,
        Accepted = 202,     // Extensions from RFC 3515 The Session Initiation Protocol (SIP) Refer Method.

		// Redirection
        MultipleChoices = 300,
        MovedPermanently = 301,
		MovedTemporarily = 302,
        UseProxy = 303,
        AlternativeService = 304,

		// Client-Error
		BadRequest = 400,
		Unauthorised = 401,
		PaymentRequired = 402,
		Forbidden = 403,
		NotFound = 404,
		MethodNotAllowed = 405,
		NotAcceptable = 406,
		ProxyAuthenticationRequired = 407,
        RequestTimeout = 408,
        Gone = 409,
        RequestEntityTooLarge = 413,
        RequestURITooLarge = 414,
        UnsupportedMediaType = 415,
        UnsupportedURIScheme = 416,
        BadExtension = 420,
        ExtensionRequired = 421,
        IntervalTooBrief = 423,
		TemporarilyNotAvailable = 480,
		CallLegTransactionDoesNotExist = 481,
		LoopDetected = 482,
		TooManyHops = 483,
        AddressIncomplete = 484,
        Ambiguous = 485,
		BusyHere = 486,
		RequestTerminated = 487,
        NotAcceptableHere = 488,
        RequestPending = 491,
        Undecipherable = 493,
		
		// Server-Error
		InternalServerError = 500,
        NotImplemented = 501,
        BadGateway = 502,
        ServiceUnavailable = 503,
		ServerTimeout = 504,
        SIPVersionNotSupported = 505,
		MessageTooLarge = 513,

		// Global-Failure
        BusyEverywhere = 600,
        Decline = 603,
        DoesNotExistAnywhere = 604,
        NotAcceptableAnywhere = 606,
	}

	public class SIPResponseStatusCodes
	{
		public static SIPResponseStatusCodesEnum GetStatusTypeForCode(int statusCode)
		{
			return (SIPResponseStatusCodesEnum)Enum.Parse(typeof(SIPResponseStatusCodesEnum), statusCode.ToString(), true);
		}
	}

    public enum SIPUserAgentRoles
    {
        Unknown = 0,
        Client = 1,     // UAC.
        Server = 2,     // UAS.
    }

    /*public enum SIPValidationError
    {
        None = 0,

        // Errors that can occur when parsing individual headers.
        CSeqMethodMissing = 1,
        CSeqNotValidInteger = 2,
        CSeqEmpty = 3,
        ExpiresNotValidInteger = 4,
        MaxHeaderNotValidInteger = 5,
        ContentLengthNotValidInteger = 6,
        NoClosingRQuote = 7,
        URIInvalid = 8,
        ViaHeaderIllegal = 10,
        NonNumericPortForIPAddress = 11,
        NoContactAddressOnVia = 14,

        // Errors that can occur on the SIP header.
        CSeqMissing = 101,
        FromHeaderMissing = 102,
        ToHeaderMissing = 103,
        NoViaHeadersPresent = 104,

        // Errors that can occur on a SIP request.
        RequestURIMissing = 201,
        RequestURIInvalid = 202,
        TooManyHops = 203,
        Loop = 204,
        TooLarge = 205,

        // Errors that can occur when creating a SIP Transacion.
        NoBranchOnVia = 301,             // This validation error will only apply to requests that are being used to create a new transaction (in theory it should be all requests but there are lots of cases in the wild that this would stop).
        DuplicateTransaction = 302,
        AckOnNonCompletedTransaction = 303,
    }*/

    public static class SIPEscape
    {
        public static string SIPEscapeString(string unescapedString)
        {
            string result = unescapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace(";", "%3B");
                result = result.Replace("=", "%3D");
            }
            return result;
        }

        public static string SIPUnescapeString(string escapedString)
        {
            string result = escapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace("%3B", ";");
                result = result.Replace("%3D", "=");
            }
            return result;
        }
    }
}
