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
                    ProcessStartInfo startInfo = new ProcessStartInfo(WorkerProcessPath, String.Format(WorkerProcessArgs, new object[] { AppServerEndpoint.ToString(), CallManagerAddress.ToString() }));
                    startInfo.CreateNoWindow = true;
                    startInfo.UseShellExecute = false;
                    WorkerProcess = Process.Start(startInfo);
                    logger.Debug("New call dispatcher worker process started on pid=" + WorkerProcess.Id + ".");
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
                    logger.Error("Exception SIPCallDispatcherWorker IsHealthy. " + excp.Message);
                    return false;
                }
            }
        }

        private const string WORKER_PROCESS_MONITOR_THREAD_NAME = "sipcalldispatcher-workermonitor";
        private const int MAX_LIFETIME_SECONDS = 180;
        private const long MAX_PHYSICAL_MEMORY = 150000000; // Restart worker processes when they've used up 150MB of physical memory.
        private const int PROCESS_RESTART_DELAY = 33;

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;
        private SIPTransport m_sipTransport;
        private XmlNode m_callDispatcherNode;
        private SIPEndPoint m_outboundProxy;
        private CompiledCode m_compiledScript;
        private string m_dispatcherScriptPath;
        private ScriptLoader m_scriptLoader;
        private ServiceHost m_callManagerPassThruSvcHost;

        private List<SIPCallDispatcherWorker> m_callDispatcherWorkers = new List<SIPCallDispatcherWorker>();
        private Dictionary<string, string> m_callIdEndPoints = new Dictionary<string, string>();    // [callid, dispatched endpoint].
        private Dictionary<string, DateTime> m_callIdAddedAt = new Dictionary<string, DateTime>();  // [callid, time added].
        private List<string> m_workerSIPEndPoints = new List<string>();   // Allow quick lookups to determine whether a remote end point is that of a worker process.

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
                logger.Debug(" SIPCallDispatcher worker added for " + callDispatcherWorker.AppServerEndpoint.ToString() + " and " + callDispatcherWorker.CallManagerAddress.ToString() + ".");
            }

            ThreadPool.QueueUserWorkItem(delegate { SpawnWorkers(); });
        }

        private void SpawnWorkers() {
            try {
                Thread.CurrentThread.Name = WORKER_PROCESS_MONITOR_THREAD_NAME;

                foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                    worker.StartProcess();
               }
                
                //int checks = 0;
                while(true) {
                    lock (m_callDispatcherWorkers) {
                        foreach (SIPCallDispatcherWorker worker in m_callDispatcherWorkers) {
                            if (worker.RestartTime != null) {
                                if (worker.RestartTime < DateTime.Now) {
                                    logger.Debug("Killing worker process on pid=" + worker.WorkerProcess.Id + ".");
                                    worker.WorkerProcess.Kill();
                                    worker.StartProcess();
                                }
                            }
                            else if (!worker.IsHealthy()) {
                                worker.StartProcess();
                            }
                            else {
                                worker.WorkerProcess.Refresh();
                                if (worker.WorkerProcess.PrivateMemorySize64 >= MAX_PHYSICAL_MEMORY) {
                                    logger.Debug("Worker process on pid=" + worker.WorkerProcess.Id + " has reached the memory limit, scheduling a restart.");
                                    worker.RestartTime = DateTime.Now.AddSeconds(PROCESS_RESTART_DELAY);
                                }
                            }
                        }
                    }
                    
                    Thread.Sleep(1000);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCallDispatcher SpawnWorkers. " + excp.Message);
            }
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
                        //logger.Debug("CallDispatcher response app server endpoint for " + sipResponse.Header.CallId + " is " + dispatchEndPoint.ToString() + ".");
                        //if (dispatchEndPoint.ToString() == m_outboundProxy.ToString()) {
                         //   logger.Error("The CallDispatcher returned a dispatcher endpoint of the outboundproxy for a response that came from the outbound proxy, callid=" + sipResponse.Header.CallId + ".");
                         //   dispatchEndPoint = null;
                        //}
                    }
                    else {
                        dispatchEndPoint = null;
                    }
                }

                if (dispatchEndPoint != null) {
                    //logger.Debug("Dispatching " + sipResponse.StatusCode + " " + sipResponse.Header.CSeqMethod + " from " + remoteEndPoint + " to " + dispatchEndPoint.ToString() + ", topvia=" + sipResponse.Header.Vias.TopViaHeader.ToString() + ", callid=" + sipResponse.Header.CallId + ".");
                    //FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dispatching " + sipResponse.Header.CSeqMethod + " response to " + dispatchEndPoint.ToString() + ", callid=" + sipResponse.Header.CallId + ".", null));
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
