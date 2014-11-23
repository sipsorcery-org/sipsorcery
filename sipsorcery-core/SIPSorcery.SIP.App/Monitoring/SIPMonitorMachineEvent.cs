// ============================================================================
// FileName: SIPMonitorMachineEvent.cs
//
// Description:
// Describes monitoring events that are for machine notifications to initiate actions such as
// updating a user interface. The events will not typically contain useful information for a human
// viewer.
//
// Author(s):
// Aaron Clauson
//
// History:
// 14 Nov 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Xml.Linq;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Describes monitoring events that are for machine notifications to initiate actions such as
    /// updating a user interface. The events will not typically contain useful information for a human
    /// viewer.
    /// </summary>
    public class SIPMonitorMachineEvent : SIPMonitorEvent
    {
        public const string SERIALISATION_PREFIX = "2";             // Prefix appended to the front of a serialised event to identify the type. 

        public SIPMonitorMachineEventTypesEnum MachineEventType;
        public string ResourceID;                                   // For a dialog SIP Event this will be the dialogue ID, for a presence SIP event this will be the SIP Account ID.
        public SIPURI ResourceURI;                                  // If applicable the URI of the resource that generated this event.

        private SIPMonitorMachineEvent()
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.Machine;
        }

        public SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum machineEventType, string owner, SIPEndPoint remoteEndPoint, string message)
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;

            RemoteEndPoint = remoteEndPoint;
            ClientType = SIPMonitorClientTypesEnum.Machine;
            Username = owner;
            MachineEventType = machineEventType;
            Message = message;
        }

        public SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum machineEventType, string owner, string resourceID, SIPURI resourceURI)
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;

            ClientType = SIPMonitorClientTypesEnum.Machine;
            Username = owner;
            MachineEventType = machineEventType;
            ResourceID = resourceID;
            ResourceURI = resourceURI;
        }

        public static SIPMonitorMachineEvent ParseMachineEventCSV(string eventCSV)
        {
            try
            {
                SIPMonitorMachineEvent machineEvent = new SIPMonitorMachineEvent();

                if (eventCSV.IndexOf(END_MESSAGE_DELIMITER) != -1)
                {
                    eventCSV.Remove(eventCSV.Length - 2, 2);
                }

                string[] eventFields = eventCSV.Split(new char[] { '|' });

                machineEvent.SessionID = eventFields[1];
                machineEvent.MonitorServerID = eventFields[2];
                machineEvent.MachineEventType = SIPMonitorMachineEventTypes.GetMonitorMachineTypeForId(Convert.ToInt32(eventFields[3]));
                machineEvent.Created = DateTimeOffset.ParseExact(eventFields[4], SERIALISATION_DATETIME_FORMAT, CultureInfo.InvariantCulture);
                machineEvent.Username = eventFields[5];
                machineEvent.RemoteEndPoint = SIPEndPoint.ParseSIPEndPoint(eventFields[6]);
                machineEvent.Message = eventFields[7];
                machineEvent.ResourceID = eventFields[8];
                string resourceURI = eventFields[9].Trim('#');

                if (!resourceURI.IsNullOrBlank())
                {
                    machineEvent.ResourceURI = SIPURI.ParseSIPURIRelaxed(resourceURI);
                }

                return machineEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMachineEvent ParseEventCSV. " + excp.Message);
                return null;
            }
        }

        public override string ToCSV()
        {
            try
            {
                int machineEventTypeId = (int)MachineEventType;
                string remoteSocket = (RemoteEndPoint != null) ? RemoteEndPoint.ToString() : null;
                string resourceURIStr = (ResourceURI != null) ? ResourceURI.ToString() : null;

                string csvEvent =
                    SERIALISATION_PREFIX + "|" +
                    SessionID + "|" +
                    MonitorServerID + "|" +
                    machineEventTypeId + "|" +
                    Created.ToString(SERIALISATION_DATETIME_FORMAT) + "|" +
                    Username + "|" +
                    remoteSocket + "|" +
                    Message + "|" +
                    ResourceID + "|" +
                    resourceURIStr
                    + END_MESSAGE_DELIMITER;

                return csvEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMachineEvent ToCSV. " + excp.Message);
                return null;
            }
        }

        public string ToAnonymousCSV()
        {
            try
            {
                int machineEventTypeId = (int)MachineEventType;
                string remoteSocket = null;

                if (RemoteEndPoint != null)
                {
                    // This is the equivalent of applying a /20 mask to the IP address to obscure the bottom 12 bits of the address.
                    byte[] addressBytes = RemoteEndPoint.Address.GetAddressBytes();
                    addressBytes[3] = 0;
                    addressBytes[2] = (byte)(addressBytes[2] & 0xf0);
                    IPAddress anonymisedIPAddress = new IPAddress(addressBytes);
                    remoteSocket = (RemoteEndPoint != null) ? anonymisedIPAddress.ToString() + ":" + RemoteEndPoint.Port : null;
                }

                string csvEvent =
                    SERIALISATION_PREFIX + "|" +
                    SessionID + "|" +
                    MonitorServerID + "|" +
                    machineEventTypeId + "|" +
                    Created.ToString(SERIALISATION_DATETIME_FORMAT) + "|" +
                    "|" +
                    remoteSocket + "|" +
                    "|" +
                    "|" +
                    END_MESSAGE_DELIMITER;

                return csvEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorMachineEvent ToAnonymousCSV. " + excp.Message);
                return null;
            }
        }

        #region Unit testing.

#if UNITTEST
	
		[TestFixture]
		public class SIPMonitorMachineEventUnitTest
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
