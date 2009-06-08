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

namespace SIPSorcery.Servers
{
    public class SIPReplyApp
    {
        private static ILog logger = AppState.GetLogger("dialplan");

        private event SIPMonitorLogDelegate Log_External;

        private string m_clientUsername = null;             // If the UAC is authenticated holds the username of the client.

        private CallProgressDelegate CallProgress_External;
        private CallFailedDelegate CallFailed_External;

        public SIPReplyApp(
            SIPMonitorLogDelegate statefulProxyLogEvent,
            string username,
            CallProgressDelegate callProgress,
            CallFailedDelegate callFailed) {

            Log_External = statefulProxyLogEvent;
            m_clientUsername = username;
            CallProgress_External = callProgress;
            CallFailed_External = callFailed;
        }

        public void Start(string commandData) {
            try {
                string[] replyFields = commandData.Split(',');
                string statusMessage = (replyFields.Length > 1 && replyFields[1] != null) ? replyFields[1].Trim() : null;

                Start(Convert.ToInt32(replyFields[0]), statusMessage);
            }
            catch (ThreadAbortException) { }
            catch (Exception excp) {
                logger.Error("Exception SIPReplyApp Start. " + excp.Message);
            }
        }

        public void Start(int status, string reason) {
            try {
                SIPResponseStatusCodesEnum statusCode = SIPResponseStatusCodes.GetStatusTypeForCode(status);
                if (!reason.IsNullOrBlank()) {
                    reason = reason.Trim();
                }

                if ((int)statusCode < 200) {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIPReplyApp sending info response of " + statusCode + " and " + reason + ".", m_clientUsername));
                    CallProgress_External(statusCode, reason, null, null);
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIPReplyApp sending failure response of " + statusCode + " and " + reason + ".", m_clientUsername));
                    CallFailed_External(statusCode, reason);
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception excp) {
                logger.Error("Exception SIPReplyApp Start. " + excp.Message);
            }
        }
    }
}
