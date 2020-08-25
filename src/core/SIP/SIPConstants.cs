//-----------------------------------------------------------------------------
// Filename: SIPConstants.cs
//
// Description: SIP constants.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Sep 2005	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPConstants
    {
        public const string CRLF = "\r\n";

        public const string SIP_VERSION_STRING = "SIP";
        public const int SIP_MAJOR_VERSION = 2;
        public const int SIP_MINOR_VERSION = 0;
        public const string SIP_FULLVERSION_STRING = "SIP/2.0";

        public const int NONCE_TIMEOUT_MINUTES = 5;                         // Length of time an issued nonce is valid for.

        /// <summary>
        /// The maximum size supported for an incoming SIP message.
        /// </summary>
        /// <remarks>
        /// From https://tools.ietf.org/html/rfc3261#section-18.1.1:
        /// However, implementations MUST be able to handle messages up to the maximum
        /// datagram packet size.For UDP, this size is 65,535 bytes, including
        /// IP and UDP headers.
        /// </remarks>
        public const int SIP_MAXIMUM_RECEIVE_LENGTH = 65535;

        public const string SIP_REQUEST_REGEX = @"^\w+ .* SIP/.*";          // bnf:	Request-Line = Method SP Request-URI SP SIP-Version CRLF
        public const string SIP_RESPONSE_REGEX = @"^SIP/.* \d{3}";          // bnf: Status-Line = SIP-Version SP Status-Code SP Reason-Phrase CRLF
        public const string SIP_BRANCH_MAGICCOOKIE = "z9hG4bK";
        public const string SIP_DEFAULT_USERNAME = "Anonymous";
        public const string SIP_DEFAULT_FROMURI = "sip:thisis@anonymous.invalid";
        public const string SIP_REGISTER_REMOVEALL = "*";                   // The value in a REGISTER request id a UA wishes to remove all REGISTER bindings.
        public const string SIP_LOOSEROUTER_PARAMETER = "lr";
        public const string SIP_REMOTEHANGUP_CAUSE = "remote end hungup";
        public const char HEADER_DELIMITER_CHAR = ':';

        public const int DEFAULT_MAX_FORWARDS = 70;
        public const int DEFAULT_REGISTEREXPIRY_SECONDS = 600;
        public const int DEFAULT_SIP_PORT = 5060;
        public const int DEFAULT_SIP_TLS_PORT = 5061;
        public const int DEFAULT_SIP_WEBSOCKET_PORT = 80;
        public const int DEFAULT_SIPS_WEBSOCKET_PORT = 443;

        public const string NAT_SENDKEEPALIVES_VALUE = "y";

        public const string ALLOWED_SIP_METHODS = "ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE";

        private static string _userAgentVersion;
        public static string SIP_USERAGENT_STRING
        {
            get
            {
                if (_userAgentVersion == null)
                {
                    _userAgentVersion = $"sipsorcery_v{Assembly.GetExecutingAssembly().GetName().Version.ToString()}";
                }

                return _userAgentVersion;
            }
        }

        /// <summary>
        /// Gets the default SIP port for the protocol. 
        /// </summary>
        /// <param name="protocol">The transport layer protocol to get the port for.</param>
        /// <returns>The default port to use.</returns>
        public static int GetDefaultPort(SIPProtocolsEnum protocol)
        {
            switch (protocol)
            {
                case SIPProtocolsEnum.udp:
                    return SIPConstants.DEFAULT_SIP_PORT;
                case SIPProtocolsEnum.tcp:
                    return SIPConstants.DEFAULT_SIP_PORT;
                case SIPProtocolsEnum.tls:
                    return SIPConstants.DEFAULT_SIP_TLS_PORT;
                case SIPProtocolsEnum.ws:
                    return SIPConstants.DEFAULT_SIP_WEBSOCKET_PORT;
                case SIPProtocolsEnum.wss:
                    return SIPConstants.DEFAULT_SIPS_WEBSOCKET_PORT;
                default:
                    throw new ApplicationException($"Protocol {protocol} was not recognised in GetDefaultPort.");
            }
        }
    }

    public enum SIPMessageTypesEnum
    {
        Unknown = 0,
        Request = 1,
        Response = 2,
    }

    public class SIPTimings
    {
        /// <summary>
        /// Value of the SIP defined timer T1 in milliseconds and is the time for the first retransmit.
        /// Should not need to be adjusted in normal circumstances.
        /// </summary>
        public static int T1 = 500;

        /// <summary>
        /// Value of the SIP defined timer T2 in milliseconds and is the maximum time between retransmits.
        /// Should not need to be adjusted in normal circumstances.
        /// </summary>
        public static int T2 = 4000;

        /// <summary>
        /// Value of the SIP defined timer T6 in milliseconds and is the period after which a transaction 
        /// has timed out. Should not need to be adjusted in normal circumstances.
        /// </summary>
        public static int T6 = 64 * T1;

        /// <summary>
        /// The number of milliseconds a transaction can stay in the proceeding state 
        /// (i.e. an INVITE will ring for) before the call is given up and timed out.     
        /// </summary>
        public static int MAX_RING_TIME = 180000;
    }

    public enum SIPSchemesEnum
    {
        sip = 1,
        sips = 2,
        tel = 3,
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

    /// <summary>
    /// A list of the transport layer protocols that are supported (the network layers
    /// supported are IPv4 mad IPv6).
    /// </summary>
    public enum SIPProtocolsEnum
    {
        /// <summary>
        /// User Datagram Protocol.
        /// </summary>
        udp = 1,
        /// <summary>.
        /// Transmission Control Protocol
        /// </summary>
        tcp = 2,
        /// <summary>
        /// Transport Layer Security.
        /// </summary>
        tls = 3,
        /// <summary>
        /// Web Socket.
        /// </summary>
        ws = 4,
        /// <summary>
        /// Web Socket over TLS.
        /// </summary>
        wss = 5,
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
        public const string SIP_HEADER_RELIABLE_ACK = "RAck";                       // RFC 3262 "The RAck header is sent in a PRACK request to support reliability of provisional responses."
        public const string SIP_HEADER_REASON = "Reason";
        public const string SIP_HEADER_RECORDROUTE = "Record-Route";
        public const string SIP_HEADER_REFERREDBY = "Referred-By";                  // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_HEADER_REFERSUB = "Refer-Sub";                      // RFC 4488 Used to stop the implicit SIP event subscription on a REFER request.
        public const string SIP_HEADER_REFERTO = "Refer-To";                        // RFC 3515 "The Session Initiation Protocol (SIP) Refer Method".
        public const string SIP_HEADER_REPLY_TO = "Reply-To";
        public const string SIP_HEADER_REPLACES = "Replaces";
        public const string SIP_HEADER_REQUIRE = "Require";
        public const string SIP_HEADER_RETRY_AFTER = "Retry-After";
        public const string SIP_HEADER_RELIABLE_SEQ = "RSeq";                     // RFC 3262 "The RSeq header is used in provisional responses in order to transmit them reliably."
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
        public const string SIP_HEADERANC_MADDR = "maddr";

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

    /// <summary>
    /// A list of the different SIP request methods that are supported.
    /// </summary>
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
            catch { }

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
    /// 
    /// <code>
    /// <![CDATA[
    /// reserved    =  ";" / "/" / "?" / ":" / "@" / "&" / "=" / "+"  / "$" / ","
    /// user-unreserved  =  "&" / "=" / "+" / "$" / "," / ";" / "?" / "/"
    /// Leaving to be escaped = ":" / "@" 
    /// ]]>
    /// </code>
    /// 
    /// For SIP URI parameters different characters are unreserved (just to make life difficult).
    /// <code>
    /// <![CDATA[
    /// reserved    =  ";" / "/" / "?" / ":" / "@" / "&" / "=" / "+"  / "$" / ","
    /// param-unreserved = "[" / "]" / "/" / ":" / "&" / "+" / "$"
    /// Leaving to be escaped =  ";" / "?" / "@" / "=" / ","
    /// ]]>
    /// </code>
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

    ///<summary>
    /// List of SIP extensions to RFC3262.
    /// </summary> 
    public enum SIPExtensions
    {
        None = 0,
        Prack = 1,          // Reliable provisional responses as per RFC3262.
        NoReferSub = 2,     // No subscription for REFERs as per RFC4488.
        Replaces = 3,
    }

    /// <summary>
    /// Constants that can be placed in the SIP Supported or Required headers to indicate support or mandate for
    /// a particular SIP extension.
    /// </summary>
    public class SIPExtensionHeaders
    {
        public const string PRACK = "100rel";
        public const string NO_REFER_SUB = "norefersub";
        public const string REPLACES = "replaces";

        /// <summary>
        /// Parses a string containing a list of SIP extensions into a list of extensions that this library
        /// understands.
        /// </summary>
        /// <param name="extensionList">The string containing the list of extensions to parse.</param>
        /// <param name="unknownExtensions">A comma separated list of the unsupported extensions.</param>
        /// <returns>A list of extensions that were understood and a boolean indicating whether any unknown extensions were present.</returns>
        public static List<SIPExtensions> ParseSIPExtensions(string extensionList, out string unknownExtensions)
        {
            List<SIPExtensions> knownExtensions = new List<SIPExtensions>();
            unknownExtensions = null;

            if (String.IsNullOrEmpty(extensionList) == false)
            {
                string[] extensions = extensionList.Trim().Split(',');

                foreach (string extension in extensions)
                {
                    if (String.IsNullOrEmpty(extension) == false)
                    {
                        if (extension.Trim().ToLower() == PRACK)
                        {
                            knownExtensions.Add(SIPExtensions.Prack);
                        }
                        else if (extension.Trim().ToLower() == NO_REFER_SUB)
                        {
                            knownExtensions.Add(SIPExtensions.NoReferSub);
                        }
                        else if (extension.Trim().ToLower() == REPLACES)
                        {
                            knownExtensions.Add(SIPExtensions.Replaces);
                        }
                        else
                        {
                            unknownExtensions += (unknownExtensions != null) ? $",{extension.Trim()}" : extension.Trim();
                        }
                    }
                }
            }

            return knownExtensions;
        }
    }
}