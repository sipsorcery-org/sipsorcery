// ============================================================================
// FileName: SIPMonitorMediator.cs
//
// Description:
// Hosts client connections to the public sockets on the SIP Monitor Server and then mediates
// the events sent to each and commands received from those clients capable of sending commands.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created.
// 14 Nov 2008  Aaron Clauson   Renamed from ProxyMonitor to SIPMonitorMediator.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    /// <summary>
    /// </summary>
    public class SIPMonitorMediator
	{
        private const int MAX_LOGFILE_SIZE = 1000000;   // Maximum log file size, approx. 1MB. Once that size oldest messages will be truncated.
        private const string EVENT_PROCESSOR_THREAD_NAME = "monitor-eventlistener";

		private static ILog logger = AppState.logger;

        private string m_fileLogDirectory;
        private bool m_listenForEvents = true;

        private int m_eventListenerPort = 0;            // The loopback port that the monitor process will listen on for events from SIP Server agents.
        private UdpClient m_udpEventListener;           // The UDP listener that this process will listen for events from SIP Server agents on.

        private ISIPMonitorPublisher m_clientManager;

        public SIPMonitorMediator(
           int eventListenerPort,
           string fileLogDirectory,
           ISIPMonitorPublisher sipMonitorPublisher)
        {
            m_eventListenerPort = eventListenerPort;
            m_fileLogDirectory = fileLogDirectory;
            m_clientManager = sipMonitorPublisher;
        }

        public void StartMonitoring()
        {
            Thread monitorThread = new Thread(new ThreadStart(StartEventProcessing));
            monitorThread.Name = EVENT_PROCESSOR_THREAD_NAME;
            monitorThread.Start();
        }

        private void StartEventProcessing()
		{
			try
			{
                // This socket is to listen for the events from SIP Servers.
                m_udpEventListener = new UdpClient(m_eventListenerPort, AddressFamily.InterNetwork);

                // Wait for events from the SIP Server agents and when received pass them onto the client connections for processing and dispatching.
                IPEndPoint remoteEP = null;
                while (m_listenForEvents)
                {
                    byte[] buffer = m_udpEventListener.Receive(ref remoteEP);

                    if (buffer != null && buffer.Length > 0)
                    {
                        //logger.Debug("Monitor event received: " + Encoding.ASCII.GetString(buffer, 0, buffer.Length) + ".");
                        SIPMonitorEvent sipMonitorEvent = SIPMonitorEvent.ParseEventCSV(Encoding.ASCII.GetString(buffer, 0, buffer.Length));
                        
                        if (sipMonitorEvent != null)
                        {
                            m_clientManager.MonitorEventReceived(sipMonitorEvent);
                        }
                    }
                }

                m_udpEventListener.Close();
			}
			catch(Exception excp)
			{
                logger.Error("Exception SIPMonitorMediator StartEventProcessing. " + excp.Message);
			}
		}

        public void Stop()
        {
            try
            {
               m_listenForEvents = false;

               try
               {
                   m_udpEventListener.Close();
               }
               catch (Exception serverExcp)
               {
                   logger.Warn("SIPMonitorMediator Stop exception shutting the UDP event listening socket. " + serverExcp.Message);
               }

               logger.Debug("SIPMonitorMediator Stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMediator Stop. " + excp.Message);
            }
        }
    }
}
