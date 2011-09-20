// ============================================================================
// FileName: SIPAppServerWorker.cs
//
// Description:
// Worker process for an application server worker. Worker processes are started and monitored by the 
// SIPAppServerManager.
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
using System.Diagnostics;
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

namespace SIPSorcery.Servers
{
    public class SIPAppServerWorker
    {
        private const int INITIAL_PROBE_RETRANSMIT_LIMIT = 6;   // The number of probes to send when a process is first started before deciding it's terminal.
        private const int SUBSEQUENT_PROBE_RETRANSMIT_LIMIT = 1; // The number of missed probes after which to request an immediate process restart once the process has successfully started and responded.
        public const int PROBE_WORKER_CALL_PERIOD_SECONDS = 10;

        private static ILog logger = AppState.logger;

        private static string m_dispatcherUsername = SIPCallManager.DISPATCHER_SIPACCOUNT_NAME;
        private SIPTransport m_sipTransport;
        private SIPClientUserAgent m_probeUAC;
        private int m_missedProbesLimit = INITIAL_PROBE_RETRANSMIT_LIMIT;
        private int m_probeCount;
        private int m_missedProbes;
        private bool m_gotInitialProbeResponse;
        private ManualResetEvent m_initialResponseMRE = new ManualResetEvent(false);

        public string WorkerProcessPath;
        public string WorkerProcessArgs;
        public DateTime WorkerProcessStartTime;
        public SIPEndPoint AppServerEndpoint;
        public EndpointAddress CallManagerAddress;
        public Process WorkerProcess;
        public bool NeedsToRestart;                         // Gets set to true when the monitor decides there is something wrong and would like to restart the process gracefully.
        public bool NeedsImmediateRestart;
        public bool IsDeactivated;                          // If this is true indicates the worker has been removed from the dispatcher list and call attempts will not be made to it.

        public int ProcessID
        {
            get
            {
                return (WorkerProcess != null) ? WorkerProcess.Id : -1;
            }
        }

        public SIPAppServerWorker(XmlNode xmlConfigNode, SIPTransport sipTransport)
        {
            WorkerProcessPath = xmlConfigNode.SelectSingleNode("workerprocesspath").InnerText;
            WorkerProcessArgs = xmlConfigNode.SelectSingleNode("workerprocessargs").InnerText;
            AppServerEndpoint = SIPEndPoint.ParseSIPEndPoint(xmlConfigNode.SelectSingleNode("sipsocket").InnerText);
            CallManagerAddress = new EndpointAddress(xmlConfigNode.SelectSingleNode("callmanageraddress").InnerText);
            m_sipTransport = sipTransport;
        }

        public bool StartProcess()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(WorkerProcessPath, String.Format(WorkerProcessArgs, new object[] { AppServerEndpoint.ToString(), CallManagerAddress.ToString(), "false" }));
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            WorkerProcess = Process.Start(startInfo);
            WorkerProcessStartTime = DateTime.Now;
            logger.Debug("New call dispatcher worker process on " + AppServerEndpoint.ToString() + " started on pid=" + WorkerProcess.Id + ".");

            if (WorkerProcess != null && !WorkerProcess.HasExited)
            {
                while (!NeedsImmediateRestart && !m_gotInitialProbeResponse)
                {
                    // Send probes until a valid response is received or the initial threshold of missed probes is reached.
                    m_initialResponseMRE.Reset();
                    logger.Debug("Sending initial probe " + (m_probeCount + 1).ToString() + " for " + AppServerEndpoint.ToString() + " pid " + ProcessID + ".");
                    SendProbe();
                    m_initialResponseMRE.WaitOne(PROBE_WORKER_CALL_PERIOD_SECONDS * 1000);
                }

                if (m_gotInitialProbeResponse)
                {
                    logger.Debug("Initial probe response correctly received for " + AppServerEndpoint.ToString() + ".");
                    m_missedProbesLimit = SUBSEQUENT_PROBE_RETRANSMIT_LIMIT;
                    m_missedProbes = 0;
                    m_initialResponseMRE = null;
                    return true;
                }
                else
                {
                    logger.Warn("StartProcess for worker " + AppServerEndpoint.ToString() + " failed.");
                    return false;
                }
            }
            else
            {
                logger.Warn("Failed to start the worker process for " + AppServerEndpoint.ToString() + ", process dead.");
                return false;
            }
        }

