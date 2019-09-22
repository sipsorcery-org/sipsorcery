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
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class CallbackApp
    {
        private const int MAXCALLBACK_DELAY_SECONDS = 15;           // The maximum seconds a callback method can be delayed for.
        private const int MAXCALLBACK_RINGTIME_SECONDS = 60;        // Set ring time for calls being created by dial plan. There is nothing that can cancel the call.
        private const int CHECK_FIRST_LEG_FOR_HANGUP_PERIOD = 1000; // The period to check whether the first call leg has been hungup while the second call leg is ringing.

        private string CRLF = SIPConstants.CRLF;
        //private static int m_maxRingTime = SIPTimings.MAX_RING_TIME;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private ISIPCallManager m_callManager;
        private DialStringParser m_dialStringParser;
        private string m_username;
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxy;
        private SIPDialogue m_firstLegDialogue;
        private bool m_firstLegEarlyMediaSet;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;

        public CallbackApp(
            SIPTransport sipTransport, 
            ISIPCallManager callManager,
            DialStringParser dialStringParser, 
            SIPMonitorLogDelegate logDelegate, 
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor)
        {
            m_sipTransport = sipTransport;
            m_callManager = callManager;
            m_dialStringParser = dialStringParser;
            Log_External = logDelegate;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_outboundProxy = outboundProxy;
            m_sipDialoguePersistor = sipDialoguePersistor;
        }

        /// <summary>
        /// Establishes a new call with the client end tied to the proxy. Since the proxy will not be sending any audio the idea is that once
        /// the call is up it should be re-INVITED off somewhere else pronto to avoid the callee sitting their listening to dead air.
        /// </summary>
        /// <param name="dest1">The dial string of the first call to place.</param>
        /// <param name="dest2">The dial string of the second call to place.</param>
        /// <param name="delaySeconds">Delay in seconds before placing the first call. Gives the user a chance to hangup their phone if they are calling themselves back.</param>
        /// <param name="ringTimeoutLeg1">The ring timeout for the first call leg, If 0 the max timeout will be used.</param>
        /// <param name="ringTimeoutLeg1">The ring timeout for the second call leg, If 0 the max timeout will be used.</param>
        /// <param name="customHeadersCallLeg1">A | delimited string that contains a list of custom SIP headers to add to the INVITE request sent for the first call leg.</param>
        /// /// <param name="customHeadersCallLeg2">A | delimited string that contains a list of custom SIP headers to add to the INVITE request sent for the second call leg.</param>
        /// <returns>The result of the call.</returns>
        public void Callback(string dest1, string dest2, int delaySeconds, int ringTimeoutLeg1, int ringTimeoutLeg2, string customHeadersCallLeg1, string customHeadersCallLeg2)
        {
            var ts = new CancellationTokenSource();
            CancellationToken ct = ts.Token;

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

                SIPRequest firstLegDummyInviteRequest = GetCallbackInviteRequest(defaultUDPEP.GetIPEndPoint(), null);
                ForkCall firstLegCall = new ForkCall(m_sipTransport, Log_External, m_callManager.QueueNewCall, null, m_username, m_adminMemberId, m_outboundProxy, m_callManager, null);
                m_firstLegDialogue = Dial(firstLegCall, dest1, ringTimeoutLeg1, 0, firstLegDummyInviteRequest, SIPCallDescriptor.ParseCustomHeaders(customHeadersCallLeg1));
                if (m_firstLegDialogue == null)
                {
                    Log("The first call leg to " + dest1 + " was unsuccessful.");
                    return;
                }

                // Persist the dialogue to the database so any hangup can be detected.
                m_sipDialoguePersistor.Add(new SIPDialogueAsset(m_firstLegDialogue));

                SDP firstLegSDP = SDP.ParseSDPDescription(m_firstLegDialogue.RemoteSDP);
                string call1SDPIPAddress = firstLegSDP.Connection.ConnectionAddress;
                int call1SDPPort = firstLegSDP.Media[0].Port;
                Log("The first call leg to " + dest1 + " was successful, audio socket=" + call1SDPIPAddress + ":" + call1SDPPort + ".");

                Log("Callback app commencing second leg to " + dest2 + ".");

                SIPRequest secondLegDummyInviteRequest = GetCallbackInviteRequest(defaultUDPEP.GetIPEndPoint(), m_firstLegDialogue.RemoteSDP);
                ForkCall secondLegCall = new ForkCall(m_sipTransport, Log_External, m_callManager.QueueNewCall, null, m_username, m_adminMemberId, m_outboundProxy, m_callManager, null);
                
                Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                        Thread.Sleep(CHECK_FIRST_LEG_FOR_HANGUP_PERIOD);

                        Console.WriteLine("Checking if first call leg is still up...");

                        if (ct.IsCancellationRequested)
                        {
                            Console.WriteLine("Checking first call leg task was cancelled.");
                            break;
                        }
                        else
                        {
                            // Check that the first call leg hasn't been hung up.
                            var dialog = m_sipDialoguePersistor.Get(m_firstLegDialogue.Id);
                            if (dialog == null)
                            {
                                Console.WriteLine("First call leg has been hungup.");

                                // The first call leg has been hungup while waiting for the second call.
                                Log("The first call leg was hungup while the second call leg was waiting for an answer.");
                                secondLegCall.CancelNotRequiredCallLegs(CallCancelCause.ClientCancelled);
                                break;
                            }
                        }
                    }

                    Console.WriteLine("Checking first call leg task finished...");
                }, ct);

                SIPDialogue secondLegDialogue = Dial(secondLegCall, dest2, ringTimeoutLeg2, 0, secondLegDummyInviteRequest, SIPCallDescriptor.ParseCustomHeaders(customHeadersCallLeg2));

                ts.Cancel();

                if (secondLegDialogue == null)
                {
                    Log("The second call leg to " + dest2 + " was unsuccessful.");
                    m_firstLegDialogue.Hangup(m_sipTransport, m_outboundProxy);
                    return;
                }

                // Check that the first call leg hasn't been hung up.
                var firstLegDialog = m_sipDialoguePersistor.Get(m_firstLegDialogue.Id);
                if (firstLegDialog == null)
                {
                    // The first call leg has been hungup while waiting for the second call.
                    Log("The first call leg was hungup while waiting for the second call leg.");
                    secondLegDialogue.Hangup(m_sipTransport, m_outboundProxy);
                    return;
                }

                SDP secondLegSDP = SDP.ParseSDPDescription(secondLegDialogue.RemoteSDP);
                string call2SDPIPAddress = secondLegSDP.Connection.ConnectionAddress;
                int call2SDPPort = secondLegSDP.Media[0].Port;
                Log("The second call leg to " + dest2 + " was successful, audio socket=" + call2SDPIPAddress + ":" + call2SDPPort + ".");

                // Persist the second leg dialogue and update the bridge ID on the first call leg.
                Guid bridgeId = Guid.NewGuid();
                secondLegDialogue.BridgeId = bridgeId;
                m_sipDialoguePersistor.Add(new SIPDialogueAsset(secondLegDialogue));
                m_sipDialoguePersistor.UpdateProperty(firstLegDialog.Id, "BridgeID", bridgeId.ToString());

                //m_callManager.CreateDialogueBridge(m_firstLegDialogue, secondLegDialogue, m_username);

                Log("Re-inviting Callback dialogues to each other.");

                m_callManager.ReInvite(m_firstLegDialogue, secondLegDialogue);
                //m_callManager.ReInvite(secondLegDialogue, m_firstLegDialogue.RemoteSDP);

                SendRTPPacket(call2SDPIPAddress + ":" + call2SDPPort, call1SDPIPAddress + ":" + call1SDPPort);
                SendRTPPacket(call1SDPIPAddress + ":" + call1SDPPort, call2SDPIPAddress + ":" + call2SDPPort);
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallbackApp. " + excp);
                Log("Exception in Callback. " + excp);
            }
            finally
            {
                if (!ts.IsCancellationRequested)
                {
                    ts.Cancel();
                }
            }
        }


        private SIPDialogue Dial(
           ForkCall call,
          string data,
          int ringTimeout,
          int answeredCallLimit,
          SIPRequest clientRequest,
          List<string> customHeaders) {

            SIPDialogue answeredDialogue = null;
            ManualResetEvent waitForCallCompleted = new ManualResetEvent(false);
            
            //call.CallProgress += (s, r, h, t, b) => { Log("Progress response of " + s + " received on CallBack Dial" + "."); };
            call.CallProgress += CallProgress;
            call.CallFailed += (s, r, h) => { waitForCallCompleted.Set(); };
            call.CallAnswered += (s, r, toTag, h, t, b, d, transferMode) => { answeredDialogue = d; waitForCallCompleted.Set(); };
           
            try {
                Queue<List<SIPCallDescriptor>> callsQueue = m_dialStringParser.ParseDialString(DialPlanContextsEnum.Script, clientRequest, data, customHeaders, null, null, null, null, null, null, null, CustomerServiceLevels.None);
                call.Start(callsQueue);

                // Wait for an answer.
                ringTimeout = (ringTimeout > MAXCALLBACK_RINGTIME_SECONDS || ringTimeout <= 0) ? MAXCALLBACK_RINGTIME_SECONDS : ringTimeout;
                logger.Debug("Set callback cancel timeout to " + ringTimeout + " seconds.");
                if (!waitForCallCompleted.WaitOne(ringTimeout * 1000, false)) {
                    call.CancelNotRequiredCallLegs(CallCancelCause.TimedOut);
                }

                logger.Debug("Callback dial returning has dialogue ? " + (answeredDialogue == null) + ".");

                return answeredDialogue;
            }
            catch (Exception excp) {
                logger.Error("Exception CallbackApp Dial. " + excp);
                return null;
            }
        }

        private void CallProgress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody, ISIPClientUserAgent uac)
        {
            try
            {
                Log("Progress response of " + progressStatus + " received on CallBack Dial" + ".");

                if (m_firstLegDialogue != null && !progressBody.IsNullOrBlank() && !m_firstLegEarlyMediaSet)
                {
                    m_firstLegEarlyMediaSet = true;
                    // The first leg is up and a call on the second leg has some early media that can be passed on.
                    //m_callManager.ReInvite(m_firstLegDialogue, progressBody);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallbackApp. " + excp.Message);
            }
        }

        private SIPRequest GetCallbackInviteRequest(IPEndPoint localSIPEndPoint, string sdp)
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

            if (sdp == null)
            {
                sdp =
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
            }
            inviteHeader.ContentLength = sdp.Length;
            inviteRequest.Body = sdp;

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
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, message, m_username));
        }
    }
}
