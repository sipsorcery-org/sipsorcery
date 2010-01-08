//-----------------------------------------------------------------------------
// Filename: SwitchboardApp.cs
//
// Description: This application forwards a call to the switchboard client.
// 
// History:
// 02 Sep` 2009	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan {
    
    public class SwitchboardApp {

        public const string SWITCHBOARD_REMOTE_HEADER = "Switchboard-Remote";

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private string m_username;
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxy;
        private DialPlanContext m_dialPlanContext;
        private UACInviteTransaction m_switchboardTransaction;
        private SIPURI m_switchboardURI;
        private SIPEndPoint m_proxySendFrom;
        private ManualResetEvent m_waitForAnswer = new ManualResetEvent(false);

        public SwitchboardApp(
            SIPTransport sipTransport, 
            SIPMonitorLogDelegate logDelegate, 
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy,
            DialPlanContext dialPlanContext)
        {
            m_sipTransport = sipTransport;
            Log_External = logDelegate;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_outboundProxy = outboundProxy;
            m_dialPlanContext = dialPlanContext;
        }

        public void SendToSwitchboard(SIPRequest sipRequest, SIPRegistrarBinding switchboardBinding) {
            SIPRequest switchboardRequest = sipRequest.Copy();
            m_switchboardURI = switchboardBinding.MangledContactSIPURI;
            switchboardRequest.URI = m_switchboardURI;
            m_proxySendFrom = SIPEndPoint.ParseSIPEndPoint(switchboardBinding.ProxySIPSocket);
            switchboardRequest.Header.ProxySendFrom = m_proxySendFrom.ToString();
            switchboardRequest.Header.UnknownHeaders.Add(SWITCHBOARD_REMOTE_HEADER + ": " + sipRequest.Header.ProxyReceivedFrom);
            switchboardRequest.Header.Vias = new SIPViaSet();
            SIPEndPoint localEndPoint = m_sipTransport.GetDefaultSIPEndPoint(m_outboundProxy.SIPProtocol);
            switchboardRequest.Header.Vias.PushViaHeader(new SIPViaHeader(m_outboundProxy, CallProperties.CreateBranchId()));
            m_switchboardTransaction = m_sipTransport.CreateUACTransaction(switchboardRequest, m_outboundProxy, localEndPoint, m_outboundProxy);
            m_switchboardTransaction.CDR = null;
            m_switchboardTransaction.UACInviteTransactionFinalResponseReceived += UACTransactionFinalResponseReceived;
            m_switchboardTransaction.UACInviteTransactionInformationResponseReceived += UACInviteTransactionInformationResponseReceived;
            m_switchboardTransaction.SendReliableRequest();

            m_waitForAnswer.WaitOne(30000);
        }

        private void UACInviteTransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            m_dialPlanContext.CallProgress(sipResponse.Status, sipResponse.ReasonPhrase, null, sipResponse.Header.ContentType, sipResponse.Body);
        }
         
        private void UACTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            m_dialPlanContext.CallAnswered(sipResponse.Status, sipResponse.ReasonPhrase, sipResponse.Header.To.ToTag, null, sipResponse.Header.ContentType, sipResponse.Body, null, SIPDialogueTransferModesEnum.NotAllowed);
            m_waitForAnswer.Set();
        }
    }
}
