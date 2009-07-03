//-----------------------------------------------------------------------------
// Filename: SIPReplyApp.cs
//
// Description: Test app to send a custom SIP response from the dial plan.
// 
// History:
// 18 Nov 2007	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.AppServer.DialPlan
{
    public class SIPReplyApp
    {
         public SIPReplyApp() { }

         public SIPResponse Start(string commandData) {
            string[] replyFields = commandData.Split(',');
            string statusMessage = (replyFields.Length > 1 && replyFields[1] != null) ? replyFields[1].Trim() : null;

            return Start(Convert.ToInt32(replyFields[0]), statusMessage, null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="status"></param>
        /// <param name="reason"></param>
        /// <param name="customHeaders">An optional list of pipe '|' delimited custom headers.</param>
        /// <returns></returns>
        public SIPResponse Start(int status, string reason, string customHeaders) {
            SIPResponseStatusCodesEnum statusCode = SIPResponseStatusCodes.GetStatusTypeForCode(status);
            if (!reason.IsNullOrBlank()) {
                reason = reason.Trim();
            }

            SIPResponse sipResponse = new SIPResponse(statusCode, reason, null);

            if (!customHeaders.IsNullOrBlank()) {
                string[] headerList = customHeaders.Split('|');
                foreach(string header in headerList) {
                    sipResponse.Header.UnknownHeaders.Add(header);
                }
            }

            return sipResponse;
        }
    }
}
