using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
//using Microsoft.Scripting;
//using Microsoft.Scripting.Interpreter;
//using Microsoft.Scripting.Hosting;
//using IronRuby;

namespace SIPSorcery
{
    public partial class SIPSwitchboard : UserControl
    {
        private ActivityMessageDelegate LogActivityMessage_External;

        private string m_owner;
        private SIPTransport m_sipTransport;
        private SilverlightTCPSIPChannel m_sipChannel;
        private SIPNonInviteTransaction m_registerTransaction;
        //private SIPEndPoint m_dstEndPoint;
        private SIPEndPoint m_localEndPoint;
        private SIPEndPoint m_outboundProxy;
        private bool m_isRegistered;
        private string m_switchboardUsername = "switchboard";
        private string m_switchboardPassword = "password";
        private bool m_authenticatedRequestSent;
        //private SIPServerUserAgent m_incomingCall;
        //private string m_inContentType;
        //private string m_inContent;
        private SIPRequest m_inviteRequest;
        private SIPViaHeader m_switchboardViaHeader;
        private UASInviteTransaction m_uasTransaction;
        private UACInviteTransaction m_uacTransaction;
        private bool m_callInProgress;
        private int m_uacCSeq = 1;

        //private ScriptEngine m_scriptEngine;
        //private SwitchboardDLRFacade m_switchboardFacade;

        public SIPSwitchboard()
        {
            InitializeComponent();
        }

