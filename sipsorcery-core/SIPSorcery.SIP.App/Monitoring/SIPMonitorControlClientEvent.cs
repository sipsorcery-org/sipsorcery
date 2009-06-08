// ============================================================================
// FileName: SIPMonitorControlClientEvent.cs
//
// Description:
// Describes the types of events that can be sent by the different SIP Servers to SIP
// Monitor clients.
//
// Author(s):
// Aaron Clauson
//
// History:
// 15 Nov 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Describes the types of events that can be sent by the different SIP Servers to SIP
    /// Monitor clients.
    /// </summary>
	public class SIPMonitorControlClientEvent : SIPMonitorEvent
	{
        public const string SERIALISATION_PREFIX = "1";     // Prefix appended to the front of a serialised event to identify the type.
        private const string CALLDIRECTION_IN_STRING = "<-";
        private const string CALLDIRECTION_OUT_STRING = "->";
				
        private SIPMonitorControlClientEvent()
        { 
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.ControlClient;
        }

        public SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum serverType, SIPMonitorEventTypesEnum eventType, string message, string username)
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.ControlClient;

            ServerType = serverType;
            EventType = eventType;
            Message = message;
            Username = username;
            Created = DateTime.Now;
        }

        public SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum serverType, SIPMonitorEventTypesEnum eventType, string message, string username, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint) {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.ControlClient;

            ServerType = serverType;
            EventType = eventType;
            Message = message;
            Username = username;
            Created = DateTime.Now;
            ServerEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
        }

        public SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum serverType, SIPMonitorEventTypesEnum eventType, string message, SIPEndPoint serverSocket, SIPEndPoint fromSocket, SIPEndPoint toSocket)
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.ControlClient;

            ServerType = serverType;
            EventType = eventType;
            Message = message;
            ServerEndPoint = serverSocket;
            RemoteEndPoint = fromSocket;
            DestinationEndPoint = toSocket;
            Created = DateTime.Now;
        }

        public SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum serverType, SIPMonitorEventTypesEnum eventType, string message, SIPRequest sipRequest, SIPResponse sipResponse, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, SIPCallDirection callDirection)
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.ControlClient;

            ServerType = serverType;
            EventType = eventType;
            Message = message;
            RemoteEndPoint = remoteEndPoint;
            ServerEndPoint = localEndPoint;
            Created = DateTime.Now;

            string dirn = (callDirection == SIPCallDirection.In) ? CALLDIRECTION_IN_STRING : CALLDIRECTION_OUT_STRING;
            if (sipRequest != null)
            {
                Message = "REQUEST (" + Created.ToString("HH:mm:ss:fff") + "): " + localEndPoint + dirn + remoteEndPoint + "\r\n" + sipRequest.ToString();
            }
            else if (sipResponse != null)
            {
                Message = "RESPONSE (" + Created.ToString("HH:mm:ss:fff") + "): " + localEndPoint + dirn + remoteEndPoint + "\r\n" + sipResponse.ToString();
            }
        }

        public static SIPMonitorControlClientEvent ParseClientControlEventCSV(string eventCSV)
        {
            try
            {
                SIPMonitorControlClientEvent monitorEvent = new SIPMonitorControlClientEvent();

                if (eventCSV.IndexOf(END_MESSAGE_DELIMITER) != -1)
                {
                    eventCSV.Remove(eventCSV.Length - 2, 2);
                }

                string[] eventFields = eventCSV.Split(new char[] { '|' });

                monitorEvent.ServerType = SIPMonitorServerTypes.GetProxyServerType(eventFields[1]);
                monitorEvent.EventType = SIPMonitorEventTypes.GetProxyEventType(eventFields[2]);
                monitorEvent.Created = DateTime.ParseExact(eventFields[3], SERIALISATION_DATETIME_FORMAT, CultureInfo.InvariantCulture);

                string serverEndPointStr = eventFields[4];
                if (serverEndPointStr != null && serverEndPointStr.Trim().Length > 0)
                {
                    monitorEvent.ServerEndPoint = SIPEndPoint.ParseSIPEndPoint(serverEndPointStr);
                }

                string remoteEndPointStr = eventFields[5];
                if (remoteEndPointStr != null && remoteEndPointStr.Trim().Length > 0)
                {
                    monitorEvent.RemoteEndPoint = SIPEndPoint.ParseSIPEndPoint(remoteEndPointStr);
                }

                string dstEndPointStr = eventFields[6];
                if (dstEndPointStr != null && dstEndPointStr.Trim().Length > 0)
                {
                    monitorEvent.DestinationEndPoint = SIPEndPoint.ParseSIPEndPoint(dstEndPointStr);
                }

                monitorEvent.Username = eventFields[7];
                monitorEvent.Message = eventFields[8].Trim('#');

                return monitorEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorControlClientEvent ParseEventCSV. " + excp.Message);
                return null;
            }
        }

        public override string ToCSV()
        {
            try
            {
                string serverEndPointValue = (ServerEndPoint != null) ? ServerEndPoint.ToString() : null;
                string remoteEndPointValue = (RemoteEndPoint != null) ? RemoteEndPoint.ToString() : null;
                string dstEndPointValue = (DestinationEndPoint != null) ? DestinationEndPoint.ToString() : null;

                string csvEvent =
                    SERIALISATION_PREFIX + "|" +
                    ServerType + "|" +
                    EventType + "|" +
                    Created.ToString(SERIALISATION_DATETIME_FORMAT) + "|" +
                    serverEndPointValue + "|" +
                    remoteEndPointValue + "|" +
                    dstEndPointValue + "|" +
                    Username + "|" +
                    Message + END_MESSAGE_DELIMITER;

                return csvEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorControlClientEvent ToCSV. " + excp.Message);
                return null;
            }
        }
              
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPMonitorControlClientEventUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

		
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}
		}

		#endif

		#endregion
	}
}
