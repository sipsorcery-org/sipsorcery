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
// 10 Aug 2008	Aaron Clauson	Created.
// 04 Oct 2008  Aaron Clauson   Added AuthUsername.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

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

    public class SwitchboardHeaders
    {
        public string SwitchboardOriginalCallID;        // If set holds a call identifier and is typically the SIP Call-ID of an associated INVITE.
        //public string SwitchboardCallerDescription;     // If set holds a general description of the caller.
        public string SwitchboardLineName;              // If set holds a name of the line that the original call was received on.
        //public string SwitchboardDialogueDescription;   // Same as the SwitchboardDescription field but is used to differentiate when a SIP header should be set and when the value should only be recorded in the dialogue.
        public string SwitchboardOwner;                 // If set indicates a specific SIP account is taking ownership of any call that gets established.
        //public string SwitchboardOriginalFrom;          // If set indicates it should be used as the From header on calls to local extensions.

        public SwitchboardHeaders()
        { }

        public SwitchboardHeaders(string callID, string callerDescription, string lineName, string owner)
        {
            SwitchboardOriginalCallID = callID;
            //SwitchboardCallerDescription = callerDescription;
            SwitchboardLineName = lineName;
            SwitchboardOwner = owner;
            //SwitchboardOriginalFrom = from;
        }
    }

    public class SIPCallDescriptor
    {
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

        // Switchboard dial string options.
        public const string SWITCHBOARD_LINE_NAME_KEY = "swln";             // Dial string option to set the Switchboard-LineName header on the call leg.
        //public const string SWITCHBOARD_DIALOGUE_DESCRIPTION_KEY = "swdd";// Dial string option to set a value for the switchboard description field on the answered dialogue. It does not set a header.
        public const string SWITCHBOARD_CALLID_KEY = "swcid";               // Dial string option to set the Switchboard-CallID header on the call leg.
        public const string SWITCHBOARD_OWNER_KEY = "swo";                  // Dial string option to set the Switchboard-Owner header on the call leg.

        private readonly static string m_defaultFromURI = SIPConstants.SIP_DEFAULT_FROMURI;
        private static char m_customHeadersSeparator = '|';                 // Must match SIPProvider.CUSTOM_HEADERS_SEPARATOR.

        private static ILog logger = AppState.logger;

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
        public int DelaySeconds;                        // An amount in seconds to delay the intiation of this call when used as part of a dial string.
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

        // Custom headers for sipsorcery switchboard application.
        public SwitchboardHeaders SwitchboardHeaders = new SwitchboardHeaders();

        // Real-time call control variables.
        public string AccountCode;          // If set indicates this is a billable call and this is the account code to bill the call against.
        public string RateCode;             // If set indicates and the call is billable indicates the rate code that should be used to determine the rate for the call.

        public CRMHeaders CRMHeaders;

        // Properties needed for Google Voice calls.
        public bool IsGoogleVoiceCall;
        public string CallbackNumber;
        public string CallbackPattern;
        public int CallbackPhoneType;

        public SIPAccount ToSIPAccount;         // If non-null indicates the call is for a SIP Account on the same server. An example of using this it to call from one user into another user's dialplan.

        public ManualResetEvent DelayMRE;       // If the call needs to be delayed DelaySeconds this MRE will be used.

        /// <summary>
        /// 
        /// </summary>
        /// <param name="toSIPAccount"></param>
        /// <param name="uri">The uri can be different to the to SIP account if a dotted notation is used. For
        /// example 1234.user@sipsorcery.com.</param>
        /// <param name="fromHeader"></param>
        /// <param name="contentType"></param>
        /// <param name="content"></param>
        public SIPCallDescriptor(SIPAccount toSIPAccount, string uri, string fromHeader, string contentType, string content)
        {
            ToSIPAccount = toSIPAccount;
            Uri = uri ?? toSIPAccount.SIPUsername + "@" + toSIPAccount.SIPDomain;
            From = fromHeader;
            ContentType = contentType;
            Content = content;
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
            SIPFromHeader fromHeader = null;

            // If the call is for a sipsorcery extension and a SwitchboardFrom header has been set use it.
            //if (CallDirection == SIPCallDirection.In && SwitchboardHeaders != null && !SwitchboardHeaders.SwitchboardOriginalFrom.IsNullOrBlank())
            //{
            //    fromHeader = SIPFromHeader.ParseFromHeader(SwitchboardHeaders.SwitchboardOriginalFrom);
            //}
            //else
            //{
                fromHeader = SIPFromHeader.ParseFromHeader(From);
            //}

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
        /// <param name="fromhost"></param>
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
                Match delayCallMatch = Regex.Match(options, DELAY_CALL_OPTION_KEY + @"=(?<delaytime>\d+)");
                if (delayCallMatch.Success)
                {
                    Int32.TryParse(delayCallMatch.Result("${delaytime}"), out DelaySeconds);
                }

                // Parse redirect mode option.
                Match redirectModeMatch = Regex.Match(options, REDIRECT_MODE_OPTION_KEY + @"=(?<redirectmode>\w)");
                if (redirectModeMatch.Success)
                {
                    string redirectMode = redirectModeMatch.Result("${redirectmode}");
                    //if (redirectMode == "a" || redirectMode == "A")
                    //{
                    //    RedirectMode = SIPCallRedirectModesEnum.Add;
                    //}
                    //else if (redirectMode == "r" || redirectMode == "R")
                    //{
                    //    RedirectMode = SIPCallRedirectModesEnum.Replace;
                    //}
                    if (redirectMode == "n" || redirectMode == "N")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.NewDialPlan;
                    }
                    else if (redirectMode == "m" || redirectMode == "M")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.Manual;
                    }
                }

                // Parse call duration limit option.
                Match callDurationMatch = Regex.Match(options, CALL_DURATION_OPTION_KEY + @"=(?<callduration>\d+)");
                if (callDurationMatch.Success)
                {
                    Int32.TryParse(callDurationMatch.Result("${callduration}"), out CallDurationLimit);
                }

                // Parse the mangle option.
                Match mangleMatch = Regex.Match(options, MANGLE_MODE_OPTION_KEY + @"=(?<mangle>\w+)");
                if (mangleMatch.Success)
                {
                    Boolean.TryParse(mangleMatch.Result("${mangle}"), out MangleResponseSDP);
                }

                // Parse the From header display name option.
                Match fromDisplayNameMatch = Regex.Match(options, FROM_DISPLAY_NAME_KEY + @"=(?<displayname>.+?)(,|$)");
                if (fromDisplayNameMatch.Success)
                {
                    FromDisplayName = fromDisplayNameMatch.Result("${displayname}").Trim();
                }

                // Parse the From header URI username option.
                Match fromUsernameNameMatch = Regex.Match(options, FROM_USERNAME_KEY + @"=(?<username>.+?)(,|$)");
                if (fromUsernameNameMatch.Success)
                {
                    FromURIUsername = fromUsernameNameMatch.Result("${username}").Trim();
                }

                // Parse the From header URI host option.
                Match fromURIHostMatch = Regex.Match(options, FROM_HOST_KEY + @"=(?<host>.+?)(,|$)");
                if (fromURIHostMatch.Success)
                {
                    FromURIHost = fromURIHostMatch.Result("${host}").Trim();
                }

                // Parse the Transfer behaviour option.
                Match transferMatch = Regex.Match(options, TRANSFER_MODE_OPTION_KEY + @"=(?<transfermode>.+?)(,|$)");
                if (transferMatch.Success)
                {
                    string transferMode = transferMatch.Result("${transfermode}");
                    if (transferMode == "n" || transferMode == "N")
                    {
                        TransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                    }
                    else if (transferMode == "p" || transferMode == "P")
                    {
                        TransferMode = SIPDialogueTransferModesEnum.PassThru;
                    }
                    else if (transferMode == "c" || transferMode == "C")
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

                // Parse the switchboard description value.
                Match switchboardDescriptionMatch = Regex.Match(options, SWITCHBOARD_LINE_NAME_KEY + @"=(?<lineName>.+?)(,|$)");
                if (switchboardDescriptionMatch.Success)
                {
                    SwitchboardHeaders.SwitchboardLineName = switchboardDescriptionMatch.Result("${lineName}").Trim();
                }

                // Parse the switchboard dialogue description value.
                //Match switchboardDialogueDescriptionMatch = Regex.Match(options, SWITCHBOARD_DIALOGUE_DESCRIPTION_KEY + @"=(?<description>.+?)(,|$)");
                //if (switchboardDialogueDescriptionMatch.Success)
                //{
                //    SwitchboardHeaders.SwitchboardDialogueDescription = switchboardDialogueDescriptionMatch.Result("${description}").Trim();
                //}

                // Parse the switchboard CallID value.
                Match switchboardCallIDMatch = Regex.Match(options, SWITCHBOARD_CALLID_KEY + @"=(?<callid>.+?)(,|$)");
                if (switchboardCallIDMatch.Success)
                {
                    SwitchboardHeaders.SwitchboardOriginalCallID = switchboardCallIDMatch.Result("${callid}").Trim();
                }

                // Parse the switchboard owner value.
                Match switchboardOwnerMatch = Regex.Match(options, SWITCHBOARD_OWNER_KEY + @"=(?<owner>.+?)(,|$)");
                if (switchboardOwnerMatch.Success)
                {
                    SwitchboardHeaders.SwitchboardOwner = switchboardOwnerMatch.Result("${owner}").Trim();
                }

                // Parse the request caller details option.
                Match callerDetailsMatch = Regex.Match(options,REQUEST_CALLER_DETAILS + @"=(?<callerdetails>\w+)");
                if (callerDetailsMatch.Success)
                {
                    Boolean.TryParse(callerDetailsMatch.Result("${callerdetails}"), out RequestCallerDetails);
                }

                // Parse the accountcode.
                Match accountCodeMatch = Regex.Match(options, ACCOUNT_CODE_KEY + @"=(?<accountCode>\w+)");
                if (accountCodeMatch.Success)
                {
                    AccountCode = accountCodeMatch.Result("${accountCode}");
                }

                // Parse the rate code.
                Match rateCodeMatch = Regex.Match(options, RATE_CODE_KEY + @"=(?<rateCode>\w+)");
                if (rateCodeMatch.Success)
                {
                    RateCode = rateCodeMatch.Result("${rateCode}");
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
                                logger.Warn("ParseCustomHeaders skipping custom header due to missing colon, " + customHeader + ".");
                                continue;
                            }
                            else
                            {
                                //int colonIndex = customHeader.IndexOf(':');
                                //string headerName = customHeader.Substring(0, colonIndex).Trim();
                                //string headerValue = (customHeader.Length > colonIndex) ? customHeader.Substring(colonIndex + 1).Trim() : String.Empty;

                                if (Regex.Match(customHeader.Trim(), "^(Via|From|To|Contact|CSeq|Call-ID|Max-Forwards|Content-Length)$", RegexOptions.IgnoreCase).Success)
                                {
                                    logger.Warn("ParseCustomHeaders skipping custom header due to an non-permitted string in header name, " + customHeader + ".");
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
                logger.Error("Exception ParseCustomHeaders (" + customHeaders + "). " + excp.Message);
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
