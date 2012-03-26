//-----------------------------------------------------------------------------
// Filename: SoftphoneMain.xaml.cs
//
// Description: Main XAML interface for softphone. 
// 
// History:
// 11 Mar 2012	Aaron Clauson	Refactored.
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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.XMPP;
using Heijden.DNS;
using log4net;
using NAudio;
using NAudio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SIPSorcery.SoftPhone
{
    public partial class SoftPhone : Window
    {
        private const int DNS_LOOKUP_TIMEOUT = 10000;
        private const string STUN_CLIENT_THREAD_NAME = "sipproxy-stunclient";
        private const string GINGLE_PREFIX = "gingle:";
        private const string XMPP_SERVER = "talk.google.com";
        private const int XMPP_SERVER_PORT = 5222;
        private const string XMPP_REALM = "google.com";
        private const string GOOGLE_VOICE_HOST = "voice.google.com";

        private delegate void SetVisibilityDelegate(UIElement element, Visibility visibility);

        private ILog logger = SIPSoftPhoneState.logger;
        private ILog _audioDeviceLogger = AppState.GetLogger("audiodevice");
        private ILog _sipTraceLogger = AppState.GetLogger("siptrace");

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;
        private string m_stunServerHostname = SIPSoftPhoneState.STUNServerHostname;

        private SIPTransport m_sipTransport;
        private SIPClientUserAgent m_uac;
        private SIPServerUserAgent m_uas;
        private RTPChannel m_rtpChannel;
        private BufferedWaveProvider m_waveProvider;
        private DateTime _lastInputSampleReceivedAt;
        private int _inputSampleCount;
        private WaveInEvent m_waveInEvent;
        private WaveOut m_waveOut;
        private ManualResetEvent m_dnsLookupComplete = new ManualResetEvent(false);
        private bool m_stop;
        private ManualResetEvent m_stunClientMRE = new ManualResetEvent(false);     // Used to set the interval on the STUN lookups and also allow the thread to be stopped.
        private int m_localRTPPacketCount;
        private int m_remoteRTPPacketCount;

        // Gingle variables.
        private XMPPClient m_xmppClient;
        private XMPPPhoneSession m_xmppCall;
        private string m_xmppUsername = "";
        private string m_xmppPassword = "";
        private string m_localSTUNUFrag;

        public IPAddress PublicIPAddress;

        public SoftPhone()
        {
            InitializeComponent();

            m_uasGrid.Visibility = Visibility.Collapsed;

            m_cancelButton.Visibility = Visibility.Collapsed;
            m_byeButton.Visibility = Visibility.Collapsed;

            if (!m_stunServerHostname.IsNullOrBlank())
            {
                // If a STUN server hostname has been specified start the STUN client thread.
                ThreadPool.QueueUserWorkItem(delegate { StartSTUNClient(); });
            }

            ThreadPool.QueueUserWorkItem(InitialiseSIP);
            SIPDNSManager.SIPMonitorLogEvent += (e) => { logger.Debug(e.Message); };

            m_waveOut = new WaveOut();
            m_waveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1));
            m_waveOut.Init(m_waveProvider);
            m_waveOut.Play();

            m_waveInEvent = new WaveInEvent();
            m_waveInEvent.BufferMilliseconds = 20;
            m_waveInEvent.NumberOfBuffers = 1;
            m_waveInEvent.DeviceNumber = 0;
            m_waveInEvent.DataAvailable += RTPChannelSampleAvailable;
            m_waveInEvent.WaveFormat = new WaveFormat(8000, 16, 1);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            m_stop = true;

            if (m_sipTransport != null)
            {
                m_sipTransport.Shutdown();
            }

            DNSManager.Stop();
        }

        private void StartSTUNClient()
        {
            try
            {
                Thread.CurrentThread.Name = STUN_CLIENT_THREAD_NAME;

                logger.Debug("STUN client started.");

                while (!m_stop)
                {
                    try
                    {
                        IPAddress publicIP = STUNClient.GetPublicIPAddress(m_stunServerHostname);
                        if (publicIP != null)
                        {
                            logger.Debug("The STUN client was able to determine the public IP address as " + publicIP.ToString() + ".");
                            PublicIPAddress = publicIP;
                        }
                        else
                        {
                            logger.Debug("The STUN client could not determine the public IP address.");
                            PublicIPAddress = null;
                        }
                    }
                    catch (Exception getAddrExcp)
                    {
                        logger.Error("Exception StartSTUNClient GetPublicIPAddress. " + getAddrExcp.Message);
                    }

                    m_stunClientMRE.Reset();
                    m_stunClientMRE.WaitOne(60000);
                }

                logger.Warn("STUN client thread stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception StartSTUNClient. " + excp.Message);
            }
        }

        private void InitialiseSIP(object state)
        {
            // Configure the SIP transport layer.
            m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
            List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipSocketsNode);
            m_sipTransport.AddSIPChannel(sipChannels);

            m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

            m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { _sipTraceLogger.Debug("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
            m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
            m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { _sipTraceLogger.Debug("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
        }

        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                if (m_uac != null && m_uac.SIPDialogue != null && sipRequest.Header.CallId == m_uac.SIPDialogue.CallId)
                {
                    // Call has been hungup by remote end.
                    logger.Debug("Call hungup by server: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    SetText(m_signallingStatus, "Call hungup by server: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    _sipTraceLogger.Debug("Request Received " + localSIPEndPoint + "<-" + remoteEndPoint + "\n" + sipRequest.ToString());
                    SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    //logger.Debug("Matching dialogue found for BYE request to " + sipRequest.URI.ToString() + ".");
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);
                    ResetToCallStartState();
                }
                else if (m_uas != null && m_uas.SIPDialogue != null && sipRequest.Header.CallId == m_uas.SIPDialogue.CallId)
                {
                    // Call has been hungup by remote end.
                    logger.Debug("Call hungup by client: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    SetText(m_signallingStatus, "Call hungup by client: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    _sipTraceLogger.Debug("Request Received " + localSIPEndPoint + "<-" + remoteEndPoint + "\n" + sipRequest.ToString());
                    m_uas.SIPDialogue.Hangup(m_sipTransport, null);
                    ResetToCallStartState();
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
                ResetToCallStartState();

                logger.Debug("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                SetText(m_signallingStatus, "incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                SetVisibility(m_uacGrid, Visibility.Collapsed);
                SetVisibility(m_uasGrid, Visibility.Visible);

                UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                m_uas = new SIPServerUserAgent(m_sipTransport, null, null, null, SIPCallDirection.In, null, null, LogTraceMessage, uasTransaction);
                m_uas.CallCancelled += UASCallCancelled;
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    logger.Debug("Matching CANCEL request received " + sipRequest.URI.ToString() + ".");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                    cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    logger.Debug("No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else
            {
                logger.Debug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                m_sipTransport.SendResponse(notAllowedResponse);
            }
        }

        private void UASCallCancelled(ISIPServerUserAgent uas)
        {
            logger.Debug("Incoming call cancelled for: " + uas.CallDestination + ".");
            SetText(m_signallingStatus, "incoming call cancelled for: " + uas.CallDestination + ".");
            ResetToCallStartState();
        }

        private void ResetToCallStartState()
        {
            SetVisibility(m_callButton, Visibility.Visible);
            SetVisibility(m_cancelButton, Visibility.Collapsed);
            SetVisibility(m_byeButton, Visibility.Collapsed);
            SetVisibility(m_answerButton, Visibility.Visible);
            SetVisibility(m_rejectButton, Visibility.Visible);
            SetVisibility(m_redirectButton, Visibility.Visible);
            SetVisibility(m_hangupButton, Visibility.Visible);
            SetVisibility(m_uacGrid, Visibility.Visible);
            SetVisibility(m_uasGrid, Visibility.Collapsed);

            m_rtpChannel.Close();
            m_waveInEvent.StopRecording();
            Dispatcher.Invoke(new Action(() =>
            {
                if (m_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    m_waveOut.Stop(); m_waveOut.Dispose();
                }
            }), null);
        }

        private void CallButton_Click(object sender, RoutedEventArgs e)
        {
            logger.Debug("Call starting: " + m_uriEntryTextBox.Text + ".");
            SetText(m_signallingStatus, "calling " + m_uriEntryTextBox.Text + ".");

            m_localRTPPacketCount = 0;
            m_remoteRTPPacketCount = 0;
            SetText(m_remoteRTPStatus, "waiting for data...");
            SetText(m_localRTPStatus, "waiting for data...");
            SetText(m_remoteRTPCount, "0");
            SetText(m_localRTPCount, "0");

            m_callButton.Visibility = Visibility.Collapsed;
            m_cancelButton.Visibility = Visibility.Visible;
            m_byeButton.Visibility = Visibility.Collapsed;

            string callURI = m_uriEntryTextBox.Text;

            if (callURI.StartsWith(GINGLE_PREFIX))
            {
                m_xmppClient = new XMPPClient(XMPP_SERVER, XMPP_SERVER_PORT, XMPP_REALM, m_xmppUsername, m_xmppPassword);
                m_xmppClient.Disconnected += XMPPDisconnected;
                m_xmppClient.IsBound += () => { XMPPPlaceCall(callURI.Replace(GINGLE_PREFIX, "")); };
                ThreadPool.QueueUserWorkItem(delegate { m_xmppClient.Connect(); });
            }
            else
            {
                // Standard SIP call.
                SIPURI uri = SIPURI.ParseSIPURIRelaxed(callURI);
                ThreadPool.QueueUserWorkItem(delegate { PlaceSIPCall(uri); });
            }
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_uac != null)
            {
                logger.Debug("Cancelling: " + m_uriEntryTextBox.Text + ".");
                SetText(m_signallingStatus, "Cancelling: " + m_uriEntryTextBox.Text + ".");
                m_uac.Cancel();
            }
        }

        private void ByeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uac.SIPDialogue.Hangup(m_sipTransport, null);
            ResetToCallStartState();
        }

        private void AnswerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Answer(null, null, null, SIPDialogueTransferModesEnum.NotAllowed);
            SetVisibility(m_answerButton, Visibility.Collapsed);
            SetVisibility(m_rejectButton, Visibility.Collapsed);
        }

        private void RejectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
            ResetToCallStartState();
        }

        private void RedirectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_uas.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURI(m_redirectURIEntryTextBox.Text));
            ResetToCallStartState();
        }

        private void HangupButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ResetToCallStartState();
        }

        private void DNSLookup(object state)
        {
            string callURIStr = (string)state;
            List<SIPDNSLookupEndPoint> results = SIPDNSManager.ResolveSIPService(callURIStr).EndPointResults;

            if (results != null)
            {
                logger.Debug("DNS result for " + callURIStr + " is " + results[0].LookupEndPoint.ToString() + ".");
            }
            else
            {
                logger.Debug("DNS lookup for " + callURIStr + " failed.");
            }

            /*if (callURI != null)
            {
                VerboseDNSLookup(callURI.Host, DNSQType.NAPTR);
                VerboseDNSLookup("_sip._tls." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup("_sip._tcp." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup("_sip._udp." + callURI.Host, DNSQType.SRV);
                VerboseDNSLookup(callURI.Host, DNSQType.A);
            }
            else
            {
                AppendTraceMessage("SIP URI could not be parsed from " + callURIStr + ".\n");
            }*/

            m_dnsLookupComplete.Set();
        }

        private void VerboseDNSLookup(string host, DNSQType queryType)
        {
            logger.Debug("Looking up " + host + " query type " + queryType.ToString() + ".");

            try
            {
                //DNSResponse dnsResponse = DNSManager.Lookup(host, queryType, 5, new List<IPEndPoint>{new IPEndPoint(IPAddress.Parse("128.59.16.20"), 53)}, false, false);
                DNSResponse dnsResponse = DNSManager.Lookup(host, queryType, 5, null, false, false);
                if (dnsResponse.Error == null)
                {
                    List<AnswerRR> records = dnsResponse.Answers;
                    //if (records.Count > 0)
                    if (dnsResponse.RecordNAPTR.Length > 0)
                    {
                        logger.Debug("Results for " + host + " query type " + queryType.ToString() + ".");
                        //foreach (AnswerRR record in records)
                        foreach (RecordNAPTR record in dnsResponse.RecordNAPTR)
                        {
                            logger.Debug(record.ToString());
                        }
                    }
                    else
                    {
                        logger.Debug("Empty result returned for " + host + " query type " + queryType.ToString() + ".");
                    }
                }
                else
                {
                    logger.Debug("DNS lookup error for " + host + " query type " + queryType.ToString() + ", " + dnsResponse.Error + ".");
                }
            }
            catch (ApplicationException appExcp)
            {
                logger.Error(appExcp.Message);
            }
        }

        private void PlaceSIPCall(SIPURI callURI)
        {
            logger.Debug("Starting call to " + callURI.ToString() + ".");
            SetText(m_signallingStatus, "starting call to " + callURI.ToString() + ".");
            m_uac = new SIPClientUserAgent(m_sipTransport, null, null, null, LogTraceMessage);
            m_uac.CallTrying += CallTrying;
            m_uac.CallRinging += CallRinging;
            m_uac.CallAnswered += CallAnswered;
            m_uac.CallFailed += CallFailed;
            SDP sdp = null;
            if (IPSocket.IsIPAddress(callURI.Host) && IPSocket.IsPrivateAddress(callURI.Host))
            {
                sdp = GetSDP(IPAddress.Parse("10.1.1.2"), 10000);
            }
            else
            {
                sdp = GetSDP(PublicIPAddress, 10000);
            }
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor("anonymous", null, callURI.ToString(), null, null, null, null, null, SIPCallDirection.Out, SDP.SDP_MIME_CONTENTTYPE, sdp.ToString(), null);
            m_rtpChannel = new RTPChannel(IPSocket.ParseSocketString("10.1.1.2:10000"));
            m_rtpChannel.SampleReceived += RTPChannelSampleReceived;
            m_uac.Call(callDescriptor);
        }

        private void XMPPDisconnected()
        {
            logger.Debug("The XMPP client was disconnected.");
            m_xmppClient = null;
        }

        private void XMPPPlaceCall(string destination)
        {
            m_rtpChannel = new RTPChannel(IPSocket.ParseSocketString("10.1.1.2:10000"));
            m_rtpChannel.SampleReceived += RTPChannelSampleReceived;

            // Call to Google Voice over XMPP & Gingle (Google's version of Jingle).
            XMPPPhoneSession phoneSession = m_xmppClient.GetPhoneSession();

            m_xmppCall = m_xmppClient.GetPhoneSession();
            m_xmppCall.Accepted += XMPPAnswered;
            m_xmppCall.Rejected += XMPPCallFailed;
            m_xmppCall.Hungup += XMPPHangup;

            m_localSTUNUFrag = Crypto.GetRandomString(8);
            SDP xmppSDP = GetSDP(PublicIPAddress, 10000);
            xmppSDP.IcePwd = Crypto.GetRandomString(12);
            xmppSDP.IceUfrag = m_localSTUNUFrag;

            m_xmppCall.PlaceCall(destination + "@" + GOOGLE_VOICE_HOST, xmppSDP);
        }

        private SDP GetSDP(IPAddress rtpIPAddress, int rtpPort)
        {
            var sdp = new SDP()
            {
                SessionId = Crypto.GetRandomString(6),
                Address = rtpIPAddress.ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(rtpIPAddress.ToString()),
                Media = new List<SDPMediaAnnouncement>() 
                {
                    new SDPMediaAnnouncement()
                    {
                        Media = SDPMediaTypesEnum.audio,
                        Port = rtpPort,
                        MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU) }
                    }
                }
            };

            return sdp;
        }

        private void CallFailed(ISIPClientUserAgent uac, string errorMessage)
        {
            logger.Debug("Call failed: " + errorMessage + ".");
            SetText(m_signallingStatus, "Call failed: " + errorMessage + ".");
            ResetToCallStartState();
        }

        private void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            logger.Debug("Call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            SetText(m_signallingStatus, "call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");

            if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
            {
                SetVisibility(m_callButton, Visibility.Collapsed);
                SetVisibility(m_cancelButton, Visibility.Collapsed);
                SetVisibility(m_byeButton, Visibility.Visible);

                IPEndPoint remoteSDPEndPoint = SDP.GetSDPRTPEndPoint(sipResponse.Body);
                logger.Debug("Remote SDP end point " + remoteSDPEndPoint + ".");
                SetText(m_remoteRTPStatus, "Remote SDP end point " + remoteSDPEndPoint + ".");
                m_rtpChannel.SetRemoteEndPoint(remoteSDPEndPoint);
                m_waveInEvent.StartRecording();
                _lastInputSampleReceivedAt = DateTime.Now;
            }
            else
            {
                ResetToCallStartState();
            }
        }

        private void RTPChannelSampleReceived(byte[] sample)
        {
            m_remoteRTPPacketCount++;
            SetText(m_remoteRTPCount, m_remoteRTPPacketCount.ToString());

            if (sample != null)
            {
                for (int index = 12; index < sample.Length; index++)
                {
                    short pcm = MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    m_waveProvider.AddSamples(pcmSample, 0, 2);
                }
            }
        }

        private void RTPChannelSampleAvailable(object sender, WaveInEventArgs e)
        {
            TimeSpan samplePeriod = DateTime.Now.Subtract(_lastInputSampleReceivedAt);
            _lastInputSampleReceivedAt = DateTime.Now;
            _inputSampleCount++;

            _audioDeviceLogger.Debug(_inputSampleCount + " sample period " + samplePeriod.TotalMilliseconds + "ms,  sample bytes " + e.BytesRecorded + ".");

            m_localRTPPacketCount++;
            SetText(m_localRTPCount, m_localRTPPacketCount.ToString());

            byte[] sample = new byte[e.Buffer.Length / 2];
            int sampleIndex = 0;

            for (int index = 0; index < e.Buffer.Length; index += 2)
            {
                var ulawByte = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(e.Buffer, index));
                sample[sampleIndex++] = ulawByte;
            }

            m_rtpChannel.Send(sample, 160);
        }

        private void CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            logger.Debug("Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            SetText(m_signallingStatus, "Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        private void CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            logger.Debug("Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            SetText(m_signallingStatus, "call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        private void LogTraceMessage(SIPMonitorEvent monitorEvent)
        {
            if (monitorEvent is SIPMonitorConsoleEvent)
            {
                if (((SIPMonitorConsoleEvent)monitorEvent).EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                    ((SIPMonitorConsoleEvent)monitorEvent).EventType != SIPMonitorEventTypesEnum.SIPTransaction)
                {
                    logger.Debug(monitorEvent.Message);
                }
                else
                {
                    _sipTraceLogger.Debug(monitorEvent.Message);
                }
            }
        }

        private void SetText(TextBlock textBlock, string text)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new Action<TextBlock, string>(SetText), textBlock, text);
                return;
            }

            textBlock.Text = text;
        }

        private void SetVisibility(UIElement element, Visibility visibility)
        {
            if (Thread.CurrentThread != Dispatcher.Thread)
            {
                Dispatcher.Invoke(new SetVisibilityDelegate(SetVisibility), element, visibility);
                return;
            }

            element.Visibility = visibility;
        }

        private void XMPPAnswered(SDP xmppSDP)
        {
            logger.Debug("Call answered.");
            SetText(m_signallingStatus, "call answered.");

            IPEndPoint remoteSDPEndPoint = SDP.GetSDPRTPEndPoint(xmppSDP.ToString());
            logger.Debug("Remote SDP end point " + remoteSDPEndPoint + ".");
            SetText(m_remoteRTPStatus, "Remote SDP end point " + remoteSDPEndPoint + ".");
            m_rtpChannel.SetRemoteEndPoint(remoteSDPEndPoint);

            STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            initMessage.AddUsernameAttribute(xmppSDP.IceUfrag + m_localSTUNUFrag);
            byte[] stunMessageBytes = initMessage.ToByteBuffer();
            m_rtpChannel.SendRaw(stunMessageBytes, stunMessageBytes.Length);

            m_waveInEvent.StartRecording();
            _lastInputSampleReceivedAt = DateTime.Now;
            //logger.Debug("Sending STUN binding request to " + m_xmppServerEndPoint + ".");
            //STUNMessage initMessage = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            //initMessage.AddUsernameAttribute(xmppSDP.IceUfrag + m_localSTUNUFrag);
            //byte[] stunMessageBytes = initMessage.ToByteBuffer();
            //m_xmppMediaSocket.Send(stunMessageBytes, stunMessageBytes.Length, m_xmppServerEndPoint);
        }

        private void XMPPCallFailed()
        {
            logger.Debug("XMPP call failed.");
            SetText(m_signallingStatus, "XMPP call failed.");
            ResetToCallStartState();
        }

        private void XMPPHangup()
        {
            logger.Debug("XMPP call terminated.");
            SetText(m_signallingStatus, "XMPP call terminated.");
            ResetToCallStartState();
        }
    }
}
