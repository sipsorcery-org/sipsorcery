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
using System.Net.Sockets;
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
        private static int SIP_DEFAULT_PORT = SIPConstants.DEFAULT_SIP_PORT;
        private static int TRANSFER_RESPONSE_TIMEOUT_SECONDS = 10;

        private ILog logger = AppState.logger;

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;    // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.

        private string m_sipUsername = SIPSoftPhoneState.SIPUsername;
        private string m_sipPassword = SIPSoftPhoneState.SIPPassword;
        private string m_sipServer = SIPSoftPhoneState.SIPServer;
        private string m_sipFromName = SIPSoftPhoneState.SIPFromName;
        private string m_DnsServer = SIPSoftPhoneState.DnsServer;

        private SIPTransport m_sipTransport;
        private SIPUserAgent m_userAgent;
        private MediaManager _mediaManager;
        bool _isIntialised = false;
        private SIPServerUserAgent m_pendingIncomingCall;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action CallAnswer;                 // Fires when an outgoing SIP call is answered.
        public event Action CallEnded;                  // Fires when an incoming or outgoing call is over.
        public event Action IncomingCall;               // Fires when an incoming call request is received.
        public event Action RemotePutOnHold;            // Fires when the remote call party puts us on hold.
        public event Action RemoteTookOffHold;          // Fires when the remote call party takes us off hold.
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
                        SIPUDPChannel udpChannel = null;
                        try
                        {
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_DEFAULT_PORT));
                        }
                        catch (SocketException bindExcp)
                        {
                            logger.Warn($"Socket exception attempting to bind UDP channel to port {SIP_DEFAULT_PORT}, will use random port. {bindExcp.Message}.");
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }
                        var tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udpChannel.Port));
                        m_sipTransport.AddSIPChannel(new List<SIPChannel> { udpChannel, tcpChannel });
                    }
                });

                // Wire up the transport layer so incoming SIP requests have somewhere to go.
                m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                m_userAgent = new SIPUserAgent(m_sipTransport, null);
                m_userAgent.ClientCallTrying += CallTrying;
                m_userAgent.ClientCallRinging += CallRinging;
                m_userAgent.ClientCallAnswered += CallAnswered;
                m_userAgent.ClientCallFailed += CallFailed;
                m_userAgent.OnCallHungup += CallFinished;
                m_userAgent.ServerCallCancelled += IncomingCallCancelled;
                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                // Log all SIP packets received to a log file.
                m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { logger.Debug("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
                m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { logger.Debug("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
                m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { logger.Debug("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
                m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { logger.Debug("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
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
            if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                if (m_userAgent?.IsCallActive == true)
                {
                    StatusMessage($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                    // If we are already on a call return a busy response.
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(m_sipTransport, sipRequest, null);
                    SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                    uasTransaction.SendFinalResponse(busyResponse);
                }
                else
                {
                    StatusMessage($"Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                    m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
                    IncomingCall();
                }
            }
            else
            {
                logger.Debug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
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
        /// Cancels an outgoing SIP call that hasn't yet been answered.
        /// </summary>
        public void Cancel()
        {
            StatusMessage("Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
            m_userAgent.Cancel();
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
        /// Sends a request to the remote call party to initiate a blind transfer to the
        /// supplied destination.
        /// </summary>
        /// <param name="destination">The SIP URI of the blind transfer destination.</param>
        /// <returns>True if the transfer was accepted or false if not.</returns>
        public async Task<bool> Transfer(string destination)
        {
            if (SIPURI.TryParse(destination, out var uri))
            {
                return await m_userAgent.Transfer(uri, TimeSpan.FromSeconds(TRANSFER_RESPONSE_TIMEOUT_SECONDS), _cts.Token);
            }
            else
            {
                StatusMessage($"The transfer destination was not a valid SIP URI.");
                return false;
            }
        }

        /// <summary>
        /// Puts the remote call party on hold.
        /// </summary>
        public void PutOnHold()
        {
            m_userAgent.PutOnHold();
            // At this point we could stop listening to the remote party's RTP and play something 
            // else and also stop sending our microphone output and play some music.
            StatusMessage("Remote party put on hold");
        }

        /// <summary>
        /// Takes the remote call party off hold.
        /// </summary>
        public void TakeOffHold()
        {
            m_userAgent.TakeOffHold();
            // At ths point we should reverse whatever changes we made to the media stream when we
            // put the remote call part on hold.
            StatusMessage("Remote taken off on hold");
        }

        /// <summary>
        /// Event handler that notifies us the remote party has put us on hold.
        /// </summary>
        private void OnRemotePutOnHold()
        {
            _mediaManager.StopSending();
            RemotePutOnHold?.Invoke();
        }

        /// <summary>
        /// Event handler that notifies us the remote party has taken us off hold.
        /// </summary>
        private void OnRemoteTookOffHold()
        {
            _mediaManager.RestartSending();
            RemoteTookOffHold?.Invoke();
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

            m_pendingIncomingCall = null;

            CallEnded();
        }

        /// <summary>
        /// An incoming call was cancelled by the caller.
        /// </summary>
        private void IncomingCallCancelled(ISIPServerUserAgent uas)
        {
            //SetText(m_signallingStatus, "incoming call cancelled for: " + uas.CallDestination + ".");
            CallFinished();
        }
    }
}
