//-----------------------------------------------------------------------------
// Filename: RTSPApp.cs
//
// Description: Call to an RTSP streaming server.
// 
// History:
// 16 Nov 2007	    Aaron Clauson	    Created.
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
using System.Reflection;
using System.Text.RegularExpressions;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.AppServer.DialPlan
{
    public delegate void RTSPCallAnsweredDelegate(RTSPApp rtspCall);
    
    public struct RTSPAppStruct
    {
        public static RTSPAppStruct Empty = new RTSPAppStruct(null, false);

        public string URL;
        public bool TraceRequired;

        public RTSPAppStruct(string url, bool traceRequired)
        {
            URL = url;
            TraceRequired = traceRequired;
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(RTSPAppStruct x, RTSPAppStruct y)
        {
            if (x.URL == y.URL &&
                x.TraceRequired == y.TraceRequired)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool operator !=(RTSPAppStruct x, RTSPAppStruct y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public class RTSPApp
    {
        private static ILog logger = AppState.GetLogger("sipproxy");

        private static string m_sipProxyUserAgent = "sipsorcery.com";

        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;

        public readonly Guid RTSPCallId = Guid.NewGuid();

        private string m_clientUsername = null;             // If the UAC is authenticated holds the username of the client.
        private UASInviteTransaction m_clientTransaction;   // Proxy transaction established with a client making a call out through the switch.
        public SIPTransaction ClientTransaction
        {
            get { return m_clientTransaction; }
        }
        //public SIPDialogue ClientDialogue
        //{
        //    get { return m_clientDialogue; }
        //}
        //private SIPDialogue m_clientDialogue;               // If the call gets established this holds the dialogue information for the call leg between the switch and the client.

        private RTSPAppStruct m_rtspAppStruct;            // Describes the RTSP server leg of the call from the sipswitch.

        //public event RTSPCallAnsweredDelegate CallAnswered;

        public string Owner
        {
            get { return m_clientUsername; }
        }

        private int m_rtpPort;

        public RTSPApp(
            SIPMonitorLogDelegate statefulProxyLogEvent,
            UASInviteTransaction clientTransaction,
            string username)
        {
            m_statefulProxyLogEvent = statefulProxyLogEvent;

            m_clientTransaction = clientTransaction;
            //m_clientTransaction.TransactionCancelled += new SIPTransactionCancelledDelegate(CallCancelled);

            m_clientUsername = username;

            logger.Debug(m_clientTransaction.TransactionRequest.Body);
            
            m_rtpPort = Convert.ToInt32(Regex.Match(m_clientTransaction.TransactionRequest.Body, @"m=audio (?<port>\d+)", RegexOptions.Singleline).Result("${port}"));
            logger.Debug("RTP port=" + m_rtpPort);
        }

        public void Start(string commandData)
        {
            try
            {
                m_rtspAppStruct = new RTSPAppStruct(commandData, false);
                
                RTSPClient rtspClient = new RTSPClient();
                string sdp = rtspClient.GetStreamDescription(m_rtspAppStruct.URL);

                SIPResponse sipResponse = GetOkResponse(m_clientTransaction.TransactionRequest, sdp);

                m_clientTransaction.SendFinalResponse(sipResponse);

                rtspClient.Start(m_rtspAppStruct.URL, m_clientTransaction.RemoteEndPoint.Address, m_rtpPort);
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPCall StartCall. " + excp.Message);
            }
        }

        private SIPResponse GetOkResponse(SIPRequest sipRequest, string messageBody)
        {
            try
            {
                SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);


                okResponse.Header.UserAgent = m_sipProxyUserAgent;
                okResponse.Header.Contact = SIPContactHeader.ParseContactHeader(sipRequest.LocalSIPEndPoint.ToString());
                okResponse.Body = messageBody;
                okResponse.Header.ContentType = "application/sdp";
                okResponse.Header.ContentLength = messageBody.Length;

                return okResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetOkResponse. " + excp.Message);
                throw excp;
            }
        } 
    }
}
