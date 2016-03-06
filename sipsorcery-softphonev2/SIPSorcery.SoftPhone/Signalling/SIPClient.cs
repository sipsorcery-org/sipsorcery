//-----------------------------------------------------------------------------
// Filename: SIPClient.cs
//
// Description: A SIP client for making and receiving calls. 
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Ltd. 
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
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Sys.Net;
using Heijden.DNS;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class SIPClient : IVoIPClient
    {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;

        private ILog logger = AppState.logger;
        private ILog _sipTraceLogger = AppState.GetLogger("siptrace");

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;                // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.
        private IPAddress _defaultLocalAddress = SIPSoftPhoneState.DefaultLocalAddress;     // The default IPv4 address for the machine running the application.
        private int _defaultSIPUdpPort = SIPConstants.DEFAULT_SIP_PORT;                     // The default UDP SIP port.

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;    
        private string m_sipServer = SIPSoftPhoneState.SIPServer;
        private string m_sipFromName = ConfigurationManager.AppSettings["SIPFromName"];    // Get the SIP From display name from the config file.

        private SIPTransport m_sipTransport;                                                // SIP transport layer.
        private SIPClientUserAgent m_uac;                                                   // A SIP user agent client used to place outgoing calls.
        private SIPServerUserAgent m_uas;                                                   // A SIP user agent server used to process incoming calls.
        private ManualResetEvent m_dnsLookupComplete = new ManualResetEvent(false);
        private MediaManager _mediaManager;

        public event Action CallAnswer;                 // Fires when an incoming SIP call is answered.
        public event Action CallEnded;                  // Fires when an incoming or outgoing call is over.
        public event Action IncomingCall;               // Fires when an incoming call request is received.
        public event Action<string> StatusMessage;      // Fires when the SIP client has a staus message it wants to inform the UI about.

        public SIPTransport SIPClientTransport
        {
            get { return m_sipTransport; }
        }

        public SIPClient()
        {
            ThreadPool.QueueUserWorkItem(delegate { InitialiseSIP(); });
        }

        /// <summary>
        /// Shutdown the SIP tranpsort layer and any other resources the SIP client is using. Typically called when the application exits.
        /// </summary>
        public void Shutdown()
        {
            if (m_sipTransport != null)
            {
                m_sipTransport.Shutdown();
            }

            DNSManager.Stop();
        }

        /// <summary>
        /// Initialises the SIP transport layer.
        /// </summary>
        private void InitialiseSIP()
        {
            // Configure the SIP transport layer.
            m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());

            if (m_sipSocketsNode != null)
            {
                // Set up the SIP channels based on the app.config file.
                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipSocketsNode);
                m_sipTransport.AddSIPChannel(sipChannels);
            }
            else
            {
                // Use default options to set up a SIP channel.
                int port = FreePort.FindNextAvailableUDPPort(_defaultSIPUdpPort);
                var sipChannel = new SIPUDPChannel(new IPEndPoint(_defaultLocalAddress, port));
                m_sipTransport.AddSIPChannel(sipChannel);
            }

            // Wire up the transport layer so incoming SIP requests have somewhere to go.
            m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

            // Log all SIP packets received to a log file.
            m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
            m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
        }

        /// <summary>
        /// Handler for processing incoming SIP requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the request was received on.</param>
        /// <param name="remoteEndPoint">The end point the request came from.</param>
        /// <param name="sipRequest">The SIP request received.</param>
        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                if (m_uac != null && m_uac.SIPDialogue != null && sipRequest.Header.CallId == m_uac.SIPDialogue.CallId)
                {
                    // Call has been hungup by remote end.
                    StatusMessage("Call hungup by remote end.");
                    SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);
                    CallFinished();
                }
                else if (m_uas != null && m_uas.SIPDialogue != null && sipRequest.Header.CallId == m_uas.SIPDialogue.CallId)
                {
                    // Call has been hungup by remote end.
                    StatusMessage("Call hungup.");
                    SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);
                    CallFinished();
                }
                else
                {
                    logger.Debug("Unmatched BYE request received for " + sipRequest.URI.ToString() + ".");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                StatusMessage("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                m_uas = new SIPServerUserAgent(m_sipTransport, null, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
                m_uas.CallCancelled += UASCallCancelled;

                m_uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                m_uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                IncomingCall();
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    StatusMessage("Call was cancelled by remote end.");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                    cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    logger.Debug("No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }

                CallFinished();
            }
            else
            {
                logger.Debug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                m_sipTransport.SendResponse(notAllowedResponse);
            }
        }

        /// <summary>
        /// Places an outgoing SIP call.
        /// </summary>
        /// <param name="destination">The SIP URI to place a call to. The destination can be a full SIP URI in which case the all will
        /// be placed anonymously directly to that URI. Alternatively it can be just the user portion of a URI in which case it will
        /// be sent to the configured SIP server.</param>
        public void Call(MediaManager mediaManager, string destination)
        {
            _mediaManager = mediaManager;
            _mediaManager.NewCall();

            // Determine if this is a direct anonymous call or whether it should be placed using the pre-configured SIP server account. 
            SIPURI callURI = null;
            string sipUsername = null;
            string sipPassword = null;
            string fromHeader = null;

            if (destination.Contains("@") || m_sipServer == null)
            {
                // Anonymous call direct to SIP server specified in the URI.
                callURI = SIPURI.ParseSIPURIRelaxed(destination);
                fromHeader = (new SIPFromHeader(m_sipFromName, SIPURI.ParseSIPURI(SIPFromHeader.DEFAULT_FROM_URI), null)).ToString();
            }
            else
            {
                // This call will use the pre-configured SIP account.
                callURI = SIPURI.ParseSIPURIRelaxed(destination + "@" + m_sipServer);
                sipUsername = m_sipUsername;
                sipPassword = m_sipPassword;
                fromHeader = (new SIPFromHeader(m_sipFromName, new SIPURI(m_sipUsername, m_sipServer, null), null)).ToString();
            }

            StatusMessage("Starting call to " + callURI.ToString() + ".");
            
            m_uac = new SIPClientUserAgent(m_sipTransport, null, null, null, null);
            m_uac.CallTrying += CallTrying;
            m_uac.CallRinging += CallRinging;
            m_uac.CallAnswered += CallAnswered;
            m_uac.CallFailed += CallFailed;

            // Get the SDP requesting that the public IP address be used if the host on the call destination is not a private IP address.
            SDP sdp = _mediaManager.GetSDP(!(IPSocket.IsIPAddress(callURI.Host) && IPSocket.IsPrivateAddress(callURI.Host)));
            System.Diagnostics.Debug.WriteLine(sdp.ToString());
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(sipUsername, sipPassword, callURI.ToString(), fromHeader, null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, sdp.ToString(), null);
            m_uac.Call(callDescriptor);
        }

        /// <summary>
        /// An incoming call was cancelled by the caller.
        /// </summary>
        private void UASCallCancelled(ISIPServerUserAgent uas)
        {
            //SetText(m_signallingStatus, "incoming call cancelled for: " + uas.CallDestination + ".");
            CallFinished();
        }

        /// <summary>
        /// Cancels an outgoing SIP call that hasn't yet been answered.
        /// </summary>
        public void Cancel()
        {
            if (m_uac != null)
            {
                StatusMessage("Cancelling SIP call to " + m_uac.CallDescriptor.Uri + ".");
                m_uac.Cancel();
            }
        }

        /// <summary>
        /// Answers an incoming SIP call.
        /// </summary>
        public void Answer(MediaManager mediaManager)
        {
            _mediaManager = mediaManager;
            _mediaManager.NewCall();

            SDP sdpAnswer = SDP.ParseSDPDescription(m_uas.CallRequest.Body);
            _mediaManager.SetRemoteSDP(sdpAnswer);

            SDP sdp = _mediaManager.GetSDP(false);
            m_uas.Answer(_sdpMimeContentType, sdp.ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
        }

        /// <summary>
        /// Redirects an incoming SIP call.
        /// </summary>
        public void Redirect(string destination)
        {
            m_uas.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        /// <summary>
        /// Rejects an incoming SIP call.
        /// </summary>
        public void Reject()
        {
            m_uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        /// <summary>
        /// Hangsup an established SIP call.
        /// </summary>
        public void Hangup()
        {
            if (m_uac != null && m_uac.SIPDialogue != null)
            {
                m_uac.SIPDialogue.Hangup(m_sipTransport, null);
            }
            CallFinished();
        }

        /// <summary>
        /// A trying response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage("Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        /// <summary>
        /// A ringing response has been received from the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage("Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        /// <summary>
        /// An outgoing call was rejected by the remote SIP UAS on an outgoing call.
        /// </summary>
        private void CallFailed(ISIPClientUserAgent uac, string errorMessage)
        {
            StatusMessage("Call failed: " + errorMessage + ".");
            CallFinished();
        }

        /// <summary>
        /// An outgoing call was successfully answered.
        /// </summary>
        /// <param name="uac">The local SIP user agent client that initiated the call.</param>
        /// <param name="sipResponse">The SIP answer response received from the remote party.</param>
        private void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            StatusMessage("Call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");

            if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
            {
                if(sipResponse.Header.ContentType != _sdpMimeContentType)
                {
                    // Payload not SDP, I don't understand :(.
                    StatusMessage("Call was hungup as the answer response content type was not recognised: " + sipResponse.Header.ContentType + ". :(");
                    Hangup();
                }
                else if(sipResponse.Body.IsNullOrBlank())
                {
                    // They said SDP but didn't give me any :(.
                    StatusMessage("Call was hungup as the answer response had an empty SDP payload. :(");
                    Hangup();
                }

                SDP sdpAnswer = SDP.ParseSDPDescription(sipResponse.Body);
                System.Diagnostics.Debug.WriteLine(sipResponse.Body);
                _mediaManager.SetRemoteSDP(sdpAnswer);
                CallAnswer();
            }
            else
            {
                CallFinished();
            }
        }

        /// <summary>
        /// Cleans up after a SIP call has completely finished.
        /// </summary>
        private void CallFinished()
        {
            if (_mediaManager != null)
            {
                _mediaManager.EndCall();
                _mediaManager = null;
            }

            m_uac = null;
            m_uas = null;

            CallEnded();
        }
    }
}
