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
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        Add = 1,
        Replace = 2,
    }

    public class SIPCallDescriptor
    {
        public const string DELAY_CALL_OPTION_KEY = "dt";       // Dial string option to delay the start of a call leg.
        public const string REDIRECT_MODE_OPTION_KEY = "rm";    // Dial string option to set the redirect mode of a call leg. Redirect mode refers to how 3xx responses to a call are handled.
        public const string CALL_DURATION_OPTION_KEY = "cd";    // Dial string option used to set the maximum duration of a call in seconds.

        private readonly static string m_defaultFromURI = SIPConstants.SIP_DEFAULT_FROMURI;
        private static char m_customHeadersSeparator = SIPProvider.CUSTOM_HEADERS_SEPARATOR;

        private static ILog logger = AppState.logger;
        
        //public static SIPCallDescriptor Empty = new SIPCallDescriptor(null, null, null, null, null, null, null, null, SIPCallDirection.None, null, null);

        public string Username;                 // The username that will be used in the From header and to authenticate the call unless overridden by AuthUsername.
        public string AuthUsername;             // The username that will be used from authentication. Optional setting only needed if the From header user needs to be different from the digest username.
        public string Password;                 // The password that will be used to authenticate the call if required.
        public string Uri;                      // A string representing the URI the call will be forwarded with.
        public string From;                     // A string representing the From header to be set for the call.
        public string To;                       // A string representing the To header to be set for the call.  
        public string RouteSet;                 // A route set for the forwarded call request. If there is only a single route or IP socket it will be treated like an Outbound Proxy (i.e. no Route header will be added).
        public HybridDictionary CustomHeaders;  // An optional list of custom SIP headers that will be added to the INVITE request.
        public SIPCallDirection CallDirection;  // Indicates whether the call is incoming out outgoing relative to this server. An outgoing call is one that is placed by a user the server authenticates.
        public string ContentType;
        public string Content;
        public int DelaySeconds;                        // An amount in seconds to delay the intiation of this call when used as part of a dial string.
        public SIPCallRedirectModesEnum RedirectMode;   // Determines how the call will handle 3xx redirect responses.
        public int CallDurationLimit;                   // If non-zero sets a limit on the duration of any call created with this descriptor.
        public bool MangleResponseSDP;                  // If false indicates the response SDP should be left alone if it contains a private IP address.

        public ManualResetEvent DelayMRE;       // If the call needs to be delayed DelaySeconds this MRE will be used.

        public SIPCallDescriptor(
            string username, 
            string password, 
            string uri, 
            string from, 
            string to, 
            string routeSet, 
            HybridDictionary customHeaders, 
            string authUsername, 
            SIPCallDirection callDirection, 
            string contentType, 
            string content,
            bool mangleResponseSDP)
        {
            Username = username;            
            Password = password;            
            Uri = uri;
            From = from ?? m_defaultFromURI;
            To = to ?? uri;                        
            RouteSet = routeSet;
            CustomHeaders = customHeaders;   
            AuthUsername = authUsername;
            CallDirection = callDirection;
            ContentType = contentType;
            Content = content;
            MangleResponseSDP = mangleResponseSDP;
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
                    if (redirectMode == "a" || redirectMode == "A")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.Add;
                    }
                    else if (redirectMode == "r" || redirectMode == "R")
                    {
                        RedirectMode = SIPCallRedirectModesEnum.Replace;
                    }
                }

                // Parse call duration limit option.
                Match callDurationMatch = Regex.Match(options, CALL_DURATION_OPTION_KEY + @"=(?<callduration>\d+)");
                if (callDurationMatch.Success)
                {
                    Int32.TryParse(callDurationMatch.Result("${callduration}"), out CallDurationLimit);
                }
            }
        }

        public static HybridDictionary ParseCustomHeaders(string customHeaders) {

            HybridDictionary customHeaderList = new HybridDictionary(false);
            
            try {
                if (!customHeaders.IsNullOrBlank()) {
                    string[] customerHeadersList = customHeaders.Split(m_customHeadersSeparator);

                    if (customerHeadersList != null && customerHeadersList.Length > 0) {
                        foreach (string customHeader in customerHeadersList) {
                            if (customHeader.IndexOf(':') == -1) {
                                logger.Warn("ParseCustomHeaders skipping custom header due to missing colon, " + customHeader + ".");
                                continue;
                            }
                            else {
                                int colonIndex = customHeader.IndexOf(':');
                                string headerName = customHeader.Substring(0, colonIndex).Trim();
                                string headerValue = (customHeader.Length > colonIndex) ? customHeader.Substring(colonIndex + 1).Trim() : String.Empty;

                                if (headerName != null && Regex.Match(headerName.Trim(), "^(Via|From|To|Contact|CSeq|Call-ID|Max-Forwards|Content-Length)$", RegexOptions.IgnoreCase).Success) {
                                    logger.Warn("ParseCustomHeaders skipping custom header due to an non-permitted string in header name, " + customHeader + ".");
                                    continue;
                                }
                                else {
                                    customHeaderList.Add(headerName, headerValue);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ParseCustomHeaders (" + customHeaders + "). " + excp.Message);
            }

            return customHeaderList;
        }

        /*
        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(SIPCallDescriptor x, SIPCallDescriptor y)
        {
            if (x.Username == y.Username &&
                x.Password == y.Password &&
                x.Uri == y.Uri &&
                x.From == y.From &&
                x.To == y.To &&
                x.RouteSet == y.RouteSet &&
                x.CustomHeaders == y.CustomHeaders &&
                x.AuthUsername == y.AuthUsername && 
                x.CallDirection == y.CallDirection &&
                x.ContentType == y.ContentType &&
                x.Content == y.Content &&
                x.DelaySeconds == y.DelaySeconds)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool operator !=(SIPCallDescriptor x, SIPCallDescriptor y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
         */
    }
}
