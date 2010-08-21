// ============================================================================
// FileName: SIPMonitorEventWriter.cs
//
// Description:
// Allows monitor events to be pushed from a SIP Server Agent to a listening
// monitor. The events are sent over UDP and are fire and forget so the actions
// of the listening Monitor will not be able to affect the Server Agent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 18 Aug 2006	Aaron Clauson	Created.
// 14 NOv 2008  Aaron Clauson   Renamed from ProxyMonitorChannel to SIPMonitorChannel.
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
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP.App;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
	/// <summary>
    /// Allows monitor events to be pushed from a SIP Server Agent to a listening
    /// monitor. The events are sent over UDP and are fire and forget so the actions
    /// of the listening Monitor will not be able to affect the Server Agent.
	/// </summary>
	public class SIPMonitorEventWriter
	{
		private static ILog logger = log4net.LogManager.GetLogger("monitor");

		private UdpClient m_monitorClient = new UdpClient();
		private IPEndPoint m_monitorEndPoint = null;

        private bool m_sendEvents = true;

        public SIPMonitorEventWriter(int monitorPort) 
		{
			m_monitorEndPoint = new IPEndPoint(IPAddress.Loopback, monitorPort);
		}

		public void Send(SIPMonitorEvent monitorEvent)
		{
			try
			{
                if (monitorEvent != null && m_sendEvents)
				{
                    string monitorEventStr = monitorEvent.ToCSV();

                    //if (monitorEvent.Message != null && monitorEvent.Message.Length > 0)
                    //{
                    //    monitorEventStr = monitorEvent.ToCSV();
                    //}

                    if (monitorEventStr != null)
                    {
                        m_monitorClient.Send(Encoding.ASCII.GetBytes(monitorEventStr), monitorEventStr.Length, m_monitorEndPoint);
                    }
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPMonitorEventWriter Send (SIPMonitorEvent). " + excp.Message);
			}
		}

        /*public void Send(SIPMonitorMachineEvent machineEvent)
        {
            try
            {
                if (machineEvent != null && m_sendEvents)
                {
                    string machineEventStr = null;

                    //if (machineEvent.Message != null && machineEvent.Message.Length > 0)
                    //{
                        machineEventStr = machineEvent.ToCSV();
                    //}

                    if (machineEventStr != null)
                    {
                        m_monitorClient.Send(Encoding.ASCII.GetBytes(machineEventStr), machineEventStr.Length, m_monitorEndPoint);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorEventPusher Send (SIPMonitorMachineEvent). " + excp.Message);
            }
        }*/

        public void Close()
        {
            try
            {
                m_sendEvents = false;
                m_monitorClient.Close();

                logger.Debug("SIPMonitorEventPusher closed on " + m_monitorEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorEventWriter Close. " + excp.Message);
            }
        }
	}
}
