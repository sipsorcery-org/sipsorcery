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
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public struct SIPReplyStruct
    {
        public static SIPReplyStruct Empty = new SIPReplyStruct(SIPResponseStatusCodesEnum.None, null);

        public SIPResponseStatusCodesEnum ResponseStatusCode;
        public string ResponseStatusMessage;

        public SIPReplyStruct(SIPResponseStatusCodesEnum statusCode, string statusMessage)
        {
            ResponseStatusCode = statusCode;
            ResponseStatusMessage = statusMessage;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(SIPReplyStruct x, SIPReplyStruct y)
        {
            if (x.ResponseStatusCode == y.ResponseStatusCode &&
                x.ResponseStatusMessage == y.ResponseStatusMessage)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool operator !=(SIPReplyStruct x, SIPReplyStruct y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public class SIPReplyApp
    {
        private static ILog logger = AppState.GetLogger("sipproxy");

        private static string m_sipProxyUserAgent = "www.mysipswitch.com";

        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;

        private string m_clientUsername = null;             // If the UAC is authenticated holds the username of the client.
        private UASInviteTransaction m_clientTransaction;   // Proxy transaction established with a client making a call out through the switch.
        public SIPTransaction ClientTransaction
        {
            get { return m_clientTransaction; }
        }

        public string Owner
        {
            get { return m_clientUsername; }
        }

        private SIPReplyStruct m_sipReplyStruct;            

        public SIPReplyApp(
            SIPMonitorLogDelegate statefulProxyLogEvent,
            UASInviteTransaction clientTransaction,
            string username)
        {
            m_statefulProxyLogEvent = statefulProxyLogEvent;

            m_clientTransaction = clientTransaction;
            //m_clientTransaction.TransactionCancelled += new SIPTransactionCancelledDelegate(CallCancelled);

            m_clientUsername = username;
        }

        public void Start(string commandData)
        {
            try
            {
                //logger.Debug("SIPReplyApp Start.");

                string[] replyFields = commandData.Split(',');

                SIPResponseStatusCodesEnum statusCode = SIPResponseStatusCodes.GetStatusTypeForCode(Convert.ToInt32(replyFields[0]));
                string statusMessage = (replyFields.Length > 1 && replyFields[1] != null) ? replyFields[1].Trim() : null;
                if(statusMessage == null || statusMessage.Trim().Length == 0)
                {
                    statusMessage = statusCode.ToString();
                }

                m_sipReplyStruct = new SIPReplyStruct(statusCode, statusMessage);

                if (m_clientTransaction != null)
                {
                    SIPResponse replyResponse = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest, statusCode, statusMessage);
                    logger.Debug("SIPReplyApp sending response " + replyFields[0] + " " + statusMessage + ".");

                    if (replyResponse.StatusCode <= 199)
                    {
                        m_clientTransaction.SendInformationalResponse(replyResponse);
                    }
                    else
                    {
                        m_clientTransaction.SendFinalResponse(replyResponse);
                    }
                }
                else
                {
                    logger.Warn("SIPReplyApp could not send response as client transaction was null.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPReplyApp Start. " + excp.Message);
            }
        }
    }
}
