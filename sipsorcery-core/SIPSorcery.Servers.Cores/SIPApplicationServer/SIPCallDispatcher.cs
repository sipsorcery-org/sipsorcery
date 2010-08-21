using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;
using Microsoft.Scripting.Hosting;

namespace SIPSorcery.Servers {

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Example call dispatcher workers node:
    /// 
    ///  <sipdispatcherworkers>
    ///   <sipdispatcherworker>
    ///     <workerprocesspath>C:\Temp\sipsorcery-appsvr1\sipsorcery-appsvr.exe</workerprocesspath>
    ///     <workerprocessargs>-sip:{0} -cms:{1}</workerprocessargs>
    ///     <sipsocket>127.0.0.1:5070</sipsocket>
    ///     <callmanageraddress>http://localhost:8081/callmanager</callmanageraddress>
    ///    </sipdispatcherworker>
    ///   </sipdispatcherworkers>
    ///   
    /// </remarks>
    public class SIPCallDispatcher {

        private class SIPCallDispatcherWorker {

            private const int START_ATTEMPT_INTERVAL = 30;

            public string WorkerProcessPath;
            public string WorkerProcessArgs;
            public SIPEndPoint AppServerEndpoint;
            public EndpointAddress CallManagerAddress;
            public Process WorkerProcess;
            public DateTime? LastStartAttempt;
            public DateTime? RestartTime;
            public bool HasBeenKilled;

            public SIPCallDispatcherWorker(XmlNode xmlConfigNode) {
                WorkerProcessPath = xmlConfigNode.SelectSingleNode("workerprocesspath").InnerText;
                WorkerProcessArgs = xmlConfigNode.SelectSingleNode("workerprocessargs").InnerText;
                AppServerEndpoint = SIPEndPoint.ParseSIPEndPoint(xmlConfigNode.SelectSingleNode("sipsocket").InnerText);
                CallManagerAddress = new EndpointAddress(xmlConfigNode.SelectSingleNode("callmanageraddress").InnerText);
            }

            public void StartProcess() {
                if (LastStartAttempt == null || DateTime.Now.Subtract(LastStartAttempt.Value).TotalSeconds > START_ATTEMPT_INTERVAL) {
                    LastStartAttempt = DateTime.Now;
                    RestartTime = null;
                    HasBeenKilled = false;
                    ProcessStartInfo startInfo = new ProcessStartInfo(WorkerProcessPath, String.Format(WorkerProcessArgs, new object[] { AppServerEndpoint.ToString(), CallManagerAddress.ToString(), "false" }));
                    startInfo.CreateNoWindow = true;
                    startInfo.UseShellExecute = false;
                    WorkerProcess = Process.Start(startInfo);
                    dispatcherLogger.Debug("New call dispatcher worker process on " + AppServerEndpoint.ToString() + " started on pid=" + WorkerProcess.Id + ".");
                }
                else {
                    dispatcherLogger.Debug("Interval of starts for call dispatcher worker process was too short, last restart " + DateTime.Now.Subtract(LastStartAttempt.Value).TotalSeconds.ToString("0.00") + "s ago.");
                }
            }

            public void Kill() {
                try {
                    dispatcherLogger.Debug("Restarting worker process on pid=" + WorkerProcess.Id + ".");
                    WorkerProcess.Kill();
                    HasBeenKilled = true;
                }
                catch (Exception excp) {
                    dispatcherLogger.Error("Exception SIPCallDispatcherWorker Kill. " + excp.Message);
                }
            }

            public bool IsHealthy() {
                try {
                    if (WorkerProcess != null && !WorkerProcess.HasExited && RestartTime == null) {
                        return true;
                    }
                    else {
                        return false;
                    }
                }
                catch (Exception excp) {
                    dispatcherLogger.Error("Exception SIPCallDispatcherWorker IsHealthy. " + excp.Message);
                    return false;
                }
            }
        }

