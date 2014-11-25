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
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
		public const int SIP_MAXIMUM_RECEIVE_LENGTH = 65535;				// Any SIP messages over this size will generate an error.
        public const int SIP_MAXIMUM_UDP_SEND_LENGTH = 1300;				// Any SIP messages over this size should be prevented from using a UDP transport.
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
        public const int MAX_SIP_PORT = 65535;
 
        public const string NAT_SENDKEEPALIVES_VALUE = "y";

        //public const string SWITCHBOARD_USER_AGENT_PREFIX = "sipsorcery-switchboard";
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
        public const string SIP_HEADER_ALLOW_EVENTS = "Allow-Events";               // RC3265 (SIP Events).
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
        public const string SIP_HEADER_EVENT = "Event";                             // RC3265 (SIP Events).
        public const string SIP_HEADER_ETAG = "SIP-ETag";                           // RFC3903
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
        public const string SIP_HEADER_REFERREDBY = "Referred-By";                  // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_HEADER_REFERSUB = "Refer-Sub";                      // RFC 4488 Used to stop the implicit SIP event subscription on a REFER request.
        public const string SIP_HEADER_REFERTO = "Refer-To";                        // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_HEADER_REPLY_TO = "Reply-To";
        public const string SIP_HEADER_REQUIRE = "Require";
        public const string SIP_HEADER_RETRY_AFTER = "Retry-After";
        public const string SIP_HEADER_ROUTE = "Route";
        public const string SIP_HEADER_SERVER = "Server";
        public const string SIP_HEADER_SUBJECT = "Subject";
        public const string SIP_HEADER_SUBSCRIPTION_STATE = "Subscription-State";       // RC3265 (SIP Events).
        public const string SIP_HEADER_SUPPORTED = "Supported";
        public const string SIP_HEADER_TIMESTAMP = "Timestamp";
		public const string SIP_HEADER_TO = "To";
        public const string SIP_HEADER_UNSUPPORTED = "Unsupported";
        public const string SIP_HEADER_USERAGENT = "User-Agent";
		public const string SIP_HEADER_VIA = "Via";
        public const string SIP_HEADER_WARNING = "Warning";
        public const string SIP_HEADER_WWWAUTHENTICATE = "WWW-Authenticate";

		// SIP Compact Header Keys.
        public const string SIP_COMPACTHEADER_ALLOWEVENTS = "u";        // RC3265 (SIP Events).
		public const string SIP_COMPACTHEADER_CALLID = "i";
		public const string SIP_COMPACTHEADER_CONTACT = "m";
        public const string SIP_COMPACTHEADER_CONTENTLENGTH = "l";
        public const string SIP_COMPACTHEADER_CONTENTTYPE = "c";
        public const string SIP_COMPACTHEADER_EVENT = "o";              // RC3265 (SIP Events).
		public const string SIP_COMPACTHEADER_FROM = "f";
        public const string SIP_COMPACTHEADER_REFERTO = "r";            // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_COMPACTHEADER_SUBJECT = "s";
        public const string SIP_COMPACTHEADER_SUPPORTED = "k";
        public const string SIP_COMPACTHEADER_TO = "t";
        public const string SIP_COMPACTHEADER_VIA = "v";

        // Custom SIP headers to allow proxy to communicate network info to internal servers.
        public const string SIP_HEADER_PROXY_RECEIVEDON = "Proxy-ReceivedOn";
        public const string SIP_HEADER_PROXY_RECEIVEDFROM = "Proxy-ReceivedFrom";
        public const string SIP_HEADER_PROXY_SENDFROM = "Proxy-SendFrom";

        // Custom SIP headers to interact with the SIP Sorcery switchboard.
        public const string SIP_HEADER_SWITCHBOARD_ORIGINAL_CALLID = "Switchboard-OriginalCallID";
        //public const string SIP_HEADER_SWITCHBOARD_CALLER_DESCRIPTION = "Switchboard-CallerDescription";
        public const string SIP_HEADER_SWITCHBOARD_LINE_NAME = "Switchboard-LineName";
        //public const string SIP_HEADER_SWITCHBOARD_ORIGINAL_FROM = "Switchboard-OriginalFrom";
        //public const string SIP_HEADER_SWITCHBOARD_FROM_CONTACT_URL = "Switchboard-FromContactURL";
        public const string SIP_HEADER_SWITCHBOARD_OWNER = "Switchboard-Owner";
        //public const string SIP_HEADER_SWITCHBOARD_ORIGINAL_TO = "Switchboard-OriginalTo";
        public const string SIP_HEADER_SWITCHBOARD_TERMINATE = "Switchboard-Terminate";
        //public const string SIP_HEADER_SWITCHBOARD_TOKEN = "Switchboard-Token";
        //public const string SIP_HEADER_SWITCHBOARD_TOKENREQUEST = "Switchboard-TokenRequest";

        // Custom SIP headers for CRM integration.
        public const string SIP_HEADER_CRM_PERSON_NAME = "CRM-PersonName";
        public const string SIP_HEADER_CRM_COMPANY_NAME = "CRM-CompanyName";
        public const string SIP_HEADER_CRM_PICTURE_URL = "CRM-PictureURL";
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

		INFO = 8,           // RFC2976.
		NOTIFY = 9,         // RFC3265.
        SUBSCRIBE = 10,     // RFC3265.
		PUBLISH = 11,       // RFC3903.
		PING = 13,
		REFER = 14,         // RFC3515 "The Session Initiation Protocol (SIP) Refer Method"
        MESSAGE = 15,       // RFC3428.
        PRACK = 16,         // RFC3262.
        UPDATE = 17,        // RFC3311.
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
        Accepted = 202,                         // RC3265 (SIP Events).
        NoNotification = 204,

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
        Gone = 410,
        ConditionalRequestFailed = 412,
        RequestEntityTooLarge = 413,
        RequestURITooLong = 414,
        UnsupportedMediaType = 415,
        UnsupportedURIScheme = 416,
        UnknownResourcePriority = 417,
        BadExtension = 420,
        ExtensionRequired = 421,
        SessionIntervalTooSmall = 422,
        IntervalTooBrief = 423,
        UseIdentityHeader = 428,
        ProvideReferrerIdentity = 429,
        FlowFailed = 430,
        AnonymityDisallowed = 433,
        BadIdentityInfo = 436,
        UnsupportedCertificate = 437,
        InvalidIdentityHeader = 438,
        FirstHopLacksOutboundSupport = 439,
        MaxBreadthExceeded = 440,
        ConsentNeeded = 470,
		TemporarilyUnavailable = 480,
		CallLegTransactionDoesNotExist = 481,
		LoopDetected = 482,
		TooManyHops = 483,
        AddressIncomplete = 484,
        Ambiguous = 485,
		BusyHere = 486,
		RequestTerminated = 487,
        NotAcceptableHere = 488,
        BadEvent = 489,                         // RC3265 (SIP Events).
        RequestPending = 491,
        Undecipherable = 493,
		SecurityAgreementRequired = 580,
        
		// Server Failure.
		InternalServerError = 500,
        NotImplemented = 501,
        BadGateway = 502,
        ServiceUnavailable = 503,
		ServerTimeout = 504,
        SIPVersionNotSupported = 505,
		MessageTooLarge = 513,
        PreconditionFailure = 580,

		// Global Failures.
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

    public class SIPMIMETypes
    {
        public const string DIALOG_INFO_CONTENT_TYPE = "application/dialog-info+xml";   // RFC4235 INVITE dialog event package.
        public const string MWI_CONTENT_TYPE = "application/simple-message-summary";    // RFC3842 MWI event package.
        public const string REFER_CONTENT_TYPE = "message/sipfrag";                     // RFC3515 REFER event package.
        public const string MWI_TEXT_TYPE = "text/text";
        public const string PRESENCE_NOTIFY_CONTENT_TYPE = "application/pidf+xml";      // RFC3856 presence event package.
    }
    /// <summary>
    /// For SIP URI user portion the reserved characters below need to be escaped.
    /// reserved    =  ";" / "/" / "?" / ":" / "@" / "&" / "=" / "+"  / "$" / ","
    /// user-unreserved  =  "&" / "=" / "+" / "$" / "," / ";" / "?" / "/"
    /// Leaving to be escaped = ":" / "@" 
    /// 
    /// For SIP URI parameters different characters are unreserved (just to amke life difficult).
    /// reserved    =  ";" / "/" / "?" / ":" / "@" / "&" / "=" / "+"  / "$" / ","
    /// param-unreserved = "[" / "]" / "/" / ":" / "&" / "+" / "$"
    /// Leaving to be escaped =  ";" / "?" / "@" / "=" / ","
    /// </summary>
    public static class SIPEscape
    {
        public static string SIPURIUserEscape(string unescapedString)
        {
            string result = unescapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace(":", "%3A");
                result = result.Replace("@", "%40");
                result = result.Replace(" ", "%20");
            }
            return result;
        }

        public static string SIPURIUserUnescape(string escapedString)
        {
            string result = escapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace("%3A", ":");
                result = result.Replace("%3a", ":");
                result = result.Replace("%20", " ");
            }
            return result;
        }

        public static string SIPURIParameterEscape(string unescapedString)
        {
            string result = unescapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace(";", "%3B");
                result = result.Replace("?", "%3F");
                result = result.Replace("@", "%40");
                result = result.Replace("=", "%3D");
                result = result.Replace(",", "%2C");
                result = result.Replace(" ", "%20");
            }
            return result;
        }

        public static string SIPURIParameterUnescape(string escapedString)
        {
            string result = escapedString;
            if (!result.IsNullOrBlank())
            {
                result = result.Replace("%3B", ";");
                result = result.Replace("%3b", ";");
                //result = result.Replace("%2F", "/");
                //result = result.Replace("%2f", "/");
                result = result.Replace("%3F", "?");
                result = result.Replace("%3f", "?");
                //result = result.Replace("%3A", ":");
                //result = result.Replace("%3a", ":");
                result = result.Replace("%40", "@");
                //result = result.Replace("%26", "&");
                result = result.Replace("%3D", "=");
                result = result.Replace("%3d", "=");
                //result = result.Replace("%2B", "+");
                //result = result.Replace("%2b", "+");
                //result = result.Replace("%24", "$");
                result = result.Replace("%2C", ",");
                result = result.Replace("%2c", ",");
                result = result.Replace("%20", " ");
            }
            return result;
        }
    }
}