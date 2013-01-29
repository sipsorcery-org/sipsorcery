//-----------------------------------------------------------------------------
// Filename: GoogleVoiceUserAgent.cs
//
// Description: A wrapper for the Google Voice call application to allow it to
// work the same way as a standard SIP call and be used in dial strings.
// 
// History:
// 22 Jan 2011	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Net;
using System.Text;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class GoogleVoiceUserAgent : ISIPClientUserAgent
    {
        private const int MAX_CALLBACK_WAIT_TIME = 60;  // The maximum time that the callback will be waited for. Generally the call would be cancelled before this anyway.

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate Log_External;

        public string Owner { get; set ;}
        public string AdminMemberId { get; set; }
        public UACInviteTransaction ServerTransaction { get; set; }
        public SIPDialogue SIPDialogue { get; set; }
        public SIPCallDescriptor CallDescriptor { get; set; }
        public bool IsUACAnswered { get; set; }

        // Real-time call control properties (not used by this user agent).
        //public string AccountCode { get; set; }
        //public decimal ReservedCredit { get; set; }
        //public int ReservedSeconds { get; set; }
        //public decimal Rate { get; set; }

        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;
        public event SIPCallFailedDelegate CallFailed;

        private GoogleVoiceCall m_googleVoiceCall;

        public GoogleVoiceUserAgent(
            SIPTransport sipTransport,
            ISIPCallManager callManager,
            SIPMonitorLogDelegate logDelegate,
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy)
        {
            Owner = username;
            AdminMemberId = adminMemberId;
            Log_External = logDelegate;
            m_googleVoiceCall = new GoogleVoiceCall(sipTransport, callManager, logDelegate, username, adminMemberId, outboundProxy);
            m_googleVoiceCall.CallProgress += new CallProgressDelegate(CallProgress);
        }

        private void CallProgress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody, ISIPClientUserAgent uac)
        {
            SIPResponse progressResponse = new SIPResponse(progressStatus, reasonPhrase, null);
            CallRinging(this, progressResponse);
        }

        public void Call(SIPCallDescriptor descriptor)
        {
            try
            {
                CallDescriptor = descriptor;
                SIPURI destinationURI = SIPURI.ParseSIPURIRelaxed(descriptor.Uri);

                bool wasSDPMangled = false;
                IPEndPoint sdpEndPoint = null;
                if (descriptor.MangleIPAddress != null)
                {
                    sdpEndPoint = SDP.GetSDPRTPEndPoint(descriptor.Content);
                    if (sdpEndPoint != null)
                    {
                        descriptor.Content = SIPPacketMangler.MangleSDP(descriptor.Content, descriptor.MangleIPAddress.ToString(), out wasSDPMangled);
                    }
                }

                if (wasSDPMangled)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on Google Voice call had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + descriptor.MangleIPAddress.ToString() + ":" + sdpEndPoint.Port + ".", Owner));
                }
                else if (sdpEndPoint != null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on Google Voice call could not be mangled, using original RTP socket of " + sdpEndPoint.ToString() + ".", Owner));
                }
                
                SIPDialogue = m_googleVoiceCall.InitiateCall(descriptor.Username, descriptor.Password, descriptor.CallbackNumber, destinationURI.User, descriptor.CallbackPattern, descriptor.CallbackPhoneType, MAX_CALLBACK_WAIT_TIME, descriptor.ContentType, descriptor.Content);

                if (SIPDialogue != null)
                {
                    CallAnswered(this, null);
                }
                else
                {
                    CallFailed(this, "Google Voice call failed.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GoogleVoiceCallAgent Call. " + excp.Message);
                CallFailed(this, excp.Message);
            }
        }

        public void Cancel()
        {
            m_googleVoiceCall.ClientCallTerminated(CallCancelCause.Unknown);
        }

        public void Update(CRMHeaders crmHeaders)
        {
            throw new NotImplementedException();
        }
    }
}