        private const string WORKER_PROCESS_MONITOR_THREAD_NAME = "sipcalldispatcher-workermonitor";
        private const string WORKER_PROCESS_PROBE_THREAD_NAME = "sipcalldispatcher-probe";

        private const int MAX_LIFETIME_SECONDS = 180;
        private const long MAX_PHYSICAL_MEMORY = 150000000; // Restart worker processes when they've used up 150MB of physical memory.
        private const int PROCESS_RESTART_DELAY = 33;
        private const int CHECK_WORKER_MEMORY_PERIOD = 1000;
        private const int PROBE_WORKER_CALL_PERIOD = 15000;

        private static ILog logger = AppState.logger;
        private static ILog dispatcherLogger = AppState.GetLogger("sipcalldispatcher");

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;
        private SIPTransport m_sipTransport;
        private XmlNode m_callDispatcherNode;
        private SIPEndPoint m_outboundProxy;
        private CompiledCode m_compiledScript;
        private string m_dispatcherScriptPath;
        private ScriptLoader m_scriptLoader;
        private ServiceHost m_callManagerPassThruSvcHost;
        private bool m_exit;
        private string m_dispatcherUsername = SIPCallManager.DISPATCHER_SIPACCOUNT_NAME;

        private List<SIPCallDispatcherWorker> m_callDispatcherWorkers = new List<SIPCallDispatcherWorker>();
        private Dictionary<string, string> m_callIdEndPoints = new Dictionary<string, string>();    // [callid, dispatched endpoint].
        private Dictionary<string, DateTime> m_callIdAddedAt = new Dictionary<string, DateTime>();  // [callid, time added].
        private List<string> m_workerSIPEndPoints = new List<string>();                             // Allow quick lookups to determine whether a remote end point is that of a worker process.

        public SIPCallDispatcher(
            SIPMonitorLogDelegate logDelegate, 
            SIPTransport sipTransport, 
            XmlNode callDispatcherNode,
            SIPEndPoint outboundProxy,
            string dispatcherScriptPath) {

            if (callDispatcherNode == null || callDispatcherNode.ChildNodes.Count == 0) {
                throw new ArgumentNullException("A SIPCallDispatcher cannot be created with an empty configuration node.");
            }

            SIPMonitorLogEvent_External = logDelegate;
            m_sipTransport = sipTransport;
            m_callDispatcherNode = callDispatcherNode;
            m_outboundProxy = outboundProxy;
            m_dispatcherScriptPath = dispatcherScriptPath;

            m_scriptLoader = new ScriptLoader(SIPMonitorLogEvent_External, m_dispatcherScriptPath);
            m_scriptLoader.ScriptFileChanged += (s, e) => { m_compiledScript = m_scriptLoader.GetCompiledScript(); };
            m_compiledScript = m_scriptLoader.GetCompiledScript();

            try {
                CallManagerPassThruServiceInstanceProvider callManagerPassThruSvcInstanceProvider = new CallManagerPassThruServiceInstanceProvider(this);
                m_callManagerPassThruSvcHost = new ServiceHost(typeof(CallManagerPassThruService));
                m_callManagerPassThruSvcHost.Description.Behaviors.Add(callManagerPassThruSvcInstanceProvider);
                m_callManagerPassThruSvcHost.Open();

                logger.Debug("SIPCallDispatcher CallManagerPassThru hosted service successfully started on " + m_callManagerPassThruSvcHost.BaseAddresses[0].AbsoluteUri + ".");
            }
            catch (Exception excp) {
                logger.Warn("Exception starting SIPCallDispatcher CallManagerPassThru hosted service. " + excp.Message);
            }

            foreach (XmlNode callDispatcherWorkerNode in callDispatcherNode.ChildNodes) {
                SIPCallDispatcherWorker callDispatcherWorker = new SIPCallDispatcherWorker(callDispatcherWorkerNode);
                m_callDispatcherWorkers.Add(callDispatcherWorker);
                m_workerSIPEndPoints.Add(callDispatcherWorker.AppServerEndpoint.ToString());
                dispatcherLogger.Debug(" SIPCallDispatcher worker added for " + callDispatcherWorker.AppServerEndpoint.ToString() + " and " + callDispatcherWorker.CallManagerAddress.ToString() + ".");
            }

            ThreadPool.QueueUserWorkItem(delegate { SpawnWorkers(); });
            ThreadPool.QueueUserWorkItem(delegate { ProbeWorkers(); });
        }

