//-----------------------------------------------------------------------------
// Filename: CallbackApp.cs
//
// Description: The framework for creating a new dial plan application.
// 
// History:
// 04 Jun 2009	    Aaron Clauson	    Created.
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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class CallbackApp
    {
        private const int MAXCALLBACK_DELAY_SECONDS = 15;       // The maximum seconds a callback method can be delayed for.
        private const int MAXCALLBACK_RINGTIME_SECONDS = 120;   // Set ring time for calls being created by dial plan. There is nothing that can cancel the call.
        
        private string CRLF = SIPConstants.CRLF;
        private static int m_maxRingTime = SIPTimings.MAX_RING_TIME;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private ISIPCallManager m_callManager;
        private DialStringParser m_dialStringParser;
        private string m_username;
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxy;

        public CallbackApp(
            SIPTransport sipTransport, 
            ISIPCallManager callManager,
            DialStringParser dialStringParser, 
            SIPMonitorLogDelegate logDelegate, 
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy)
        {
            m_sipTransport = sipTransport;
            m_callManager = callManager;
            m_dialStringParser = dialStringParser;
            Log_External = logDelegate;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_outboundProxy = outboundProxy;
        }

        /// <summary>
        /// Establishes a new call with the client end tied to the proxy. Since the proxy will not be sending any audio the idea is that once
        /// the call is up it should be re-INVITED off somewhere else pronto to avoid the callee sitting their listening to dead air.
        /// </summary>
        /// <param name="dest1">The dial string of the first call to place.</param>
        /// <param name="dest2">The dial string of the second call to place.</param>
        /// <param name="delaySeconds">Delay in seconds before placing the first call. Gives the user a chance to hangup their phone if they are calling themselves back.</param>
        /// <returns>The result of the call.</returns>
        public void Callback(string dest1, string dest2, int delaySeconds)
        {
            try
            {
                if (delaySeconds > 0)
                {
                    delaySeconds = (delaySeconds > MAXCALLBACK_DELAY_SECONDS) ? MAXCALLBACK_DELAY_SECONDS : delaySeconds;
                    Log("Callback app delaying by " + delaySeconds + "s.");
                    Thread.Sleep(delaySeconds * 1000);
                }

                Log("Callback app commencing first leg to " + dest1 + ".");

                SIPEndPoint defaultUDPEP = m_sipTransport.GetDefaultSIPEndPoint(SIPProtocolsEnum.udp);

                SIPRequest firstLegDummyInviteRequest = GetCallbackInviteRequest(defaultUDPEP.SocketEndPoint);
                SIPDialogue firstLegDialogue = Dial(dest1, MAXCALLBACK_RINGTIME_SECONDS, 0, firstLegDummyInviteRequest);
                if (firstLegDialogue == null)
                {
                    Log("The first call leg to " + dest1 + " was unsuccessful.");
                    return;
                }

                SDP firstLegSDP = SDP.ParseSDPDescription(firstLegDialogue.RemoteSDP);
                string call1SDPIPAddress = firstLegSDP.Media[0].ConnectionAddress;
                int call1SDPPort = firstLegSDP.Media[0].Port;
                Log("The first call leg to " + dest1 + " was successful, audio socket=" + call1SDPIPAddress + ":" + call1SDPPort + ".");

                Log("Callback app commencing second leg to " + dest2 + ".");

                SIPRequest secondLegDummyInviteRequest = GetCallbackInviteRequest(defaultUDPEP.SocketEndPoint);
                SIPDialogue secondLegDialogue = Dial(dest2, MAXCALLBACK_RINGTIME_SECONDS, 0, secondLegDummyInviteRequest);
                if (secondLegDialogue == null)
                {
                    Log("The second call leg to " + dest2 + " was unsuccessful.");
                    firstLegDialogue.Hangup(m_sipTransport, m_outboundProxy);
                    return;
                }

                SDP secondLegSDP = SDP.ParseSDPDescription(secondLegDialogue.RemoteSDP);
                string call2SDPIPAddress = secondLegSDP.Media[0].ConnectionAddress;
                int call2SDPPort = secondLegSDP.Media[0].Port;
                Log("The second call leg to " + dest2 + " was successful, audio socket=" + call2SDPIPAddress + ":" + call2SDPPort + ".");

                m_callManager.CreateDialogueBridge(firstLegDialogue, secondLegDialogue, m_username);

                Log("Re-inviting Callback dialogues to each other.");

                m_callManager.ReInvite(firstLegDialogue, secondLegDialogue.RemoteSDP);
                m_callManager.ReInvite(secondLegDialogue, firstLegDialogue.RemoteSDP);

                SendRTPPacket(call2SDPIPAddress + ":" + call2SDPPort, call1SDPIPAddress + ":" + call1SDPPort);
                SendRTPPacket(call1SDPIPAddress + ":" + call1SDPPort, call2SDPIPAddress + ":" + call2SDPPort);
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallbackApp. " + excp);
                Log("Exception in Callback. " + excp);
            }
        }

        private SIPDialogue Dial(
          string data,
          int ringTimeout,
          int answeredCallLimit,
          SIPRequest clientRequest) {

            SIPDialogue answeredDialogue = null;
            ManualResetEvent waitForCallCompleted = new ManualResetEvent(false);

            ForkCall call = new ForkCall(m_sipTransport, Log_External, m_callManager.QueueNewCall, m_username, m_adminMemberId, null, m_outboundProxy);
            call.CallProgress += (s, r, h, t, b) => { Log("Progress response of " + s + " received on CallBack Dial" + "."); };
            call.CallFailed += (s, r, h) => { waitForCallCompleted.Set(); };
            call.CallAnswered += (s, r, toTag, h, t, b, d) => { answeredDialogue = d; waitForCallCompleted.Set(); };

            try {
                Queue<List<SIPCallDescriptor>> callsQueue = m_dialStringParser.ParseDialString(DialPlanContextsEnum.Script, clientRequest, data, null, null, null, null, null, null, null, null);
                call.Start(callsQueue);

                // Wait for an answer.
                ringTimeout = (ringTimeout > m_maxRingTime) ? m_maxRingTime : ringTimeout;
                if (waitForCallCompleted.WaitOne(ringTimeout * 1000, false)) {
                    // Call timed out.
                    call.CancelNotRequiredCallLegs(CallCancelCause.TimedOut);
                }

                return answeredDialogue;
            }
            catch (Exception excp) {
                logger.Error("Exception CallbackApp Dial. " + excp);
                return null;
            }
        }

        private SIPRequest GetCallbackInviteRequest(IPEndPoint localSIPEndPoint)
        {
            string callBackURI = "sip:callback@sipsorcery.com";
            string callBackUserField = "<" + callBackURI + ">";
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, callBackURI);
            SIPHeader inviteHeader = new SIPHeader(callBackUserField, callBackUserField, 1, CallProperties.CreateNewCallId());
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteHeader.ContentType = "application/sdp";
            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            inviteHeader.Vias.PushViaHeader(viaHeader);
            inviteRequest.Header = inviteHeader;

            string body =
               "v=0" + CRLF +
                "o=- " + Crypto.GetRandomInt(1000, 5000).ToString() + " 2 IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                "s=session" + CRLF +
                "c=IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                "t=0 0" + CRLF +
                "m=audio " + Crypto.GetRandomInt(10000, 20000).ToString() + " RTP/AVP 0 18 101" + CRLF +
                "a=rtpmap:0 PCMU/8000" + CRLF +
                "a=rtpmap:18 G729/8000" + CRLF +
                "a=rtpmap:101 telephone-event/8000" + CRLF +
                "a=fmtp:101 0-16" + CRLF +
                "a=recvonly";
            inviteHeader.ContentLength = body.Length;
            inviteRequest.Body = body;

            return inviteRequest;
        }

        private void SendRTPPacket(string sourceSocket, string destinationSocket)
        {
            try
            {
                //logger.Debug("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");
                Log("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");

                IPEndPoint sourceEP = IPSocket.GetIPEndPoint(sourceSocket);
                IPEndPoint destEP = IPSocket.GetIPEndPoint(destinationSocket);

                RTPPacket rtpPacket = new RTPPacket(80);
                rtpPacket.Header.SequenceNumber = (UInt16)6500;
                rtpPacket.Header.Timestamp = 100000;

                UDPPacket udpPacket = new UDPPacket(sourceEP.Port, destEP.Port, rtpPacket.GetBytes());
                IPv4Header ipHeader = new IPv4Header(ProtocolType.Udp, Crypto.GetRandomInt(6), sourceEP.Address, destEP.Address);
                IPv4Packet ipPacket = new IPv4Packet(ipHeader, udpPacket.GetBytes());

                byte[] data = ipPacket.GetBytes();

                Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

                rawSocket.SendTo(data, destEP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendRTPPacket. " + excp.Message);
            }
        }

        private void Log(string message) {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, message, m_username));
        }
    }
}
