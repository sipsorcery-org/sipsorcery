// ============================================================================
// FileName: ServiceAuthToken.cs
//
// Description:
// Represents a security token that is passed to web or WCF service.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Jun 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
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
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.RegularExpressions;

#if !SILVERLIGHT
using System.Web;
using System.Web.Services;
#endif

namespace SIPSorcery.Sys.Auth
{
    public class ServiceAuthToken
    {
        public const string AUTH_TOKEN_KEY = "authid";
        public const string COOKIES_KEY = "Cookie";

#if !SILVERLIGHT

        public static string GetAuthId()
        {
            string authId = null;

            SIPSorcerySecurityHeader securityheader = SIPSorcerySecurityHeader.ParseHeader(OperationContext.Current);
            if (securityheader != null)
            {
                authId = securityheader.AuthID;
            }

            // HTTP Context is available for ?? binding.
            if (authId.IsNullOrBlank() && HttpContext.Current != null)
            {
                // If running in IIS check for a cookie.
                HttpCookie authIdCookie = HttpContext.Current.Request.Cookies[AUTH_TOKEN_KEY];
                if (authIdCookie != null)
                {
                    //logger.Debug("authid cookie found: " + authIdCookie.Value + ".");
                    authId = authIdCookie.Value;
                }
            }

            // No HTTP context available so try and get a cookie value from the operation context.
            if (authId.IsNullOrBlank() && OperationContext.Current != null && OperationContext.Current.IncomingMessageProperties[HttpRequestMessageProperty.Name] != null)
            {
                HttpRequestMessageProperty httpRequest = (HttpRequestMessageProperty)OperationContext.Current.IncomingMessageProperties[HttpRequestMessageProperty.Name];
                // Check for the header in a case insensitive way. Allows matches on authid, Authid etc.
                if (httpRequest.Headers.AllKeys.Contains(AUTH_TOKEN_KEY, StringComparer.InvariantCultureIgnoreCase))
                {
                    string authIDHeader = httpRequest.Headers.AllKeys.First(h => { return String.Equals(h, AUTH_TOKEN_KEY, StringComparison.InvariantCultureIgnoreCase); });
                    authId = httpRequest.Headers[authIDHeader];
                    //logger.Debug("authid HTTP header found: " + authId + ".");
                }
                else if (httpRequest.Headers.AllKeys.Contains(COOKIES_KEY, StringComparer.InvariantCultureIgnoreCase))
                {
                    Match authIDMatch = Regex.Match(httpRequest.Headers[COOKIES_KEY], @"authid=(?<authid>.+)");
                    if (authIDMatch.Success)
                    {
                        authId = authIDMatch.Result("${authid}");
                        //logger.Debug("authid HTTP cookie found: " + authId + ".");
                    }
                }
            }

            return authId;
        }
#endif
    }
}