        private void SpawnWorkers() {
            try {
                Thread.CurrentThread.Name = WORKER_PROCESS_MONITOR_THREAD_NAME;

                foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                    worker.StartProcess();
               }
                
                while (!m_exit) {
                    try {
                        lock (m_callDispatcherWorkers) {
                            foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                                if (worker.RestartTime != null) {
                                    if (worker.RestartTime < DateTime.Now) {
                                        dispatcherLogger.Debug("Restarting worker process on pid=" + worker.WorkerProcess.Id + ".");
                                        if (!worker.HasBeenKilled) {
                                            worker.Kill();
                                        }
                                        worker.StartProcess();
                                    }
                                }
                                else if (!worker.IsHealthy()) {
                                    worker.StartProcess();
                                }
                                else {
                                    worker.WorkerProcess.Refresh();
                                    if (worker.WorkerProcess.PrivateMemorySize64 >= MAX_PHYSICAL_MEMORY) {
                                        dispatcherLogger.Debug("Worker process on pid=" + worker.WorkerProcess.Id + " has reached the memory limit, scheduling a restart.");
                                        worker.RestartTime = DateTime.Now.AddSeconds(PROCESS_RESTART_DELAY);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception checkWorkersExcp) {
                        dispatcherLogger.Error("Exception SIPCallDispatcher Checkin Workers. " + checkWorkersExcp.Message);
                    }

                    Thread.Sleep(CHECK_WORKER_MEMORY_PERIOD);
                }
            }
            catch (Exception excp) {
                dispatcherLogger.Error("Exception SIPCallDispatcher SpawnWorkers. " + excp.Message);
            }
        }

        private void ProbeWorkers() {
            try {

                while (!m_exit) {

                    try {
                        SIPEndPoint activeWorkerEndPoint = GetFirstHealthyEndPoint();
                        SIPCallDescriptor callDescriptor = new SIPCallDescriptor(m_dispatcherUsername, null, "sip:" + m_dispatcherUsername + "@" + activeWorkerEndPoint.SocketEndPoint.ToString(),
                                "sip:" + m_dispatcherUsername + "@sipcalldispatcher", "sip:" + activeWorkerEndPoint.SocketEndPoint.ToString(), null, null, null, SIPCallDirection.Out, null, null, null);
                        SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, null, null, null, null);
                        uac.CallAnswered += DispatcherCallAnswered;
                        uac.CallFailed += new SIPCallFailedDelegate(DispatcherCallFailed);
                        uac.Call(callDescriptor);
                    }
                    catch (Exception probeExcp) {
                        dispatcherLogger.Error("Exception SIPCallDispatcher Sending Probe. " + probeExcp.Message);
                    }

                    Thread.Sleep(PROBE_WORKER_CALL_PERIOD);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCallDispatcher ProberWorkers. " + excp.Message);
            }
        }

        private void DispatcherCallFailed(ISIPClientUserAgent uac, string errorMessage) {
            try {
                string workerSocket = SIPURI.ParseSIPURI(uac.CallDescriptor.Uri).Host;
                dispatcherLogger.Debug("Dispatcher call to " + workerSocket + " failed " + errorMessage + ".");

                // Find the worker for the failed end point.
                SIPCallDispatcherWorker failedWorker = null;
                lock (m_callDispatcherWorkers) {
                    foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                        if (worker.AppServerEndpoint.SocketEndPoint.ToString() == workerSocket) {
                            failedWorker = worker;
                            break;
                        }
                    }
                }

                if (failedWorker != null) {
                    dispatcherLogger.Debug("Scheduling immediate restart on worker process pid=" + failedWorker.WorkerProcess.Id + " due to failed probe.");
                    failedWorker.RestartTime = DateTime.Now;
                }
            }
            catch (Exception excp) {
                dispatcherLogger.Error("Exception DispatcherCallFailed. " + excp.Message);
            }
        }

        private void DispatcherCallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            //logger.Debug("Dispatcher call answered, execution count = " + sipResponse.Header.UnknownHeaders[0] + ".");
        }

