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
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

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

        private const long MAX_PHYSICAL_MEMORY = 150000000; // Restart worker processes when they've used up 150MB of physical memory.
        private const int PROCESS_RESTART_DELAY_SECONDS = 33;
        private const int CHECK_WORKER_PERIOD_SECONDS = 1;
        private const int WAIT_FOR_RECYCLE_LOCK_SECONDS = 10;

        private static int m_unhealthyPriority = SIPCallDispatcherFile.DISABLED_APPSERVER_PRIORITY;
        private static int m_healthyPriority = SIPCallDispatcherFile.USEALWAYS_APPSERVER_PRIORITY;
        private static int m_probePeriodSeconds = SIPAppServerWorker.PROBE_WORKER_CALL_PERIOD_SECONDS;

        private static ILog logger = AppState.logger;

        private static object m_recycleLock = new object();

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;
        private SIPTransport m_sipTransport;
        private XmlNode m_appServerWorkersNode;
        private ServiceHost m_callManagerPassThruSvcHost;
        private bool m_exit;
        private string m_appServerEndPointsPath;
        private SIPCallDispatcherFile m_sipCallDispatcherFile;
        private List<SIPAppServerWorker> m_appServerWorkers = new List<SIPAppServerWorker>();

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
                XmlNode workerNode = appServerWorkerNode;
                ThreadPool.QueueUserWorkItem(delegate { ManageWorker(workerNode); });
                Thread.Sleep(1000);     // Won't be able to start until all subsequnet workers are started anyway due to the need for the recycle lock.
            }
        }

        /// <summary>
        /// Method that gets spawned on a dedicated thread to manage an app server process.
        /// </summary>
        private void ManageWorker(XmlNode appServerWorkerNode)
        {
            try
            {
                SIPAppServerWorker appServerWorker = RecycleAppServer(null, 0, appServerWorkerNode);
                string appServerSocket = appServerWorker.AppServerEndpoint.ToString();
                Thread.CurrentThread.Name = "manage-" + appServerWorker.AppServerEndpoint.Port;

                DateTime lastProbeTime = DateTime.MinValue;

                while (!m_exit)
                {
                    // Process checks.
                    if (!appServerWorker.NeedsImmediateRestart && !appServerWorker.NeedsToRestart)
                    {
                        if (appServerWorker.WorkerProcess != null && appServerWorker.WorkerProcess.HasExited)
                        {
                            // This is the only case where action is taken to immediately disable the use of a worker process outside of the access controlled
                            // RecycleAppServer method. The reason being there's no point attempting to use a worker process that has completely gone.
                            logger.Debug("Worker has disappeared for " + appServerSocket + ". Deactivating and marking for immediate restart.");
                            appServerWorker.NeedsImmediateRestart = true;
                            appServerWorker.IsDeactivated = true;
                            lock (m_appServerWorkers)
                            {
                                m_appServerWorkers.Remove(appServerWorker);
                            }
                            m_sipCallDispatcherFile.UpdateAppServerPriority(appServerWorker.AppServerEndpoint, m_unhealthyPriority);
                        }
                        else
                        {
                            appServerWorker.WorkerProcess.Refresh();
                            if (appServerWorker.WorkerProcess.PrivateMemorySize64 >= MAX_PHYSICAL_MEMORY)
                            {
                                logger.Debug("Memory limit reached on worker " + appServerSocket + " pid " + appServerWorker.ProcessID + ". Marking for graceful restart.");
                                appServerWorker.NeedsToRestart = true;
                            }
                        }
                    }

                    // Periodically send a SIP call probe to the app server socket.
                    if (!appServerWorker.NeedsImmediateRestart && !appServerWorker.NeedsToRestart
                        && DateTime.Now.Subtract(lastProbeTime).TotalSeconds > m_probePeriodSeconds)
                    {
                        appServerWorker.SendProbe();
                        lastProbeTime = DateTime.Now;
                    }

                    // Restarts.
                    if (appServerWorker.NeedsImmediateRestart || appServerWorker.NeedsToRestart)
                    {
                        double secondsSinceLastStart = DateTime.Now.Subtract(appServerWorker.WorkerProcessStartTime).TotalSeconds;

                        if (secondsSinceLastStart < PROCESS_RESTART_DELAY_SECONDS)
                        {
                            int secondsDelay = PROCESS_RESTART_DELAY_SECONDS - Convert.ToInt32(secondsSinceLastStart % Int32.MaxValue);
                            logger.Debug("Waiting " + secondsDelay + " seconds before attempting to recycle worker on " + appServerSocket + ".");
                            Thread.Sleep(secondsDelay * 1000);
                        }

                        string recycleType = (appServerWorker.NeedsImmediateRestart) ? "immediate" : "graceful";
                        logger.Debug("Attempting to get lock for " + recycleType + " recycle for " + appServerSocket + " pid " + appServerWorker.ProcessID + ".");
                        if (Monitor.TryEnter(m_recycleLock, WAIT_FOR_RECYCLE_LOCK_SECONDS * 1000))
                        {
                            int recycleDelay = (appServerWorker.NeedsToRestart) ? PROCESS_RESTART_DELAY_SECONDS : 0;
                            appServerWorker = RecycleAppServer(appServerWorker, recycleDelay, appServerWorkerNode);
                            lastProbeTime = DateTime.Now;
                            Monitor.Exit(m_recycleLock);
                        }
                        else
                        {
                            logger.Debug("Failed to acquire recycle lock for " + appServerSocket + " pid " + appServerWorker.ProcessID + ".");
                        }
                    }

                    Thread.Sleep(CHECK_WORKER_PERIOD_SECONDS * 1000);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ManageWorker. " + excp.Message);
            }
            finally
            {
                try
                {
                    Monitor.Exit(m_recycleLock);
                }
                catch { }
            }
        }

        private SIPAppServerWorker RecycleAppServer(SIPAppServerWorker badAppServerWorker, int delaySeconds, XmlNode appServerWorkerNode)
        {
            try
            {
                lock (m_recycleLock)
                {
                    // Shutdown existing worker process.
                    if (badAppServerWorker != null)
                    {
                        if (!badAppServerWorker.IsDeactivated)
                        {
                            logger.Debug("Deactivating worker on " + badAppServerWorker.AppServerEndpoint.ToString() + ".");
                            m_sipCallDispatcherFile.UpdateAppServerPriority(badAppServerWorker.AppServerEndpoint, m_unhealthyPriority);

                            lock (m_appServerWorkers)
                            {
                                m_appServerWorkers.Remove(badAppServerWorker);
                            }
                        }

                        if (delaySeconds > 0)
                        {
                            logger.Debug("Delaying process restart for " + badAppServerWorker.AppServerEndpoint.ToString() + " by " + delaySeconds + "s.");
                            Thread.Sleep(delaySeconds * 1000);
                        }

                        badAppServerWorker.Kill();
                    }

                    // Start new worker process and wait for a successful probe response before returning.
                    SIPAppServerWorker appServerWorker = new SIPAppServerWorker(appServerWorkerNode, m_sipTransport);
                    logger.Debug("Starting new worker on " + appServerWorker.AppServerEndpoint.ToString() + ".");

                    DateTime startTime = DateTime.Now;
                    if (appServerWorker.StartProcess())
                    {
                        logger.Debug("Worker on " + appServerWorker.AppServerEndpoint.ToString() + " ready after " + DateTime.Now.Subtract(startTime).TotalSeconds.ToString("0.##") + " seconds.");
                        m_sipCallDispatcherFile.UpdateAppServerPriority(appServerWorker.AppServerEndpoint, m_healthyPriority);
                        lock (m_appServerWorkers)
                        {
                            m_appServerWorkers.Add(appServerWorker);
                        }
                    }
                    else
                    {
                        logger.Debug("Worker on " + appServerWorker.AppServerEndpoint.ToString() + " failed to reach a ready state after " + DateTime.Now.Subtract(startTime).TotalSeconds.ToString("0.##") + " seconds.");
                        appServerWorker.NeedsImmediateRestart = true;
                    }

                    return appServerWorker;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RecycleAppServer. " + excp.Message);
                throw;
            }
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
    }
}