        public SIPSwitchboard(
            ActivityMessageDelegate logActivityMessage,
            string owner)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_owner = owner;
            m_sipTransport = new SIPTransport(ResolveSIPEndPoint, new SIPTransactionEngine());
            m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;
            m_sipTransport.SIPTransportResponseReceived += SIPTransportResponseReceived;
            m_sipChannel = new SilverlightTCPSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            m_sipTransport.AddSIPChannel(m_sipChannel);
            //m_dstEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Parse("10.1.1.5"), 4504));
            m_localEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0));
            m_outboundProxy = new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Parse("10.1.1.5"), 4504));

            //m_scriptEngine = Ruby.CreateEngine();
            //m_switchboardFacade = new SwitchboardDLRFacade(LogActivityMessage_External, LogScriptMessage);
        }

        public void Start()
        {
            if (!m_isRegistered)
            {
                try
                {
                    //NewCall.Begin();
                    m_authenticatedRequestSent = false;
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Sending REGISTER request to 10.1.1.5");
                    SIPRequest registerRequest = new SIPRequest(SIPMethodsEnum.REGISTER, SIPURI.ParseSIPURI("sip:sipsorcery.com"));
                    //SIPContactHeader contactHeader = SIPContactHeader.ParseContactHeader("sip:" + localEndPoint)[0];
                    SIPFromHeader fromHeader = SIPFromHeader.ParseFromHeader("sip:switchboard@sipsorcery.com");
                    SIPToHeader toHeader = SIPToHeader.ParseToHeader("sip:switchboard@sipsorcery.com");
                    SIPContactHeader contactHeader = new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, m_localEndPoint));
                    SIPHeader header = new SIPHeader(contactHeader, fromHeader, toHeader, 1, CallProperties.CreateNewCallId());
                    registerRequest.Header = header;
                    header.CSeqMethod = SIPMethodsEnum.REGISTER;
                    SIPViaHeader viaHeader = new SIPViaHeader(m_localEndPoint, CallProperties.CreateBranchId());
                    header.Vias.PushViaHeader(viaHeader);

                    if (!m_sipChannel.IsConnected)
                    {
                        m_sipChannel.Connect(m_outboundProxy.SocketEndPoint);
                    }

                    m_registerTransaction = m_sipTransport.CreateNonInviteTransaction(registerRequest, m_outboundProxy, m_sipChannel.SIPChannelEndPoint, m_outboundProxy);
                    m_registerTransaction.NonInviteTransactionFinalResponseReceived += RegisterTransactionFinalResponseReceived;
                    m_registerTransaction.SendReliableRequest();
                    LogActivityMessage_External(MessageLevelsEnum.Info, "REGISTER request sent to 10.1.1.5");
                }
                catch (Exception excp)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, excp.Message);
                }
            }
        }

        private void SIPTransportResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            LogScriptMessage(MessageLevelsEnum.Info, "Switchboard received SIP response from " + remoteEndPoint.ToString() + " status " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            //if (sipResponse.Header.Vias.Length > 1) {
            //   sipResponse.Header.Vias.PopTopViaHeader();
            //    LogActivityMessage_External(MessageLevelsEnum.Info, "Forwarding SIP Response.");
            //   m_sipTransport.SendResponse(sipResponse);
            // }
        }

        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            LogScriptMessage(MessageLevelsEnum.Info, "Switchboard received SIP request from " + remoteEndPoint.ToString() + " " + sipRequest.Method + " " + sipRequest.URI.ToString() + ".");

            if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                UIHelper.SetText(m_newCallFrom, "From: " + sipRequest.Header.From.FromURI.CanonicalAddress.ToString());
                UIHelper.SetText(m_newCallTime, "Time: " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss"));
                m_inviteRequest = sipRequest;
                SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                //m_sipTransport.SendResponse(tryingResponse);
                m_uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                //m_uasTransaction.TransactionTraceMessage += UASTransactionTraceMessage;
                m_uasTransaction.TransactionStateChanged += UASTransactionStateChanged;
                m_uasTransaction.SendInformationalResponse(tryingResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.ACK || sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                /*sipRequest.URI = SIPURI.ParseSIPURI("sip:101@sipsorcery.com");
                SIPViaHeader switchboardViaHeader = new SIPViaHeader(m_localEndPoint, CallProperties.CreateBranchId());
                sipRequest.Header.Vias.PushViaHeader(switchboardViaHeader);
                m_sipTransport.SendRequest(m_outboundProxy, sipRequest);*/
                LogScriptMessage(MessageLevelsEnum.Warn, sipRequest.Method.ToString() + " request received that did not belong to an existing transaction.");
            }
        }

        private void UASTransactionStateChanged(SIPTransaction sipTransaction)
        {
            //LogScriptMessage(MessageLevelsEnum.Info, "UAS transaction state now " + sipTransaction.TransactionState + ".");
        }

        private void UASTransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            if (message.IndexOf('\n') == -1)
            {
                LogScriptMessage(MessageLevelsEnum.Info, message);
            }
            else
            {
                LogScriptMessage(MessageLevelsEnum.Info, message.Substring(message.IndexOf('\n')));
            }
        }

        private void RegisterTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, sipResponse.Status + " on REGISTER");

                if (sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised && !m_authenticatedRequestSent)
                {
                    m_authenticatedRequestSent = true;
                    SIPRequest authRequest = GetAuthenticatedRegistrationRequest(sipTransaction.TransactionRequest, sipResponse, m_switchboardUsername, m_switchboardPassword);
                    m_registerTransaction = m_sipTransport.CreateNonInviteTransaction(authRequest, m_outboundProxy, m_sipChannel.SIPChannelEndPoint, m_outboundProxy);
                    m_registerTransaction.NonInviteTransactionFinalResponseReceived += RegisterTransactionFinalResponseReceived;
                    m_registerTransaction.SendReliableRequest();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    m_localEndPoint = new SIPEndPoint(sipResponse.Header.Contact[0].ContactURI);
                    m_sipTransport.RemoveSIPChannel(m_sipChannel);
                    m_sipChannel.SetLocalSIPEndPoint(m_localEndPoint);
                    m_sipTransport.AddSIPChannel(m_sipChannel);
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Local endpoint determined as " + m_localEndPoint.ToString() + ".");
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception RegisterTransactionFinalResponseReceived. " + excp.Message);
            }
        }

        private SIPEndPoint ResolveSIPEndPoint(SIPURI uri, bool synchronous)
        {
            return new SIPEndPoint(SIPProtocolsEnum.tcp, new IPEndPoint(IPAddress.Parse("10.1.1.5"), 4504));
        }

        private SIPRequest GetAuthenticatedRegistrationRequest(SIPRequest registerRequest, SIPResponse sipResponse, string username, string password)
        {

            SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
            authRequest.SetCredentials(username, password, registerRequest.URI.ToString(), SIPMethodsEnum.REGISTER.ToString());

            SIPRequest regRequest = registerRequest.Copy();
            regRequest.LocalSIPEndPoint = registerRequest.LocalSIPEndPoint;
            regRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
            regRequest.Header.From.FromTag = CallProperties.CreateNewTag();
            regRequest.Header.To.ToTag = null;
            regRequest.Header.CSeq = ++registerRequest.Header.CSeq;
            regRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
            regRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;

            return regRequest;
        }

        private void AaronPolycom_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            /*ScriptScope rubyScope = m_scriptEngine.CreateScope();
            rubyScope.SetVariable("sys", m_switchboardFacade);
            m_scriptEngine.Execute("sys.Log(\"hello from IronRuby\")\n", rubyScope);*/
            if (!m_callInProgress)
            {
                LogScriptMessage(MessageLevelsEnum.Info, "Starting UAC call to sip:101@sipsorcery.com.");
                m_callInProgress = true;
                SIPRequest uacRequest = m_inviteRequest.Copy();
                uacRequest.Header.ProxySendFrom = null;
                uacRequest.URI = SIPURI.ParseSIPURI("sip:101@sipsorcery.com");
                m_switchboardViaHeader = new SIPViaHeader(m_localEndPoint, CallProperties.CreateBranchId());
                uacRequest.Header.Vias.PushViaHeader(m_switchboardViaHeader);
                uacRequest.Header.CSeq = m_uacCSeq;
                //m_sipTransport.SendRequest(m_outboundProxy, m_inviteRequest);

                m_uacTransaction = m_sipTransport.CreateUACTransaction(uacRequest, m_outboundProxy, m_localEndPoint, m_outboundProxy);
                m_uacTransaction.TransactionStateChanged += UACTransactionStateChanged;
                m_uacTransaction.UACInviteTransactionInformationResponseReceived += UACInviteTransactionInformationResponseReceived;
                m_uacTransaction.UACInviteTransactionFinalResponseReceived += UACInviteTransactionFinalResponseReceived;
                m_uacTransaction.SendReliableRequest();
            }
            else
            {
                LogScriptMessage(MessageLevelsEnum.Info, "Cancelling UAC call to sip:101@sipsorcery.com.");
                m_callInProgress = false;
                SIPRequest cancelRequest = m_uacTransaction.TransactionRequest.Copy();
                cancelRequest.Method = SIPMethodsEnum.CANCEL;
                cancelRequest.Header.CSeqMethod = SIPMethodsEnum.CANCEL;
                m_uacTransaction.SendRequest(m_outboundProxy, cancelRequest);
                m_uacTransaction.CancelCall();
                m_uacCSeq = m_uacCSeq + 2;
            }
        }

        private void Aaron_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            //SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, m_outboundProxy, m_owner, null, LogMonitorEvent);
            //SIPCallDescriptor callDescriptor = new SIPCallDescriptor(m_switchboardUsername, m_switchboardPassword, "sip:101@sipsorcery.com", "sip:switchboard@sipsorcery.com", "sip:101@sipsorcery.com", null, null, null, SIPCallDirection.Out, m_inContentType, m_inContent, null);
            //uac.Call(callDescriptor);
            //uac.CallAnswered += UACCallAnswered;
            //m_inviteRequest.URI = SIPURI.ParseSIPURI("sip:300@sipsorcery.com");
            //m_switchboardViaHeader = new SIPViaHeader(m_localEndPoint, CallProperties.CreateBranchId());
            //m_inviteRequest.Header.Vias.PushViaHeader(m_switchboardViaHeader);
            //m_sipTransport.SendRequest(m_outboundProxy, m_inviteRequest);

            LogScriptMessage(MessageLevelsEnum.Info, "Starting UAC call to sip:300@sipsorcery.com.");
            m_callInProgress = true;
            SIPRequest uacRequest = m_inviteRequest.Copy();
            uacRequest.Header.ProxySendFrom = null;
            uacRequest.URI = SIPURI.ParseSIPURI("sip:300@sipsorcery.com");
            m_switchboardViaHeader = new SIPViaHeader(m_localEndPoint, CallProperties.CreateBranchId());
            uacRequest.Header.Vias.PushViaHeader(m_switchboardViaHeader);
            uacRequest.Header.CSeq = m_uacCSeq;
            //m_sipTransport.SendRequest(m_outboundProxy, m_inviteRequest);

            m_uacTransaction = m_sipTransport.CreateUACTransaction(uacRequest, m_outboundProxy, m_localEndPoint, m_outboundProxy);
            m_uacTransaction.TransactionStateChanged += UACTransactionStateChanged;
            m_uacTransaction.UACInviteTransactionInformationResponseReceived += UACInviteTransactionInformationResponseReceived;
            m_uacTransaction.UACInviteTransactionFinalResponseReceived += UACInviteTransactionFinalResponseReceived;
            m_uacTransaction.SendReliableRequest();
        }

        private void UACInviteTransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            LogScriptMessage(MessageLevelsEnum.Info, "UAC transaction got info response.");
            SIPResponse infoResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, sipResponse.Status, sipResponse.ReasonPhrase);
            m_uasTransaction.SendInformationalResponse(infoResponse);
        }

        private void UACTransactionStateChanged(SIPTransaction sipTransaction)
        {
            //LogScriptMessage(MessageLevelsEnum.Info, "UAC transaction state now " + sipTransaction.TransactionState + ".");
        }

        private void UACInviteTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            LogScriptMessage(MessageLevelsEnum.Info, "UAC transaction got final response.");
            m_callInProgress = false;

            if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
            {
                m_uasTransaction.SetLocalTag(sipResponse.Header.To.ToTag);
                SIPResponse okResponse = m_uasTransaction.GetOkResponse(m_uasTransaction.TransactionRequest, m_localEndPoint, sipResponse.Header.ContentType, sipResponse.Body);
                m_uasTransaction.SendFinalResponse(okResponse);

                // Because the call ID & tags are the same for the uac and uas legs the ACK on the uas will never get matched. The call is on TCP anyway so it's safe to do without the ACK.
                m_uasTransaction.ACKReceived(m_localEndPoint, m_outboundProxy, null);
            }
        }

        private void LogScriptMessage(MessageLevelsEnum level, string logMessage)
        {
            UIHelper.AppendToActivityLog(m_switchboardLogScrollViewer, m_switchboardLogTextBox, level, logMessage);
        }

        private void LogMonitorEvent(SIPMonitorEvent monitorEvent)
        {
            if (monitorEvent is SIPMonitorConsoleEvent && 
                ((SIPMonitorConsoleEvent)monitorEvent).EventType != SIPMonitorEventTypesEnum.SIPTransaction)
            {
                UIHelper.AppendToActivityLog(m_switchboardLogScrollViewer, m_switchboardLogTextBox, MessageLevelsEnum.Info, monitorEvent.Message);
            }
        }
    }

    /// <summary>
    /// This class provides access to a range of application functions for DLR scripts.
    /// </summary>
    public class SwitchboardDLRFacade
    {
        private ActivityMessageDelegate LogActivityMessage_External;
        private ActivityMessageDelegate LogScriptMessage_External;

        public SwitchboardDLRFacade(ActivityMessageDelegate logActivityMessage, ActivityMessageDelegate logScriptMessage)
        {
            LogActivityMessage_External = logActivityMessage;
            LogScriptMessage_External = logScriptMessage;
        }

        /// <summary>
        /// Prints a message in the application's notification text box.
        /// </summary>
        /// <param name="message">The message to print.</param>
        public void Notify(string message)
        {
            if (!message.IsNullOrBlank())
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, message);
            }
        }

        /// <summary>
        /// Prints a message in the script log message text box.
        /// </summary>
        /// <param name="message">The message to print.</param>
        public void Log(string message)
        {
            if (!message.IsNullOrBlank())
            {
                LogScriptMessage_External(MessageLevelsEnum.Info, message);
            }
        }
    }
}