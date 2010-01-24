// ============================================================================
// FileName: FastAGIDaemon.cs
//
// Description:
// Implements a daemon to listen for FastAGI requests and queue them.
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Jan 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK
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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Asterisk.FastAGI
{
	public class FastAGIDaemon
	{
        private const string THREAD_NAME = "fastagi";

		private static ILog logger = AppState.logger;

		private string m_agiServerIPAddress = FastAGIConfiguration.AGIServerIPAddress; 
		private int  m_agiServerPort = FastAGIConfiguration.AGIServerPort; 
        private int m_agiWorkerThreadsCount = FastAGIConfiguration.AGIWorkerThreadsCount; 

        private int m_queueAlertThreshold = FastAGIConfiguration.FastAGIQueuedRequestsAlertThreshold;
        private string m_agiErrorAddress = FastAGIConfiguration.AlertEmailAddress;
        private string m_queueMetricsFilePath = FastAGIConfiguration.FastAGIQueueMetricsFilePath;
        private string m_appMetricsFilePath = FastAGIConfiguration.FastAGIAppMetricsFilePath;

        private TcpListener m_fastAGIServer = null;
        private FastAGIQueueDaemon m_fastAGIQueueDaemon = null;
        private bool m_stopDaemon = false;

		public void Start()
		{
			try
			{
				logger.Debug("FastAGIDaemon Starting");

                if (m_agiServerIPAddress == null || m_agiServerIPAddress.Trim().Length == 0)
                {
                    throw new ApplicationException("Could not start FastAGI service as no listening IP address was specified.");
                }
                else if (m_agiWorkerThreadsCount <= 0)
                {
                    throw new ApplicationException("Could not start FastAGI service as the number of worker threads is zero.");
                }

				IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(m_agiServerIPAddress), m_agiServerPort);

				logger.Debug("Starting FastAGI socket on " + IPSocket.GetSocketString(localEndPoint));

				m_fastAGIServer = new TcpListener(localEndPoint);
                m_fastAGIServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                m_fastAGIServer.Start();

                m_fastAGIQueueDaemon = new FastAGIQueueDaemon(m_queueAlertThreshold, m_queueMetricsFilePath, m_appMetricsFilePath, m_agiErrorAddress);
                m_fastAGIQueueDaemon.Start(m_agiWorkerThreadsCount, THREAD_NAME);

                while (!m_stopDaemon)
				{
					Socket fastAGIClient = m_fastAGIServer.AcceptSocket();
					IPEndPoint connEndPoint = (IPEndPoint)fastAGIClient.RemoteEndPoint;

					logger.Debug("new connection from " + IPSocket.GetSocketString(connEndPoint));

                    m_fastAGIQueueDaemon.AddNewConnection(fastAGIClient);
				}

                logger.Debug("FastAGI Deamon Stopping.");
                
			}
			catch(Exception excp)
			{
                logger.Error("Exception FastAGIDaemon Start. " + excp.Message);
			}
		}

        public void Stop()
        {
            try
			{
                logger.Debug("FastAGIDaemon Stop.");
                
                m_stopDaemon = true;

                if (m_fastAGIQueueDaemon != null)
                {
                    m_fastAGIQueueDaemon.Stop();
                }

                logger.Debug("Stopping FastAGI listener socket for new connections.");

                if (m_fastAGIServer != null)
                {
                    m_fastAGIServer.Stop();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FastAGIDaemon Stop. " + excp.Message);
            }
        }
	}
}
