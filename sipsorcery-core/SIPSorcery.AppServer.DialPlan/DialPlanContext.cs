using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public delegate void CallCancelledDelegate(CallCancelCause cancelCause);
    public delegate void CallProgressDelegate(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string progressContentType, string progressBody);
    public delegate void CallFailedDelegate(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase);
    public delegate void CallAnsweredDelegate(SIPResponseStatusCodesEnum answeredStatus, string reasonPhrase, string answeredContentType, string answeredBody, SIPDialogue answeredDialogue);

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

    public class DialPlanContext {
        private const string TRACE_FROM_ADDRESS = "siptrace@sipsorcery.com";
        private const string TRACE_SUBJECT = "SIP Sorcery Trace";
        
        protected static ILog logger = AppState.GetLogger("dialplan");
        private string CRLF = SIPConstants.CRLF;

        private SIPMonitorLogDelegate Log_External;
        private DialogueBridgeCreatedDelegate CreateBridge_External;

        private SIPTransport m_sipTransport;
        private UASInviteTransaction m_clientTransaction;
        private SIPEndPoint m_outboundProxy;        // If this app forwards calls via an outbound proxy this value will be set.
        private string m_traceDirectory;

        protected List<SIPProvider> m_sipProviders;
        protected StringBuilder m_traceLog = new StringBuilder();
        protected SIPDialPlan m_dialPlan;

        public SIPDialPlan SIPDialPlan
        {
            get { return m_dialPlan; }
        }
        public bool SendTrace;                      // True means the trace should be sent, false it shouldn't.
        public DialPlanContextsEnum ContextType;
        public string Owner {
            get { return m_dialPlan.Owner; }
        }
        public string AdminMemberId {
            get { return m_dialPlan.AdminMemberId; }
        }
        public string TraceEmailAddress
        {
            get { return m_dialPlan.TraceEmailAddress; }
        }
        public List<SIPProvider> SIPProviders {
            get { return m_sipProviders; }
        }
        public string DialPlanScript {
            get { return m_dialPlan.DialPlanScript; }
        }
        public StringBuilder TraceLog
        {
            get { return m_traceLog; }
        }
        public string ClientCallContentType
        {
            get{ return m_clientTransaction.TransactionRequest.Header.ContentType; }
        }
        public string ClientCallContent
        {
            get{ return m_clientTransaction.TransactionRequest.Body; }
        }
        public SIPRequest ClientInviteRequest
        {
            get { return m_clientTransaction.TransactionRequest; }
        }
        public string CallersNetworkId;             // If the caller was a locally administered SIP account this will hold it's network id. Used so calls between two accounts on the same local network can be identified.

        private bool m_isAnswered;
        public bool IsAnswered {
            get { return m_isAnswered; }
        }

        internal event CallCancelledDelegate CallCancelledByClient;

        public DialPlanContext(
            SIPMonitorLogDelegate monitorLogDelegate,
            SIPTransport sipTransport,
            DialogueBridgeCreatedDelegate createBridge,
            SIPEndPoint outboundProxy,
            UASInviteTransaction clientTransaction,
            SIPDialPlan dialPlan,
            List<SIPProvider> sipProviders,
            string traceDirectory,
            string callersNetworkId) {

            Log_External = monitorLogDelegate;
            CreateBridge_External = createBridge;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_clientTransaction = clientTransaction;
            m_dialPlan = dialPlan;
            m_sipProviders = sipProviders;
            m_traceDirectory = traceDirectory;
            CallersNetworkId = callersNetworkId;

            if (m_clientTransaction != null) {
                clientTransaction.TransactionTraceMessage += TransactionTraceMessage;
                TraceLog.AppendLine(SIPMonitorEventTypesEnum.SIPTransaction + "=>" + "Request received " + m_clientTransaction.LocalSIPEndPoint +
                    "<-" + m_clientTransaction.RemoteEndPoint + CRLF + m_clientTransaction.TransactionRequest.ToString());

                m_clientTransaction.CDR.Owner = m_dialPlan.Owner;
                m_clientTransaction.UASInviteTransactionTimedOut += ClientTransactionTimedOut;
                m_clientTransaction.UASInviteTransactionCancelled += ClientCallCancelled;
                m_clientTransaction.TransactionRemoved += ClientTransactionRemoved;
            }
        }

        public void CallProgress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string progressContentType, string progressBody) {
            try {
                if (!m_isAnswered) {
                    if ((int)progressStatus >= 200) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "CallProgress was passed an invalid response status of " + progressStatus + ", ignoring.", Owner));
                    }
                    else {
                        if (m_clientTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding) {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "CallProgress ignoring progress response with status of " + progressStatus + " as already in " + m_clientTransaction.TransactionState + ".", Owner));
                        }
                        else {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call progressing with " + progressStatus + ".", Owner));
                            SIPResponse progressResponse = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest, progressStatus, reasonPhrase);

                            if (!progressBody.IsNullOrBlank()) {
                                progressResponse.Body = progressBody;
                                progressResponse.Header.ContentType = progressContentType;
                                progressResponse.Header.ContentLength = progressBody.Length;
                            }

                            m_clientTransaction.SendInformationalResponse(progressResponse);
                        }
                    }
                }
                else {
                    logger.Warn("DialPlanContext CallProgress fired on already answered call.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanContext CallProgress. " + excp.Message);
            }
        }

        public void CallFailed(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase) {
            try {
                if (!m_isAnswered) {
                    if ((int)failureStatus < 400) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "CalFailure was passed an invalid response status of " + failureStatus + ", ignoring.", Owner));
                    }
                    else {
                        m_isAnswered = true;
                        string failureReason = (!reasonPhrase.IsNullOrBlank()) ? " and " + reasonPhrase : null;
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call failed with " + failureStatus + failureReason + ".", Owner));
                        SIPResponse failureResponse = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest, failureStatus, reasonPhrase);
                        m_clientTransaction.SendFinalResponse(failureResponse);
                    }
                }
                else {
                    logger.Warn("DialPlanContext CallFailed fired on already answered call.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanContext CallFailed. " + excp.Message);
            }
        }

        public void CallAnswered(SIPResponseStatusCodesEnum answeredStatus, string reasonPhrase, string answeredContentType, string answeredBody, SIPDialogue answeredDialogue) {
            try {
                if (!m_isAnswered) {
                    if ((int)answeredStatus < 200 || (int)answeredStatus > 299) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "CallAnswered was passed an invalid response status of " + answeredStatus + ", ignoring.", Owner));
                    }
                    else {
                        m_isAnswered = true;
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Answering client call with " + answeredStatus + ".", Owner));

                        // Send the OK through to the client.
                        //if (m_manglePrivateAddresses)
                        //{
                        //    SIPPacketMangler.MangleSIPResponse(SIPMonitorServerTypesEnum.StatefulProxy, answeredResponse, answeredUAC.ServerTransaction.RemoteEndPoint, m_username, m_statefulProxyLogEvent);
                        //}

                        SIPResponse okResponse = m_clientTransaction.GetOkResponse(m_clientTransaction.TransactionRequest, m_clientTransaction.LocalSIPEndPoint, answeredContentType, answeredBody);
                        m_clientTransaction.SendFinalResponse(okResponse);

                        // NOTE the Record-Route header does not get reversed for this Route set!! Since the Route set is being used from the server end NOT
                        // the client end a reversal will generate a Route set in the wrong order.
                        SIPDialogue clientLegDialogue = new SIPDialogue(
                            m_sipTransport,
                            m_clientTransaction,
                            m_dialPlan.Owner,
                            m_dialPlan.AdminMemberId);

                        // Record the now established call with the call manager for in dialogue management and hangups.

                        CreateBridge_External(clientLegDialogue, answeredDialogue, m_dialPlan.Owner);
                    }
                }
                else {
                    logger.Warn("DialPlanContext CallAnswered fired on already answered call.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanContext CallAnswered. " + excp.Message);
            }
        }

        /// <summary>
        /// The client transaction will time out after ringing for the maximum allowed time for an INVITE transaction (probably 10 minutes) or less
        /// if the invite transaction timeout value has been adjusted.
        /// </summary>
        /// <param name="sipTransaction"></param>
        private void ClientTransactionTimedOut(SIPTransaction sipTransaction) {
            if (!m_isAnswered)
            {
                m_isAnswered = true;
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call timed out in a " + sipTransaction.TransactionState + " state after " + DateTime.Now.Subtract(sipTransaction.Created).TotalSeconds.ToString("0.##") + "s.", Owner));
                // Let the client know the call failed.
                CallFailed(SIPResponseStatusCodesEnum.ServerTimeout, "The dial plan did not generate ringing");
                if (CallCancelledByClient != null)
                {
                    CallCancelledByClient(CallCancelCause.TimedOut);
                }
            }
            else
            {
                logger.Warn("DialPlanContext ClientTransactionTimedOut fired on already answered call.");
            }
        }

        private void ClientCallCancelled(SIPTransaction clientTransaction) {
            if (!m_isAnswered)
            {
                m_isAnswered = true;
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Client call cancelled halting dial plan.", Owner));
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

        private void ClientTransactionRemoved(SIPTransaction sipTransaction) {
            try {
                if (!m_traceDirectory.IsNullOrBlank() && !TraceEmailAddress.IsNullOrBlank() && TraceLog != null && TraceLog.Length > 0) {
                    if (!Directory.Exists(m_traceDirectory)) {
                        logger.Warn("Dial Plan trace could not be saved as trace directory " + m_traceDirectory + " does not exist.");
                    }
                    else {
                        ThreadPool.QueueUserWorkItem(CompleteTrace);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanContext ClientTransactionRemoved. " + excp.Message);
            }
        }

        private void CompleteTrace(object state) {
            try {
                SIPMonitorEvent traceCompleteEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dialplan trace completed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".", Owner);
                TraceLog.AppendLine(traceCompleteEvent.EventType + "=> " + traceCompleteEvent.Message);

                string traceFilename = m_traceDirectory + Owner + "-" + DateTime.Now.ToString("ddMMMyyyyHHmmss") + ".txt";
                StreamWriter traceSW = new StreamWriter(traceFilename);
                traceSW.Write(TraceLog.ToString());
                traceSW.Close();

                if (TraceEmailAddress != null) {
                    logger.Debug("Emailing trace to " + TraceEmailAddress + ".");
                    Email.SendEmail(TraceEmailAddress, TRACE_FROM_ADDRESS, TRACE_SUBJECT, TraceLog.ToString());
                }
            }
            catch (Exception traceExcp) {
                logger.Error("Exception DialPlanContext CompleteTrace. " + traceExcp.Message);
            }
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message) {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.SIPTransaction, message, Owner));
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent) {
            try {
                if (TraceLog != null) {
                    TraceLog.AppendLine(monitorEvent.EventType + "=> " + monitorEvent.Message);
                }

                if (Log_External != null) {
                    Log_External(monitorEvent);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception FireProxyLogEvent DialPlanContext. " + excp.Message);
            }
        }
    }
}