        public void Stop() {
            m_exit = true;
        }

        public CallManagerServiceClient GetCallManagerClient() {

            lock (m_callDispatcherWorkers) {
                foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                    if (worker.IsHealthy()) {
                        return new CallManagerServiceClient(new BasicHttpBinding(), worker.CallManagerAddress);
                    }
                }
            }

            logger.Warn("GetCallManagerClient could not find a healthy SIPCallDispatcherWorker.");

            return null;
        }

        public void Dispatch(string remoteEndPoint, SIPRequest sipRequest) {
            try {
                //logger.Debug("Dispatch SIPRequest from " + remoteEndPoint + " " + sipRequest.Method + " callid=" + sipRequest.Header.CallId + ".");
                SIPEndPoint dispatchEndPoint = m_outboundProxy;

                if (remoteEndPoint == m_outboundProxy.ToString()) {
                    if (m_callIdEndPoints.ContainsKey(sipRequest.Header.CallId)) {
                        // Request from proxy that matches an existing dispatched callid.
                        dispatchEndPoint = SIPEndPoint.ParseSIPEndPoint(m_callIdEndPoints[sipRequest.Header.CallId]);
                        m_sipTransport.SendRequest(dispatchEndPoint, sipRequest);
                    }
                    else {
                        // A new request from proxy that needs to have a decision made about which app server to dispatch to.

                        // A new request has arrived from the proxy.
                        /*if (m_appServerIndex > m_appServerEndPoints.Count - 1) {
                            m_appServerIndex = 0;
                        }
                        dispatchEndPoint = m_appServerEndPoints[m_appServerIndex++];
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dispatching new " + sipRequest.Method + " request to " + dispatchEndPoint.ToString() + ", callid=" + sipRequest.Header.CallId + ".", null));
                        m_callIdEndPoints.Add(sipRequest.Header.CallId, dispatchEndPoint.ToString());
                        m_callIdAddedAt.Add(sipRequest.Header.CallId, DateTime.Now);*/
                        
                        m_compiledScript.DefaultScope.SetVariable("dispatcher", this);
                        m_compiledScript.DefaultScope.SetVariable("request", sipRequest);
                        m_compiledScript.Execute();
                        //DispatchRequest("127.0.0.1:5070", sipRequest);
                    }
                }
                else if (!m_callIdEndPoints.ContainsKey(sipRequest.Header.CallId)) {
                    // A new request has originated from an App Server.
                    //logger.Debug("CalLDispatcher adding endpoint for callid=" + sipRequest.Header.CallId + " as " + remoteEndPoint.ToString() + ".");
                    m_callIdEndPoints.Add(sipRequest.Header.CallId, remoteEndPoint.ToString());
                    m_callIdAddedAt.Add(sipRequest.Header.CallId, DateTime.Now);
                    m_sipTransport.SendRequest(dispatchEndPoint, sipRequest);
                }
                else {
                    // A request from an app server that matches an existing dispatched callid.
                    m_sipTransport.SendRequest(dispatchEndPoint, sipRequest);
                }

                //logger.Debug("Dispatching " + sipRequest.Method + " from " + remoteEndPoint.ToString() + " to " + dispatchEndPoint.ToString() + ", callid=" + sipRequest.Header.CallId + ".");

                RemoveExpiredCallIds();
            }
            catch (Exception excp) {
                logger.Error("Exception Dispatch SIPRequest. " + excp.Message);
                throw;
            }
        }

