// ============================================================================
// FileName: SIPCallDescriptor.cs
//
// Description:
// Used to hold all the fields needed to place a SIP call.
//
// Author(s):
// Aaron Clauson
//
// History:
// 10 Aug 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 04 Oct 2008  Aaron Clauson   Added AuthUsername.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public enum SIPCallRedirectModesEnum
    {
        None = 0,
        //Add = 1,        // (option=a)
        //Replace = 2,    // (option=r)
        NewDialPlan = 3,// (option=n)
        Manual = 4,      // (option=m) Means don't do anything with a redirect response. Let the user handle it in their dialplan.
    }

    public class CRMHeaders
    {
        public string PersonName;
        public string CompanyName;
        public string AvatarURL;
        public bool Pending = true;
        public string LookupError;

        public CRMHeaders()
        { }

        public CRMHeaders(string personName, string companyName, string avatarURL)
        {
            PersonName = personName;
            CompanyName = companyName;
            AvatarURL = avatarURL;
            Pending = false;
        }
    }

    public partial class SIPCallDescriptor
    {
        private const int MAX_REINVITE_DELAY = 5;
        private const int DEFAULT_REINVITE_DELAY = 2;

        public const string DELAY_CALL_OPTION_KEY = "dt";       // Dial string option to delay the start of a call leg.
        public const string REDIRECT_MODE_OPTION_KEY = "rm";    // Dial string option to set the redirect mode of a call leg. Redirect mode refers to how 3xx responses to a call are handled.
        public const string CALL_DURATION_OPTION_KEY = "cd";    // Dial string option used to set the maximum duration of a call in seconds.
        public const string MANGLE_MODE_OPTION_KEY = "ma";      // Dial string option used to specify whether the SDP should be mangled. Default is true.
        public const string FROM_DISPLAY_NAME_KEY = "fd";       // Dial string option to customise the From header display name on the call request.
        public const string FROM_USERNAME_KEY = "fu";           // Dial string option to customise the From header SIP URI User on the call request.
        public const string FROM_HOST_KEY = "fh";               // Dial string option to customise the From header SIP URI Host on the call request.
        public const string TRANSFER_MODE_OPTION_KEY = "tr";    // Dial string option to dictate how REFER (transfer) requests will be handled.
        public const string REQUEST_CALLER_DETAILS = "rcd";     // Dial string option to indicate the client agent would like any caller details if/when available.
        public const string ACCOUNT_CODE_KEY = "ac";            // Dial string option which indicates that the call leg is billable and the account code it should be billed against.
        public const string RATE_CODE_KEY = "rc";               // Dial string option which indicates the rate code a billable call leg should use. If no rate code is specified then the rate will be looked up based on the call destination.
        public const string DELAYED_REINVITE_KEY = "dr";        // Dial string option to initiate a re-INVITE request when a call is answered in an attempt to solve a one way audio problem.

        // Switchboard dial string options.
        public const string SWITCHBOARD_LINE_NAME_KEY = "swln";             // Dial string option to set the Switchboard-LineName header on the call leg.
                                                                            //public const string SWITCHBOARD_DIALOGUE_DESCRIPTION_KEY = "swdd";// Dial string option to set a value for the switchboard description field on the answered dialogue. It does not set a header.
        public const string SWITCHBOARD_CALLID_KEY = "swcid";               // Dial string option to set the Switchboard-CallID header on the call leg.
        public const string SWITCHBOARD_OWNER_KEY = "swo";                  // Dial string option to set the Switchboard-Owner header on the call leg.

        private readonly static string m_defaultFromURI = SIPConstants.SIP_DEFAULT_FROMURI;
        private readonly static string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static char m_customHeadersSeparator = '|';                 // Must match SIPProvider.CUSTOM_HEADERS_SEPARATOR.
        private const string DELAY_CALL_REGEX_PATTERN = @"dt=(?<delaytime>\d+)";
        private const string REDIRECT_MODE_REGEX_PATTERN = @"rm=(?<redirectmode>\w)";
        private const string CALL_DURATION_REGEX_PATTERN = @"cd=(?<callduration>\d+)";
        private const string MANGLE_MODE_REGEX_PATTERN = @"ma=(?<mangle>\w+)";
        private const string FROM_DISPLAY_NAME_REGEX_PATTERN = @"fd=(?<displayname>.+?)(,|$)";
        private const string FROM_USERNAME_REGEX_PATTERN = @"fu=(?<username>.+?)(,|$)";
        private const string FROM_HOST_REGEX_PATTERN = @"fh=(?<host>.+?)(,|$)";
        private const string TRANSFER_MODE_REGEX_PATTERN = @"tr=(?<transfermode>.+?)(,|$)";
        private const string CALLER_DETAILS_REGEX_PATTERN = @"rcd=(?<callerdetails>\w+)";
        private const string ACCOUNT_CODE_REGEX_PATTERN = @"ac=(?<accountCode>\w+)";
        private const string RATE_CODE_REGEX_PATTERN = @"rc=(?<rateCode>\w+)";
        private const string DELAYED_REINVITE_REGEX_PATTERN = @"dr=(?<delayedReinvite>\d+)";
        private const string IMMEDIATE_REINVITE_REGEX_PATTERN = @"ir=\w+";

        private static readonly ILogger logger = LogFactory.CreateLogger<SIPCallDescriptor>();

#if NET7_0_OR_GREATER
        [GeneratedRegex(DELAY_CALL_REGEX_PATTERN)]
        private static partial Regex DelayCallRegex();

        [GeneratedRegex(REDIRECT_MODE_REGEX_PATTERN)]
        private static partial Regex RedirectModeRegex();

        [GeneratedRegex(CALL_DURATION_REGEX_PATTERN)]
        private static partial Regex CallDurationRegex();

        [GeneratedRegex(MANGLE_MODE_REGEX_PATTERN)]
        private static partial Regex MangleModeRegex();

        [GeneratedRegex(FROM_DISPLAY_NAME_REGEX_PATTERN)]
        private static partial Regex FromDisplayNameRegex();

        [GeneratedRegex(FROM_USERNAME_REGEX_PATTERN)]
        private static partial Regex FromUsernameRegex();

        [GeneratedRegex(FROM_HOST_REGEX_PATTERN)]
        private static partial Regex FromHostRegex();

        [GeneratedRegex(TRANSFER_MODE_REGEX_PATTERN)]
        private static partial Regex TransferModeRegex();

        [GeneratedRegex(CALLER_DETAILS_REGEX_PATTERN)]
        private static partial Regex CallerDetailsRegex();

        [GeneratedRegex(ACCOUNT_CODE_REGEX_PATTERN)]
        private static partial Regex AccountCodeRegex();

        [GeneratedRegex(RATE_CODE_REGEX_PATTERN)]
        private static partial Regex RateCodeRegex();

        [GeneratedRegex(DELAYED_REINVITE_REGEX_PATTERN)]
        private static partial Regex DelayedReinviteRegex();

        [GeneratedRegex(IMMEDIATE_REINVITE_REGEX_PATTERN)]
        private static partial Regex ImmediateReinviteRegex();
#else
        private static readonly Regex m_delayCallRegex = new Regex(DELAY_CALL_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_redirectModeRegex = new Regex(REDIRECT_MODE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_callDurationRegex = new Regex(CALL_DURATION_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_mangleModeRegex = new Regex(MANGLE_MODE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_fromDisplayNameRegex = new Regex(FROM_DISPLAY_NAME_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_fromUsernameRegex = new Regex(FROM_USERNAME_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_fromHostRegex = new Regex(FROM_HOST_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_transferModeRegex = new Regex(TRANSFER_MODE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_callerDetailsRegex = new Regex(CALLER_DETAILS_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_accountCodeRegex = new Regex(ACCOUNT_CODE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_rateCodeRegex = new Regex(RATE_CODE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_delayedReinviteRegex = new Regex(DELAYED_REINVITE_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_immediateReinviteRegex = new Regex(IMMEDIATE_REINVITE_REGEX_PATTERN, RegexOptions.Compiled);

        private static Regex DelayCallRegex() => m_delayCallRegex;
        private static Regex RedirectModeRegex() => m_redirectModeRegex;
        private static Regex CallDurationRegex() => m_callDurationRegex;
        private static Regex MangleModeRegex() => m_mangleModeRegex;
        private static Regex FromDisplayNameRegex() => m_fromDisplayNameRegex;
        private static Regex FromUsernameRegex() => m_fromUsernameRegex;
        private static Regex FromHostRegex() => m_fromHostRegex;
        private static Regex TransferModeRegex() => m_transferModeRegex;
        private static Regex CallerDetailsRegex() => m_callerDetailsRegex;
        private static Regex AccountCodeRegex() => m_accountCodeRegex;
        private static Regex RateCodeRegex() => m_rateCodeRegex;
        private static Regex DelayedReinviteRegex() => m_delayedReinviteRegex;
        private static Regex ImmediateReinviteRegex() => m_immediateReinviteRegex;
#endif

        public string Username;                 // The username that will be used in the From header and to authenticate the call unless overridden by AuthUsername.
        public string AuthUsername;             // The username that will be used from authentication. Optional setting only needed if the From header user needs to be different from the digest username.
        public string Password;                 // The password that will be used to authenticate the call if required.
        public string Uri;                      // A string representing the URI the call will be forwarded with.
        public string From;                     // A string representing the From header to be set for the call.
        public string To;                       // A string representing the To header to be set for the call.  
        public string RouteSet;                 // A route set for the forwarded call request. If there is only a single route or IP socket it will be treated like an Outbound Proxy (i.e. no Route header will be added).
        public string ProxySendFrom;            // Used to set the Proxy-SendFrom header which informs an upstream proxy which socket the request should try and go out on.
        public List<string> CustomHeaders;      // An optional list of custom SIP headers that will be added to the INVITE request.
        public SIPCallDirection CallDirection;  // Indicates whether the call is incoming out outgoing relative to this server. An outgoing call is one that is placed by a user the server authenticates.
        public string ContentType;
        public string Content;
        public int DelaySeconds;                        // An amount in seconds to delay the initiation of this call when used as part of a dial string.
        public SIPCallRedirectModesEnum RedirectMode;   // Determines how the call will handle 3xx redirect responses.
        public int CallDurationLimit;                   // If non-zero sets a limit on the duration of any call created with this descriptor.
        public bool MangleResponseSDP = true;           // If false indicates the response SDP should be left alone if it contains a private IP address.
        public IPAddress MangleIPAddress;               // If mangling is required on this call this address needs to be set as the one to mangle to.
        public string FromDisplayName;
        public string FromURIUsername;
        public string FromURIHost;
        public SIPDialogueTransferModesEnum TransferMode = SIPDialogueTransferModesEnum.Default;   // Determines how the call (dialogues) created by this descriptor will handle transfers (REFER requests).
        public bool RequestCallerDetails;       // If true indicates the client agent would like to pass on any caller details if/when available.
        public Guid DialPlanContextID;
        public int ReinviteDelay = -1;          // If >= 0 a SIP re-INVITE request will be sent to the remote caller after this many seconds. This is an attempt to work around a bug with one way audio and early media on a particular SIP server.

        //rj2
        /// <summary>
        /// A string representing the Call Identifier
        /// defaults to <see cref="CallProperties.CreateNewCallId()"/> if not set
        /// 
        /// CallId MUST be unique between different calls
        /// </summary>
        public string CallId;
        /// <summary>
        /// A string representing the Branch part of the SIP-VIA header to identify Call-Requests and Call-Responses
        /// defaults to <see cref="CallProperties.CreateBranchId()"/> if not set
        /// 
        /// BranchId MUST be unique between different calls and even requests
        /// BranchId MUST start with "z9hG4bK"
        /// </summary>
        /// <remarks>
        /// to avoid unexpected behaviour:
        /// BranchId should only be customized in fully controlled enclosed environments
        /// or for testing purposes 
        /// </remarks>
        public string BranchId;

        // Real-time call control variables.
        public string AccountCode;          // If set indicates this is a billable call and this is the account code to bill the call against.
        public string RateCode;             // If set indicates and the call is billable indicates the rate code that should be used to determine the rate for the call.

        public CRMHeaders CRMHeaders;

        // Properties needed for Google Voice calls.
        public bool IsGoogleVoiceCall;
        public string CallbackNumber;
        public string CallbackPattern;
        public int CallbackPhoneType;

        public ISIPAccount ToSIPAccount;         // If non-null indicates the call is for a SIP Account on the same server. An example of using this it to call from one user into another user's dialplan.

        public ManualResetEvent DelayMRE;       // If the call needs to be delayed DelaySeconds this MRE will be used.

        /// <summary>
        /// This constructor is for calls to a SIP account that the application recognises as belonging to it.
        /// </summary>
        /// <param name="toSIPAccount">The destination SP account for teh call.</param>
        /// <param name="uri">The uri can be different to the to SIP account if a dotted notation is used. For
        /// example 1234.user@sipsorcery.com.</param>
        /// <param name="fromHeader"></param>
        /// <param name="contentType"></param>
        /// <param name="content"></param>
        public SIPCallDescriptor(ISIPAccount toSIPAccount, string uri, string fromHeader, string contentType, string content)
        {
            ToSIPAccount = toSIPAccount;
            Uri = uri ?? $"{toSIPAccount.SIPUsername}@{toSIPAccount.SIPDomain}";
            From = fromHeader;
            ContentType = contentType;
            Content = content;
        }

        /// <summary>
        /// This constructor is for non-authenticated calls that do not require any custom
        /// headers etc.
        /// </summary>
        /// <param name="dstUri">The destination URI to place the call to.</param>
        /// <param name="sdp">The Session Description Protocol (SDP) body to use in the call request.
        /// Can be empty if the remote party supports SDP answers via ACK requests.</param>
        public SIPCallDescriptor(
            string dstUri,
            string sdp)
        {
            if (string.IsNullOrWhiteSpace(dstUri))
            {
                throw new ArgumentNullException(nameof(dstUri), "A destination must be supplied when creating a SIPCallDescriptor.");
            }

            Uri = SIPURI.ParseSIPURIRelaxed(dstUri).ToString();
            From = m_defaultFromURI;
            To = Uri;
            ContentType = (sdp != null) ? m_sdpContentType : null;
            Content = sdp;
        }

        public SIPCallDescriptor(
            string username,
            string password,
            string uri,
            string from,
            string to,
            string routeSet,
            List<string> customHeaders,
            string authUsername,
            SIPCallDirection callDirection,
            string contentType,
            string content,
            IPAddress mangleIPAddress)
        {
            Username = username;
            Password = password;
            Uri = uri;
            From = from ?? m_defaultFromURI;
            To = to ?? uri;
            RouteSet = routeSet;
            CustomHeaders = customHeaders ?? new List<string>();
            AuthUsername = authUsername;
            CallDirection = callDirection;
            ContentType = contentType;
            Content = content;
            MangleIPAddress = mangleIPAddress;
        }

        public SIPCallDescriptor(
            string username,
            string password,
            string uri,
            string callbackNumber,
            string callbackPattern,
            int callbackPhoneType,
            string content,
            string contentType,
            IPAddress mangleIPAddress)
        {
            IsGoogleVoiceCall = true;
            Username = username;
            Password = password;
            Uri = uri;
            CallbackNumber = callbackNumber;
            CallbackPattern = callbackPattern;
            CallbackPhoneType = callbackPhoneType;
            ContentType = contentType;
            Content = content;
            MangleIPAddress = mangleIPAddress;
        }

        public SIPFromHeader GetFromHeader()
        {
            SIPFromHeader fromHeader = SIPFromHeader.ParseFromHeader(From);

            if (!FromDisplayName.IsNullOrBlank())
            {
                fromHeader.FromName = FromDisplayName;
            }
            if (!FromURIUsername.IsNullOrBlank())
            {
                fromHeader.FromURI.User = FromURIUsername;
            }
            if (!FromURIHost.IsNullOrBlank())
            {
                fromHeader.FromURI.Host = FromURIHost;
            }

            return fromHeader;
        }

        /// <summary>
        /// These setting do NOT override the ones from the call options.
        /// </summary>
        /// <param name="fromDisplayName"></param>
        /// <param name="fromUsername"></param>
        /// <param name="fromHost"></param>
        public void SetGeneralFromHeaderFields(string fromDisplayName, string fromUsername, string fromHost)
        {
            if (!fromDisplayName.IsNullOrBlank() && FromDisplayName == null)
            {
                FromDisplayName = fromDisplayName.Trim();
            }

            if (!fromUsername.IsNullOrBlank() && FromURIUsername == null)
            {
                FromURIUsername = fromUsername.Trim();
            }

            if (!fromHost.IsNullOrBlank() && FromURIHost == null)
            {
                FromURIHost = fromHost.Trim();
            }
        }

        public void ParseCallOptions(string options)
        {
            if (!options.IsNullOrBlank())
            {
                options = options.Trim('[', ']');

                // Parse delay time option.
                var delayCallMatch = DelayCallRegex().Match(options);
                if (delayCallMatch.Success)
                {
                    int.TryParse(delayCallMatch.Result("${delaytime}"), out DelaySeconds);
                }

                // Parse redirect mode option.
                var redirectModeMatch = RedirectModeRegex().Match(options);
                if (redirectModeMatch.Success)
                {
                    string redirectMode = redirectModeMatch.Result("${redirectmode}");
                    //if (redirectMode is "a" or "A")
                    //{
                    //    RedirectMode = SIPCallRedirectModesEnum.Add;
                    //}
                    //else if (redirectMode is "r" or "R")
                    //{
                    //    RedirectMode = SIPCallRedirectModesEnum.Replace;
                    //}
                    if (redirectMode is "n" or "N")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.NewDialPlan;
                    }
                    else if (redirectMode is "m" or "M")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.Manual;
                    }
                }

                // Parse call duration limit option.
                var callDurationMatch = CallDurationRegex().Match(options);
                if (callDurationMatch.Success)
                {
                    int.TryParse(callDurationMatch.Result("${callduration}"), out CallDurationLimit);
                }

                // Parse the mangle option.
                var mangleMatch = MangleModeRegex().Match(options);
                if (mangleMatch.Success)
                {
                    bool.TryParse(mangleMatch.Result("${mangle}"), out MangleResponseSDP);
                }

                // Parse the From header display name option.
                var fromDisplayNameMatch = FromDisplayNameRegex().Match(options);
                if (fromDisplayNameMatch.Success)
                {
                    FromDisplayName = fromDisplayNameMatch.Result("${displayname}").Trim();
                }

                // Parse the From header URI username option.
                var fromUsernameNameMatch = FromUsernameRegex().Match(options);
                if (fromUsernameNameMatch.Success)
                {
                    FromURIUsername = fromUsernameNameMatch.Result("${username}").Trim();
                }

                // Parse the From header URI host option.
                var fromURIHostMatch = FromHostRegex().Match(options);
                if (fromURIHostMatch.Success)
                {
                    FromURIHost = fromURIHostMatch.Result("${host}").Trim();
                }

                // Parse the Transfer behaviour option.
                var transferMatch = TransferModeRegex().Match(options);
                if (transferMatch.Success)
                {
                    string transferMode = transferMatch.Result("${transfermode}");
                    if (transferMode is "n" or "N")
                    {
                        TransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                    }
                    else if (transferMode is "p" or "P")
                    {
                        TransferMode = SIPDialogueTransferModesEnum.PassThru;
                    }
                    else if (transferMode is "c" or "C")
                    {
                        TransferMode = SIPDialogueTransferModesEnum.BlindPlaceCall;
                    }
                    /*else if (transferMode == "o" || transferMode == "O")
                    {
                        TransferMode = SIPCallTransferModesEnum.Caller;
                    }
                    else if (transferMode == "d" || transferMode == "D")
                    {
                        TransferMode = SIPCallTransferModesEnum.Callee;
                    }
                    else if (transferMode == "b" || transferMode == "B")
                    {
                        TransferMode = SIPCallTransferModesEnum.Both;
                    }*/
                }

                // Parse the request caller details option.
                var callerDetailsMatch = CallerDetailsRegex().Match(options);
                if (callerDetailsMatch.Success)
                {
                    bool.TryParse(callerDetailsMatch.Result("${callerdetails}"), out RequestCallerDetails);
                }

                // Parse the accountcode.
                var accountCodeMatch = AccountCodeRegex().Match(options);
                if (accountCodeMatch.Success)
                {
                    AccountCode = accountCodeMatch.Result("${accountCode}");
                }

                // Parse the rate code.
                var rateCodeMatch = RateCodeRegex().Match(options);
                if (rateCodeMatch.Success)
                {
                    RateCode = rateCodeMatch.Result("${rateCode}");
                }

                // Parse the delayed reinvite option.
                var delayedReinviteMatch = DelayedReinviteRegex().Match(options);
                if (delayedReinviteMatch.Success)
                {
                    int.TryParse(delayedReinviteMatch.Result("${delayedReinvite}"), out ReinviteDelay);

                    if (ReinviteDelay > MAX_REINVITE_DELAY)
                    {
                        ReinviteDelay = DEFAULT_REINVITE_DELAY;
                    }
                }

                // Parse the immediate reinvite option (TODO: remove after user switches to delayed reinvite option).
                var immediateReinviteMatch = ImmediateReinviteRegex().Match(options);
                if (immediateReinviteMatch.Success)
                {
                    ReinviteDelay = DEFAULT_REINVITE_DELAY;
                }
            }
        }

        public static List<string> ParseCustomHeaders(string customHeaders)
        {
            List<string> customHeaderList = new List<string>();

            try
            {
                if (!customHeaders.IsNullOrBlank())
                {
                    string[] customerHeadersList = customHeaders.Split(m_customHeadersSeparator);

                    if (customerHeadersList != null && customerHeadersList.Length > 0)
                    {
                        foreach (string customHeader in customerHeadersList)
                        {
                            if (customHeader.IsNullOrBlank())
                            {
                                continue;
                            }
                            else if (customHeader.IndexOf(':') == -1)
                            {
                                logger.LogParseCustomHeaderMissingColon(customHeader);
                                continue;
                            }
                            else
                            {
                                //int colonIndex = customHeader.IndexOf(':');
                                //string headerName = customHeader.Substring(0, colonIndex).Trim();
                                //string headerValue = (customHeader.Length > colonIndex) ? customHeader.Substring(colonIndex + 1).Trim() : String.Empty;

                                var trimmedCustomHeader = customHeader.AsSpan().Trim();
                                if (SIPHeaders.SIP_HEADER_VIA.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_FROM.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_CONTACT.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_CSEQ.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_CALLID.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_MAXFORWARDS.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase) ||
                                    SIPHeaders.SIP_HEADER_CONTENTLENGTH.Equals(trimmedCustomHeader, StringComparison.OrdinalIgnoreCase))
                                {
                                    logger.LogParseCustomHeaderNonPermitted(customHeader);
                                    continue;
                                }
                                else
                                {
                                    customHeaderList.Add(customHeader.Trim());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception ParseCustomHeaders ({CustomHeaders}). {Message}", customHeaders, excp.Message);
            }

            return customHeaderList;
        }

        public SIPCallDescriptor CopyOf()
        {
            List<string> copiedCustomHeaders = null;
            if (CustomHeaders != null)
            {
                copiedCustomHeaders = new List<string>();
                copiedCustomHeaders.InsertRange(0, CustomHeaders);
            }

            SIPCallDescriptor copy = new SIPCallDescriptor(
                Username,
                Password,
                Uri,
                From,
                To,
                RouteSet,
                copiedCustomHeaders,
                AuthUsername,
                CallDirection,
                ContentType,
                Content,
                (MangleIPAddress != null) ? new IPAddress(MangleIPAddress.GetAddressBytes()) : null);

            // Options.
            copy.DelaySeconds = DelaySeconds;
            copy.RedirectMode = RedirectMode;
            copy.CallDurationLimit = CallDurationLimit;
            copy.MangleResponseSDP = MangleResponseSDP;
            copy.FromDisplayName = FromDisplayName;
            copy.FromURIUsername = FromURIUsername;
            copy.FromURIHost = FromURIHost;
            copy.TransferMode = TransferMode;

            copy.ToSIPAccount = ToSIPAccount;

            return copy;
        }
    }
}