        public void Kill()
        {
            try
            {
                logger.Debug("Killing worker process for " + AppServerEndpoint.ToString() + " on pid " + WorkerProcess.Id + ".");
                if (!WorkerProcess.HasExited)
                {
                    WorkerProcess.Kill();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerWorker Kill. " + excp.Message);
            }
        }

        public bool IsHealthy()
        {
            if (WorkerProcess != null && !WorkerProcess.HasExited && m_gotInitialProbeResponse && !NeedsImmediateRestart && !NeedsToRestart)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Sends the SIP INVITE probe request.
        /// </summary>
        public void SendProbe()
        {
            try
            {
                if (WorkerProcess == null)
                {
                    logger.Debug("When attempting to send probe the worker process was null. Marking for immediate restart.");
                    NeedsImmediateRestart = true;
                }
                else if (WorkerProcess.HasExited)
                {
                    logger.Debug("When attempting to send probe the worker had exited. Marking for immediate restart.");
                    NeedsImmediateRestart = true;
                }
                else if (m_probeUAC != null && !m_probeUAC.IsUACAnswered)
                {
                    // A probe call has timed out.
                    m_probeUAC.Cancel();
                    m_missedProbes++;
                    if (m_missedProbes >= m_missedProbesLimit)
                    {
                        logger.Warn(m_missedProbes + " probes missed for " + AppServerEndpoint.ToString() + ". Marking for immediate restart.");
                        NeedsImmediateRestart = true;
                    }
                }
                
                if(!NeedsImmediateRestart && !NeedsToRestart)
                {
                    m_probeCount++;
                    //logger.Debug("Sending probe " + m_probeCount + " to " + AppServerEndpoint.GetIPEndPoint().ToString() + ".");
                    DateTime probeSentAt = DateTime.Now;

                    SIPCallDescriptor callDescriptor = new SIPCallDescriptor(m_dispatcherUsername, null, "sip:" + m_dispatcherUsername + "@" + AppServerEndpoint.GetIPEndPoint().ToString(),
                                       "sip:" + m_dispatcherUsername + "@sipcalldispatcher", "sip:" + AppServerEndpoint.GetIPEndPoint().ToString(), null, null, null, SIPCallDirection.Out, null, null, null);
                    m_probeUAC = new SIPClientUserAgent(m_sipTransport, null, null, null, null);

                    m_probeUAC.CallAnswered += (call, sipResponse) =>
                    {
                        //logger.Debug("Probe response received for " + AppServerEndpoint.ToString() + ".");
                        if (sipResponse.Status != SIPResponseStatusCodesEnum.BadExtension)
                        //if (sipResponse.Status != SIPResponseStatusCodesEnum.InternalServerError)
                        {
                            logger.Warn("Probe to " + AppServerEndpoint.ToString() + " answered incorrectly on probe number " + m_probeCount + " after " 
                                    + DateTime.Now.Subtract(probeSentAt).TotalSeconds.ToString("0.##") + "s, unexpected response of " + ((int)sipResponse.StatusCode) + ".");
                            NeedsImmediateRestart = true;
                        }
                        else
                        {
                            m_gotInitialProbeResponse = true;
                        }

                        if (m_initialResponseMRE != null)
                        {
                            m_initialResponseMRE.Set();
                        }
                    };

                    m_probeUAC.Call(callDescriptor);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendProbe. " + excp.Message);
            }
        }
    }
}