        public void Dispatch(SIPEndPoint remoteEndPoint, SIPResponse sipResponse) {
            try {
                //logger.Debug("Dispatch SIPResponse from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " " + sipResponse.StatusCode + ".");
                SIPEndPoint dispatchEndPoint = m_outboundProxy;

                if (remoteEndPoint.ToString() == m_outboundProxy.ToString()) {
                    if (m_callIdEndPoints.ContainsKey(sipResponse.Header.CallId)) {
                        dispatchEndPoint = SIPEndPoint.ParseSIPEndPoint(m_callIdEndPoints[sipResponse.Header.CallId]);
                    }
                    else {
                        dispatchEndPoint = null;
                    }
                }

                if (dispatchEndPoint != null) {
                    m_sipTransport.SendResponse(dispatchEndPoint, sipResponse);
                }
                else {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The callid for an " + sipResponse.Header.CSeqMethod + " response was not found in the dispatcher callids lookup table, dropping, callid=" + sipResponse.Header.CallId + ".", null));
                }
            }
            catch (Exception excp) {
                logger.Error("Exception Dispatch SIPResponse. " + excp.Message);
                throw;
            }
        }

        public bool IsWorkerSIPEndPoint(SIPEndPoint remoteEndPoint) {
            return m_workerSIPEndPoints.Contains(remoteEndPoint.ToString());
        }

        public bool IsDispatcherCall(string callId) {
            return m_callIdEndPoints.ContainsKey(callId);
        }

        private void RemoveExpiredCallIds() {
            try {
                string[] callIds = m_callIdAddedAt.Keys.ToArray();
                if (callIds != null && callIds.Length > 0) {
                    for (int index = 0; index < callIds.Length; index++) {
                        string callId = callIds[index];
                        if (m_callIdAddedAt[callId] < DateTime.Now.AddSeconds(MAX_LIFETIME_SECONDS * -1)) {
                            logger.Debug("SIPCallDispatcher removing expired callid=" + callId + ".");
                            m_callIdEndPoints.Remove(callId);
                            m_callIdAddedAt.Remove(callId);
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception RemoveExpiredCallIds. " + excp.Message);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent) {
            if (SIPMonitorLogEvent_External != null) {
                try {
                    SIPMonitorLogEvent_External(monitorEvent);
                }
                catch (Exception excp) {
                    logger.Error("Exception FireProxyLogEvent SIPAppServerCore. " + excp.Message);
                }
            }
        }

        public void Log(string message) {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.CallDispatcher, message, null));
        }

        public void DispatchRequest(string dispatchTo, SIPRequest sipRequest) {
            SIPEndPoint dispatchEndPoint = SIPEndPoint.ParseSIPEndPoint(dispatchTo);
            DispatchRequest(dispatchEndPoint, sipRequest);
        }

        public void DispatchRequest(SIPEndPoint dispatchEndPoint, SIPRequest sipRequest) {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dispatching new " + sipRequest.Method + " request to " + dispatchEndPoint.ToString() + ", callid=" + sipRequest.Header.CallId + ".", null));
            m_callIdEndPoints.Add(sipRequest.Header.CallId, dispatchEndPoint.ToString());
            m_callIdAddedAt.Add(sipRequest.Header.CallId, DateTime.Now);
            m_sipTransport.SendRequest(dispatchEndPoint, sipRequest);
        }

        public SIPEndPoint GetFirstHealthyEndPoint() {
            lock (m_callDispatcherWorkers) {
                foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                    if (worker.IsHealthy()) {
                        return worker.AppServerEndpoint;
                    }
                }
            }

            logger.Warn("GetCallManagerClient could not find a healthy SIPCallDispatcherWorker.");

            return null;
        }

        public void Respond(SIPRequest sipRequest, SIPResponseStatusCodesEnum statusCode, string reason) {
            SIPResponse errorResponse = SIPTransport.GetResponse(sipRequest, statusCode, reason);
            m_sipTransport.SendResponse(m_outboundProxy, errorResponse);
        }
    }
}
