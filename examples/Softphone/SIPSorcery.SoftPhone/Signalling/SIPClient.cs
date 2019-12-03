//-----------------------------------------------------------------------------
// Filename: SIPClient.cs
//
// Description: A SIP client for making and receiving calls. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
// 03 Dec 2019  Aaron Clauson   Replace separate client and server user agents with full user agent.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SoftPhone
{
    public class SIPClient : IVoIPClient
    {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;

        private ILog logger = AppState.logger;
        private ILog _sipTraceLogger = AppState.GetLogger("siptrace");

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;                // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;
        private string m_sipFromName = SIPSoftPhoneState.SIPFromName;
        private string m_DnsServer = SIPSoftPhoneState.DnsServer;

        private SIPTransport m_sipTransport;                                                // SIP transport layer.
        private SIPUserAgent m_userAgent;
        private ManualResetEvent m_dnsLookupComplete = new ManualResetEvent(false);
        private MediaManager _mediaManager;
        bool _isIntialised = false;
        private SIPServerUserAgent m_pendingIncomingCall;

        public event Action CallAnswer;                 // Fires when an outgoing SIP call is answered.
        public event Action CallEnded;                  // Fires when an incoming or outgoing call is over.
        public event Action IncomingCall;               // Fires when an incoming call request is received.
        public event Action<string> StatusMessage;      // Fires when the SIP client has a status message it wants to inform the UI about.

        public SIPTransport SIPClientTransport
        {
            get { return m_sipTransport; }
        }

        public SIPClient()
        { }

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
        public async Task InitialiseSIP()
        {
            if (_isIntialised == false)
            {
                await Task.Run(() =>
                {
                    _isIntialised = true;

                    if (String.IsNullOrEmpty(m_DnsServer) == false)
                    {
                        // Use a custom DNS server.
                        m_DnsServer = m_DnsServer.Contains(":") ? m_DnsServer : m_DnsServer + ":53";
                        DNSManager.SetDNSServers(new List<IPEndPoint> { IPSocket.ParseSocketString(m_DnsServer) });
                    }

                    // Configure the SIP transport layer.
                    m_sipTransport = new SIPTransport();
                    bool sipChannelAdded = false;

                    if (m_sipSocketsNode != null)
                    {
                        // Set up the SIP channels based on the app.config file.
                        List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipSocketsNode);
                        if (sipChannels?.Count > 0)
                        {
                            m_sipTransport.AddSIPChannel(sipChannels);
                            sipChannelAdded = true;
                        }
                    }

                    if (sipChannelAdded == false)
                    {
                        // Use default options to set up a SIP channel.
                        var sipChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                        m_sipTransport.AddSIPChannel(sipChannel);
                    }
                });

                // Wire up the transport layer so incoming SIP requests have somewhere to go.
                m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                m_userAgent = new SIPUserAgent(m_sipTransport, null);
                m_userAgent.ClientCallTrying += CallTrying;
                m_userAgent.ClientCallRinging += CallRinging;
                m_userAgent.ClientCallAnswered += CallAnswered;
                m_userAgent.ClientCallFailed += CallFailed;

                // Log all SIP packets received to a log file.
                m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
                m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
                m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
                m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
            }
        }

        /// <summary>
        /// Handler for processing incoming SIP requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the request was received on.</param>
        /// <param name="remoteEndPoint">The end point the request came from.</param>
        /// <param name="sipRequest">The SIP request received.</param>
        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Header.From != null &&
                sipRequest.Header.From.FromTag != null &&
                sipRequest.Header.To != null &&
                sipRequest.Header.To.ToTag != null)
            {
                // In dialog request will include BYE's.
                m_userAgent.DialogRequestReceivedAsync(sipRequest).Wait();
            }
            else if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                // If a BYE request isn't detected as belonging to our dialog then it;s an orphan.
                logger.Debug("Unmatched BYE request received for " + sipRequest.URI.ToString() + ".");
                SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                m_sipTransport.SendResponse(noCallLegResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                if (m_userAgent?.IsCallActive == true)
                {
                    StatusMessage($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                    // If we are already on a call return a busy response.
                    UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, null);
                    SIPResponse busyResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                    uasTransaction.SendFinalResponse(busyResponse);
                }
                else
                {
                    StatusMessage($"Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                    m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
                    m_pendingIncomingCall.CallCancelled += UASCallCancelled;
                    IncomingCall();
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    StatusMessage("Call was cancelled by remote end.");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, inviteTransaction);
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
        public async void Call(MediaManager mediaManager, string destination)
        {
            //_initialisationTask.Wait(_cancelCallTokenSource.Token);

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

            StatusMessage($"Starting call to {callURI}.");

            var lookupResult = await Task.Run(() =>
            {
                var result = SIPDNSManager.ResolveSIPService(callURI, false);
                return result;
            });

            if (lookupResult == null || lookupResult.LookupError != null)
            {
                StatusMessage($"Call failed, could not resolve {callURI}.");
            }
            else
            {
                StatusMessage($"Call progressing, resolved {callURI} to {lookupResult.GetSIPEndPoint()}.");
                System.Diagnostics.Debug.WriteLine($"DNS lookup result for {callURI}: {lookupResult.GetSIPEndPoint()}.");
                var dstAddress = lookupResult.GetSIPEndPoint().Address;

                SDP sdp = _mediaManager.GetSDP(dstAddress);
                System.Diagnostics.Debug.WriteLine(sdp.ToString());
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(sipUsername, sipPassword, callURI.ToString(), fromHeader, null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, sdp.ToString(), null);
                m_userAgent.Call(callDescriptor);
            }
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
            StatusMessage("Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
            m_userAgent.Cancel();
            //_cancelCallTokenSource.Cancel();
        }

        /// <summary>
        /// Answers an incoming SIP call.
        /// </summary>
        public void Answer(MediaManager mediaManager)
        {
            if (m_pendingIncomingCall == null)
            {
                StatusMessage($"There was no pending call available to answer.");
            }
            else
            {
                _mediaManager = mediaManager;
                _mediaManager.NewCall();

                SDP sdpAnswer = SDP.ParseSDPDescription(m_pendingIncomingCall.CallRequest.Body);
                _mediaManager.SetRemoteSDP(sdpAnswer);

                SDP sdp = _mediaManager.GetSDP(m_pendingIncomingCall.CallRequest.RemoteSIPEndPoint.Address);
                m_userAgent.Answer(m_pendingIncomingCall, sdp);

                m_pendingIncomingCall = null;
            }
        }

        /// <summary>
        /// Redirects an incoming SIP call.
        /// </summary>
        public void Redirect(string destination)
        {
            m_pendingIncomingCall?.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        /// <summary>
        /// Rejects an incoming SIP call.
        /// </summary>
        public void Reject()
        {
            m_pendingIncomingCall?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        /// <summary>
        /// Hangsup an established SIP call.
        /// </summary>
        public void Hangup()
        {
            m_userAgent.Hangup();
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
                if (sipResponse.Header.ContentType != _sdpMimeContentType)
                {
                    // Payload not SDP, I don't understand :(.
                    StatusMessage("Call was hungup as the answer response content type was not recognised: " + sipResponse.Header.ContentType + ". :(");
                    Hangup();
                }
                else if (sipResponse.Body.IsNullOrBlank())
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

            //_cancelCallTokenSource.Cancel();

            m_pendingIncomingCall = null;

            CallEnded();
        }
    }
}
