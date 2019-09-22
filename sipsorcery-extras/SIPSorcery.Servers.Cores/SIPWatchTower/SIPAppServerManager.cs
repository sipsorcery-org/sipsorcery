// ============================================================================
// FileName: SIPAppServerManager.cs
//
// Description:
// This class is responsible for starting the SIP Sorcery application server workers. Once started it monitors
// the workers to ensure they are still operating correctly and restarts them if not.
//
// Author(s):
// Aaron Clauson
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;
using Microsoft.Scripting.Hosting;

namespace SIPSorcery.Servers
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Example call dispatcher workers node:
    /// 
    ///  <sipappserverworkers>
    ///   <sipappserverworker>
    ///     <workerprocesspath>C:\Temp\sipsorcery-appsvr1\sipsorcery-appsvr.exe</workerprocesspath>
    ///     <workerprocessargs>-sip:{0} -cms:{1}</workerprocessargs>
    ///     <sipsocket>127.0.0.1:5070</sipsocket>
    ///     <callmanageraddress>http://localhost:8081/callmanager</callmanageraddress>
    ///    </sipappserverworker>
    ///   </sipappserverworkers>
    ///   
    /// </remarks>
    public class SIPAppServerManager : ISIPCallDispatcher
    {
        private const string WORKER_PROCESS_MONITOR_THREAD_NAME = "sipappservermanager-workermonitor";
        private const string WORKER_PROCESS_PROBE_THREAD_NAME = "sipappservermanager-probe";

        private const int MAX_LIFETIME_SECONDS = 180;
        private const long MAX_PHYSICAL_MEMORY = 150000000; // Restart worker processes when they've used up 150MB of physical memory.
        private const int PROCESS_RESTART_DELAY = 33;
        private const int CHECK_WORKER_MEMORY_PERIOD = 1000;
        private const int PROBE_WORKER_CALL_PERIOD = 15000;
        private const int INITIAL_PROBE_RETRANSMIT_LIMIT = 3;

        private static int m_unhealthyPriority = SIPCallDispatcherFile.DISABLED_APPSERVER_PRIORITY;
        private static int m_healthyPriority = SIPCallDispatcherFile.USEALWAYS_APPSERVER_PRIORITY;

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;
        private SIPTransport m_sipTransport;
        private XmlNode m_appServerWorkersNode;
        private ServiceHost m_callManagerPassThruSvcHost;
        private bool m_exit;
        private string m_dispatcherUsername = SIPCallManager.DISPATCHER_SIPACCOUNT_NAME;
        private string m_appServerEndPointsPath;
        private SIPCallDispatcherFile m_sipCallDispatcherFile;

        private List<SIPAppServerWorker> m_appServerWorkers = new List<SIPAppServerWorker>();
        private List<string> m_workerSIPEndPoints = new List<string>();                             // Allow quick lookups to determine whether a remote end point is that of a worker process.

        public SIPAppServerManager(
            SIPMonitorLogDelegate logDelegate,
            SIPTransport sipTransport,
            XmlNode appServerWorkersNode,
            string appServerEndPointsPath)
        {
            if (appServerWorkersNode == null || appServerWorkersNode.ChildNodes.Count == 0)
            {
                throw new ArgumentNullException("A SIPAppServerManager cannot be created with an empty workers node.");
            }

            SIPMonitorLogEvent_External = logDelegate;
            m_sipTransport = sipTransport;
            m_appServerWorkersNode = appServerWorkersNode;
            m_appServerEndPointsPath = appServerEndPointsPath;

            if (!appServerEndPointsPath.IsNullOrBlank() && File.Exists(appServerEndPointsPath))
            {
                m_sipCallDispatcherFile = new SIPCallDispatcherFile(logDelegate, appServerEndPointsPath);
            }

            try
            {
                CallManagerPassThruServiceInstanceProvider callManagerPassThruSvcInstanceProvider = new CallManagerPassThruServiceInstanceProvider(this);
                m_callManagerPassThruSvcHost = new ServiceHost(typeof(CallManagerPassThruService));
                m_callManagerPassThruSvcHost.Description.Behaviors.Add(callManagerPassThruSvcInstanceProvider);
                m_callManagerPassThruSvcHost.Open();

                logger.Debug("SIPAppServerManager CallManagerPassThru hosted service successfully started on " + m_callManagerPassThruSvcHost.BaseAddresses[0].AbsoluteUri + ".");
            }
            catch (Exception excp)
            {
                logger.Warn("Exception starting SIPAppServerManager CallManagerPassThru hosted service. " + excp.Message);
            }

            foreach (XmlNode appServerWorkerNode in m_appServerWorkersNode.ChildNodes)
            {
                SIPAppServerWorker appServerWorker = new SIPAppServerWorker(appServerWorkerNode);
                if (m_sipCallDispatcherFile != null)
                {
                    appServerWorker.Healthy += WorkerIsHealthy;
                    appServerWorker.Unhealthy += WorkerIsUnhealthy;
                }
                m_appServerWorkers.Add(appServerWorker);
                m_workerSIPEndPoints.Add(appServerWorker.AppServerEndpoint.ToString());
                logger.Debug("SIPAppServerManager worker added for " + appServerWorker.AppServerEndpoint.ToString() + " and " + appServerWorker.CallManagerAddress.ToString() + ".");
            }

            ThreadPool.QueueUserWorkItem(delegate { SpawnWorkers(); });
            ThreadPool.QueueUserWorkItem(delegate { ProbeWorkers(); });
        }

        /// <summary>
        /// Event handler that gets fired when a worker process is identified as being healthy after start up.
        /// </summary>
        private void WorkerIsHealthy(SIPAppServerWorker worker)
        {
            if (worker.IsHealthy())
            {
                m_sipCallDispatcherFile.UpdateAppServerPriority(worker.AppServerEndpoint, m_healthyPriority);
            }
            else
            {
                logger.Warn("An app server worker was unhealthy after the process initialisation period.");
            }
        }

        /// <summary>
        /// Event handler that gets fired when a worker process is identified as being unhealthy at any point in its lifetime.
        /// </summary>
        /// <param name="worker"></param>
        private void WorkerIsUnhealthy(SIPAppServerWorker worker)
        {
            m_sipCallDispatcherFile.UpdateAppServerPriority(worker.AppServerEndpoint, m_unhealthyPriority);
        }

        /// <summary>
        /// Runs a persistent thread that does the initial start up of the SIP application server workers and then restarts
        /// the process as necessary.
        /// </summary>
        private void SpawnWorkers()
        {
            try
            {
                Thread.CurrentThread.Name = WORKER_PROCESS_MONITOR_THREAD_NAME;

                foreach (SIPAppServerWorker worker in m_appServerWorkers)
                {
                    StartWorkerProcess(worker);
                }

                while (!m_exit)
                {
                    try
                    {
                        lock (m_appServerWorkers)
                        {
                            foreach (SIPAppServerWorker worker in m_appServerWorkers)
                            {
                                if (worker.InitialProbeResponseReceived)
                                {
                                    if (worker.RestartTime != null)
                                    {
                                        // The worker is unhealthy and a restart is required.
                                        if (worker.RestartTime < DateTime.Now)
                                        {
                                            StartWorkerProcess(worker);
                                        }
                                    }
                                    else if (!worker.IsHealthy())
                                    {
                                        // Worker process has exited unexpectedly.
                                        if (!worker.IsUnHealthy)
                                        {
                                            // Worker's health status has changed, update the app server dispatcher file.
                                            worker.IsUnHealthy = true;
                                            m_sipCallDispatcherFile.UpdateAppServerPriority(worker.AppServerEndpoint, m_unhealthyPriority);
                                        }
                                        StartWorkerProcess(worker);
                                    }
                                    else
                                    {
                                        // Worker process is healthy and normal. Check the memory usage.
                                        worker.WorkerProcess.Refresh();
                                        if (worker.WorkerProcess.PrivateMemorySize64 >= MAX_PHYSICAL_MEMORY)
                                        {
                                            // The memory limit has been reached but don't restart until it is safe to do so.
                                            if ((from wk in m_appServerWorkers where wk.RestartTime != null select wk).Count() == 0 &&      // Make sure there are no other restarts currently scheduled.
                                                (from wk in m_appServerWorkers where wk != worker && wk.IsHealthy() select wk).Count() > 0) // Make sure there is at least one other healthy worker.
                                            {
                                                logger.Debug("Worker process on pid=" + worker.WorkerProcess.Id + " has reached the memory limit, scheduling a restart.");
                                                worker.ScheduleRestart(DateTime.Now.AddSeconds(PROCESS_RESTART_DELAY));
                                            }
                                            else
                                            {
                                                logger.Debug("Worker process on pid=" + worker.WorkerProcess.Id + " has reached the memory limit but a restart was not safe.");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception checkWorkersExcp)
                    {
                        logger.Error("Exception SIPAppServerManager Checking Workers. " + checkWorkersExcp.Message);
                    }

                    Thread.Sleep(CHECK_WORKER_MEMORY_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerManager SpawnWorkers. " + excp.Message);
            }
        }

        /// <summary>
        /// Starts a new SIP application server worker process.
        /// </summary>
        private void StartWorkerProcess(SIPAppServerWorker worker)
        {
            string errorMessage = worker.StartProcess();
            if (errorMessage == null)
            {
                ProbeWorker(worker, true);
            }
            else
            {
                logger.Warn("Error starting worker process for " + worker.AppServerEndpoint.ToString() + ". " + errorMessage);
            }
        }

        /// <summary>
        /// Sends a probe, which is a SIP INVITE request, to the first healthy application server. Probes will also be resent to any
        /// application servers that have not responded to their initial probe.
        /// </summary>
        private void ProbeWorkers()
        {
            try
            {
                while (!m_exit)
                {
                    Thread.Sleep(PROBE_WORKER_CALL_PERIOD);

                    try
                    {
                        //SIPAppServerWorker activeWorker = GetFirstHealthyWorker();
                        //if (activeWorker != null)
                        //{
                        //    ProbeWorker(activeWorker, false);
                        //}
                        //else
                        //{
                        //    logger.Warn("SIPAppServerManager was not able to find a healthy app server endpoint.");
                        //}

                        lock (m_appServerWorkers)
                        {
                            foreach (SIPAppServerWorker worker in m_appServerWorkers)
                            {
                                if (!worker.InitialProbeResponseReceived)
                                {
                                    logger.Debug("Resending initial probe to " + worker.AppServerEndpoint.ToString() + ".");
                                    ProbeWorker(worker, true);
                                }
                                else if (worker.IsHealthy())
                                {
                                    ProbeWorker(worker, false);
                                }
                            }
                        }
                    }
                    catch (Exception probeExcp)
                    {
                        logger.Error("Exception SIPAppServerManager Sending Probe. " + probeExcp.Message);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerManager ProberWorkers. " + excp.Message);
            }
        }

        /// <summary>
        /// Sends the SIP INVITE probe request.
        /// </summary>
        private void ProbeWorker(SIPAppServerWorker worker, bool isInitialProbe)
        {
            try
            {
                if (isInitialProbe)
                {
                    worker.InitialProbeCount++;
                }

                int workerProcessID = worker.WorkerProcess.Id;
                SIPEndPoint workerEndPoint = worker.AppServerEndpoint;
                DateTime probeSentAt = DateTime.Now;
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(m_dispatcherUsername, null, "sip:" + m_dispatcherUsername + "@" + workerEndPoint.GetIPEndPoint().ToString(),
                                   "sip:" + m_dispatcherUsername + "@sipcalldispatcher", "sip:" + workerEndPoint.GetIPEndPoint().ToString(), null, null, null, SIPCallDirection.Out, null, null, null);
                SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, null, null, null, null);
                
                uac.CallFailed += (failedUAC, errorMessage) =>
                {
                    AppServerCallFailed(failedUAC, errorMessage, workerProcessID, probeSentAt, isInitialProbe);
                };

                uac.CallAnswered += (call, sipResponse) =>
                {
                    if (sipResponse.Status != SIPResponseStatusCodesEnum.BadExtension)
                    {
                        //logger.Warn("Probe call answered with unexpected response code of " + sipResponse.StatusCode + ".");
                        AppServerCallFailed(call, "Unexpected response of " + ((int)sipResponse.StatusCode) + " on probe call.", workerProcessID, probeSentAt, isInitialProbe);
                    }
                    else
                    {
                        AppServerCallSucceeded(call);
                    }
                };

                uac.Call(callDescriptor);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerManager ProberWorker. " + excp.Message);
            }
        }

        /// <summary>
        /// Event handler for a successful probe response.
        /// </summary>
        private void AppServerCallSucceeded(ISIPClientUserAgent uac)
        {
            try
            {
                string workerSocket = SIPURI.ParseSIPURI(uac.CallDescriptor.Uri).Host;
                SIPAppServerWorker worker = GetWorkerForEndPoint(workerSocket);
                if (!worker.InitialProbeResponseReceived)
                {
                    logger.Debug("Initial probe received for " + workerSocket + ".");
                    worker.InitialCallSuccessful();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppServerCallSucceeded. " + excp.Message);
            }
        }

        /// <summary>
        /// Event handler for a failed probe response.
        /// </summary>
        private void AppServerCallFailed(ISIPClientUserAgent uac, string errorMessage, int workerProcessID, DateTime probeSentAt, bool isInitialProbe)
        {
            try
            {
                string workerSocket = SIPURI.ParseSIPURI(uac.CallDescriptor.Uri).Host;
                logger.Warn("SIPAppServerManager call to " + workerSocket + " for PID " + workerProcessID + " failed, initial probe " + isInitialProbe  + " , sent at " + probeSentAt.ToString("dd MMM yyyy HH:mm:ss") + ", " + errorMessage);

                // Find the worker for the failed end point.
                SIPAppServerWorker failedWorker = GetWorkerForEndPoint(workerSocket);
                    
                // Make sure the worker process hasn't changed in the meantime and don't restart for initial probes.
                if (failedWorker != null && failedWorker.WorkerProcess != null && failedWorker.WorkerProcess.Id == workerProcessID)
                {
                    if (!isInitialProbe)
                    {
                        failedWorker.InitialProbeResponseReceived = true;
                        logger.Debug("Scheduling immediate restart on app server worker process pid=" + failedWorker.WorkerProcess.Id + ", " + workerSocket + " due to failed probe.");
                        failedWorker.ScheduleRestart(DateTime.Now);
                    }
                    else if (failedWorker.InitialProbeCount >= INITIAL_PROBE_RETRANSMIT_LIMIT)
                    {
                        failedWorker.InitialProbeResponseReceived = true;
                        logger.Debug("Initial probe retransmit limit reached, scheduling immediate restart on app server worker process pid=" + failedWorker.WorkerProcess.Id + ", " + workerSocket + " due to failed probe.");
                        failedWorker.ScheduleRestart(DateTime.Now);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppServerCallFailed. " + excp.Message);
            }
        }

        /// <summary>
        /// Matches a SIP end point to an app server worker process.
        /// </summary>
        private SIPAppServerWorker GetWorkerForEndPoint(string host)
        {
            lock (m_appServerWorkers)
            {
                foreach (SIPAppServerWorker worker in m_appServerWorkers)
                {
                    if (worker.AppServerEndpoint.GetIPEndPoint().ToString() == host)
                    {
                        return worker;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Called when the SIPAppServerManager is being shutdown. Informs each of the threads they should halt whatever they
        /// are doing and exit.
        /// </summary>
        public void Stop()
        {
            m_exit = true;
        }

        /// <summary>
        /// Retrieves the first healthy application server WCF call manager proxy from the list of worker processes.
        /// This proxy can be used for initiating web calls and is the interface between the web and the SIP application 
        /// servers.
        /// </summary>
        public CallManagerProxy GetCallManagerClient()
        {
            lock (m_appServerWorkers)
            {
                foreach (SIPAppServerWorker worker in m_appServerWorkers)
                {
                    if (worker.IsHealthy())
                    {
                        return new CallManagerProxy(new BasicHttpBinding(), worker.CallManagerAddress);
                    }
                }
            }

            logger.Warn("GetCallManagerClient could not find a healthy SIPAppServerWorker.");

            return null;
        }

        /// <summary>
        /// Retrieves the first healthy application server worker from the list.
        /// </summary>
        public SIPAppServerWorker GetFirstHealthyWorker()
        {
            lock (m_appServerWorkers)
            {
                foreach (SIPAppServerWorker worker in m_appServerWorkers)
                {
                    if (worker.IsHealthy())
                    {
                        return worker;
                    }
                }
            }

            logger.Warn("GetFirstHealthyEndPoint could not find a healthy SIPAppServerWorker.");

            return null;
        }
    }
}
