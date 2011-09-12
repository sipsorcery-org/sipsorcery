using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public delegate void CallCancelledDelegate(CallCancelCause cancelCause);
    public delegate void CallProgressDelegate(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody, ISIPClientUserAgent uac);
    public delegate void CallFailedDelegate(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders);
    public delegate void CallAnsweredDelegate(SIPResponseStatusCodesEnum answeredStatus, string reasonPhrase, string toTag, string[] customHeaders, string answeredContentType, string answeredBody, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum uasTransferMode);

    public enum DialPlanAppResult
    {
        Unknown = 0,
        Answered = 1,           // The application answered the call.
        NoAnswer = 2,           // The application had at least one call provide a ringing response.
        Failed = 3,             // The application failed to get any calls to the progressing stage.
        ClientCancelled = 4,    // Call cancelled by client user agent.
        AdminCancelled = 5,     // Call cancelled by a an external administrative action or dial plan rule.
        TimedOut = 6,           // No response from any forward within the time limit.
        Error = 7,
        AlreadyAnswered = 8,    // Was answered prior to the Dial command.
        Redirect = 9            // Same as Failed but at least one of the falure responses was a redirect (3xx) response with a valid Contact URI.
    }

    public enum DialPlanContextsEnum
    {
        None = 0,
        Line = 1,
        Script = 2,
    }

    public enum CallCancelCause
    {
        Unknown = 0,
        TimedOut = 1,           // The call was automatically cancelled by the Dial application after a timeout.
        Administrative = 2,     // Call was cancelled by an administrative action such as clicking cancel on the Call Manager UI.
        ClientCancelled = 3,
        NormalClearing = 4,
        Error = 5,
    }

    public class DialPlanContext
    {
        private const string TRACE_FROM_ADDRESS = "admin@sipsorcery.com";
        private const string TRACE_SUBJECT = "SIP Sorcery Trace";

        protected static ILog logger = AppState.GetLogger("dialplan");

        private SIPMonitorLogDelegate Log_External;
        public DialogueBridgeCreatedDelegate CreateBridge_External;

        private SIPTransport m_sipTransport;
        private ISIPServerUserAgent m_sipServerUserAgent;
        private SIPEndPoint m_outboundProxy;        // If this app forwards calls via an outbound proxy this value will be set.
        private string m_traceDirectory;
        public DialPlanEngine m_dialPlanEngine;       // Gets used to allow an authorised redirect response to initiate a new dial plan execution.

        protected List<SIPProvider> m_sipProviders;
        protected StringBuilder m_traceLog = new StringBuilder();
        protected SIPDialPlan m_dialPlan;

        public SIPDialPlan SIPDialPlan
        {
            get { return m_dialPlan; }
        }
        public bool SendTrace = true;                      // True means the trace should be sent, false it shouldn't.
        public DialPlanContextsEnum ContextType;
        public string Owner
        {
            get { return m_dialPlan.Owner; }
        }
        public string AdminMemberId
        {
            get { return m_dialPlan.AdminMemberId; }
        }
        public string TraceEmailAddress
        {
            get { return m_dialPlan.TraceEmailAddress; }
        }
        public List<SIPProvider> SIPProviders
        {
            get { return m_sipProviders; }
        }
        public string DialPlanScript
        {
            get { return m_dialPlan.DialPlanScript; }
        }
        public StringBuilder TraceLog
        {
            get { return m_traceLog; }
        }
        private bool m_hasBeenRedirected;                   // Gets set to true if a second dial plan execution has been executed within this call (only one redirect dialplan instance is allowed).

        public string CallersNetworkId;             // If the caller was a locally administered SIP account this will hold it's network id. Used so calls between two accounts on the same local network can be identified.
        public Customer Customer;                   // The customer that owns this dialplan.

        private bool m_isAnswered;
        public bool IsAnswered
        {
            get { return m_isAnswered; }
        }

        public bool IsDialPlanComplete
        {
            get { return m_dialPlanComplete; }
        }

        public SIPAccount SIPAccount
        {
            get { return m_sipServerUserAgent.SIPAccount; }
        }

        private SIPResponse m_redirectResponse;
        public SIPResponse RedirectResponse
        {
            get { return m_redirectResponse; }
        }

        private SIPURI m_redirectURI;
        public SIPURI RedirectURI
        {
            get { return m_redirectURI; }
        }

        public CRMHeaders CallerCRMDetails = null;  // Can be populated asynchronously by looking up the caller's details in a CRM system.

        private List<ISIPClientUserAgent> m_uacWaitingForCallDetails = new List<ISIPClientUserAgent>();     // UACs can indicate they would like the call details when available.
        private List<ISIPClientUserAgent> m_uacCallDetailsSent = new List<ISIPClientUserAgent>();           // List of UAC's that the caller details have already been sent to.

        internal event CallCancelledDelegate CallCancelledByClient;

        private bool m_dialPlanComplete;
        //public event Action DialPlanComplete;

        public DialPlanContext(
            SIPMonitorLogDelegate monitorLogDelegate,
            SIPTransport sipTransport,
            DialogueBridgeCreatedDelegate createBridge,
            SIPEndPoint outboundProxy,
            ISIPServerUserAgent sipServerUserAgent,
            SIPDialPlan dialPlan,
            List<SIPProvider> sipProviders,
            string traceDirectory,
            string callersNetworkId,
            Customer customer,
            DialPlanEngine dialPlanEngine)
        {
            Log_External = monitorLogDelegate;
            CreateBridge_External = createBridge;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipServerUserAgent = sipServerUserAgent;
            m_dialPlan = dialPlan;
            m_sipProviders = sipProviders;
            m_traceDirectory = traceDirectory;
            CallersNetworkId = callersNetworkId;
            Customer = customer;
            m_dialPlanEngine = dialPlanEngine;

            m_sipServerUserAgent.CallCancelled += ClientCallCancelled;
            m_sipServerUserAgent.NoRingTimeout += ClientCallNoRingTimeout;
            m_sipServerUserAgent.TransactionComplete += ClientTransactionRemoved;
            m_sipServerUserAgent.SetTraceDelegate(TransactionTraceMessage);
        }

        /// <summary>
        /// Constructor for non-INVITE requests that can initiate dialplan executions.
        /// </summary>
        public DialPlanContext(
           SIPMonitorLogDelegate monitorLogDelegate,
           SIPTransport sipTransport,
           SIPEndPoint outboundProxy,
           SIPDialPlan dialPlan,
           List<SIPProvider> sipProviders,
           string traceDirectory,
           string callersNetworkId,
           Customer customer)
        {
            Log_External = monitorLogDelegate;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_dialPlan = dialPlan;
            m_sipProviders = sipProviders;
            m_traceDirectory = traceDirectory;
            CallersNetworkId = callersNetworkId;
            Customer = customer;
        }

        public void CallProgress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody, ISIPClientUserAgent uac)
        {
            if (m_sipServerUserAgent != null && !m_isAnswered)
            {
                m_sipServerUserAgent.Progress(progressStatus, reasonPhrase, customHeaders, progressContentType, progressBody);

                if (uac != null && uac.CallDescriptor.RequestCallerDetails && CallerCRMDetails != null)
                {
                    if (CallerCRMDetails.Pending && !m_uacWaitingForCallDetails.Contains(uac) && !m_uacCallDetailsSent.Contains(uac))
                    {
                        m_uacWaitingForCallDetails.Add(uac);
                    }
                    else if (!CallerCRMDetails.Pending && !m_uacCallDetailsSent.Contains(uac))
                    {
                        // Send the call details to the client user agent.
                        uac.Update(CallerCRMDetails);
                    }
                }
            }
        }

        public void CallFailed(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            try
            {
                if (m_sipServerUserAgent != null && !m_isAnswered)
                {
                    m_isAnswered = true;
                    if ((int)failureStatus >= 300 && (int)failureStatus <= 399)
                    {
                        SIPURI redirectURI = SIPURI.ParseSIPURIRelaxed(customHeaders[0]);
                        m_sipServerUserAgent.Redirect(failureStatus, redirectURI);
                    }
                    else
                    {
                        m_sipServerUserAgent.Reject(failureStatus, reasonPhrase, customHeaders);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanContext CallFailed. " + excp.Message);
            }
            finally
            {
                DialPlanExecutionFinished();
            }
        }

        public void CallAnswered(SIPResponseStatusCodesEnum answeredStatus, string reasonPhrase, string toTag, string[] customHeaders, string answeredContentType, string answeredBody, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum uasTransferMode)
        {
            try
            {
                if (m_sipServerUserAgent != null && !m_isAnswered)
                {
                    if (!m_sipServerUserAgent.IsInvite)
                    {
                        m_isAnswered = true;
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Answering client call with a response status of " + (int)answeredStatus + ".", Owner));
                        m_sipServerUserAgent.AnswerNonInvite(answeredStatus, reasonPhrase, customHeaders, answeredContentType, answeredBody);
                    }
                    else
                    {
                        m_isAnswered = true;
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Answering client call with a response status of " + (int)answeredStatus + ".", Owner));

                        SIPDialogue uasDialogue = m_sipServerUserAgent.Answer(answeredContentType, answeredBody, toTag, answeredDialogue, uasTransferMode);

                        if (!m_sipServerUserAgent.IsB2B && answeredDialogue != null)
                        {
                            if (uasDialogue != null)
                            {
                                // Duplicate switchboard dialogue settings.
                                uasDialogue.SwitchboardDescription = answeredDialogue.SwitchboardDescription;
                                uasDialogue.SwitchboardCallerDescription = answeredDialogue.SwitchboardCallerDescription;
                                uasDialogue.SwitchboardOwner = answeredDialogue.SwitchboardOwner;

                                // Record the now established call with the call manager for in dialogue management and hangups.
                                CreateBridge_External(uasDialogue, answeredDialogue, m_dialPlan.Owner);
                            }
                            else
                            {
                                logger.Warn("Failed to get a SIPDialogue from UAS.Answer.");
                            }
                        }
                    }
                }
                else
                {
                    logger.Warn("DialPlanContext CallAnswered fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanContext CallAnswered. " + excp.Message);
            }
            finally
            {
                DialPlanExecutionFinished();
            }
        }

        public void DialPlanExecutionFinished()
        {
            if (!m_dialPlanComplete)
            {
                m_dialPlanComplete = true;

                //if (DialPlanComplete != null)
                //{
                //    DialPlanComplete();
                //}
            }
        }

        public void SetCallerDetails(CRMHeaders crmHeaders)
        {
            try
            {
                CallerCRMDetails = crmHeaders;

                if (!CallerCRMDetails.Pending)
                {
                    lock (m_uacWaitingForCallDetails)
                    {
                        if (m_uacWaitingForCallDetails.Count > 0)
                        {
                            foreach (var waitingUAC in m_uacWaitingForCallDetails)
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Sending CRM caller details to " + waitingUAC.CallDescriptor.Uri + ".", Owner));
                                waitingUAC.Update(crmHeaders);
                                m_uacCallDetailsSent.Add(waitingUAC);
                            }

                            m_uacWaitingForCallDetails.Clear();
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetCallerDetails. " + excp.Message);
            }
        }

        /// <summary>
        /// The client transaction will time out after ringing for the maximum allowed time for an INVITE transaction (probably 10 minutes) or less
        /// if the invite transaction timeout value has been adjusted.
        /// </summary>
        /// <param name="sipTransaction"></param>
        private void ClientCallNoRingTimeout(ISIPServerUserAgent sipServerUserAgent)
        {
            try
            {
                m_isAnswered = true;
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call timed out, no ringing response was receved within the allowed time.", Owner));
                if (CallCancelledByClient != null)
                {
                    CallCancelledByClient(CallCancelCause.TimedOut);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ClientCallNoRingTimeout. " + excp.Message);
            }
            finally
            {
                DialPlanExecutionFinished();
            }
        }

        private void ClientCallCancelled(ISIPServerUserAgent uas)
        {
            try
            {
                if (!m_isAnswered)
                {
                    m_isAnswered = true;
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call cancelled halting dial plan.", Owner));
                    if (CallCancelledByClient != null)
                    {
                        CallCancelledByClient(CallCancelCause.ClientCancelled);
                    }
                }
                else
                {
                    logger.Warn("DialPlanContext ClientCallCancelled fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanContext ClientCallCancelled. " + excp.Message);
            }
            finally
            {
                DialPlanExecutionFinished();
            }
        }

        private void ClientTransactionRemoved(ISIPServerUserAgent uas)
        {
            try
            {
                if (!TraceEmailAddress.IsNullOrBlank() && TraceLog != null && TraceLog.Length > 0 && SendTrace)
                {
                    ThreadPool.QueueUserWorkItem(delegate { CompleteTrace(); });
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanContext ClientTransactionRemoved. " + excp.Message);
            }
        }

        private void CompleteTrace()
        {
            try
            {
                SIPMonitorConsoleEvent traceCompleteEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dialplan trace completed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".", Owner);
                TraceLog.AppendLine(traceCompleteEvent.EventType + "=> " + traceCompleteEvent.Message);

                if (!m_traceDirectory.IsNullOrBlank() && Directory.Exists(m_traceDirectory))
                {
                    string traceFilename = m_traceDirectory + Owner + "-" + DateTime.Now.ToString("ddMMMyyyyHHmmss") + ".txt";
                    StreamWriter traceSW = new StreamWriter(traceFilename);
                    traceSW.Write(TraceLog.ToString());
                    traceSW.Close();
                }

                if (TraceEmailAddress != null)
                {
                    logger.Debug("Emailing trace to " + TraceEmailAddress + ".");
                    SIPSorcerySMTP.SendEmail(TraceEmailAddress, TRACE_FROM_ADDRESS, TRACE_SUBJECT, TraceLog.ToString());
                }
            }
            catch (Exception traceExcp)
            {
                logger.Error("Exception DialPlanContext CompleteTrace. " + traceExcp.Message);
            }
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.SIPTransaction, message, Owner));
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (monitorEvent is SIPMonitorConsoleEvent)
                {
                    SIPMonitorConsoleEvent consoleEvent = monitorEvent as SIPMonitorConsoleEvent;

                    if (TraceLog != null)
                    {
                        TraceLog.AppendLine(consoleEvent.EventType + "=> " + monitorEvent.Message);
                    }
                }

                if (Log_External != null)
                {
                    Log_External(monitorEvent);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireProxyLogEvent DialPlanContext. " + excp.Message);
            }
        }

        /// <summary>
        /// Executes a new instance of the current dialplan as a result of receiving a redirect response.
        /// </summary>
        public void ExecuteDialPlanForRedirect(SIPResponse redirectResponse)
        {
            SIPURI redirectURI = redirectResponse.Header.Contact[0].ContactURI;

            if (m_hasBeenRedirected)
            {
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " rejcted, only a single redirect dialplan execution allowed.", Owner));
            }
            else
            {
                m_hasBeenRedirected = true;
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " accepted, new dialplan execution commencing.", Owner));

                m_redirectResponse = redirectResponse;
                m_redirectURI = redirectURI;

                m_dialPlanEngine.Execute(this, m_sipServerUserAgent, SIPCallDirection.Redirect, CreateBridge_External, null);
            }
        }
    }
}
