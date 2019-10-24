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
// 14 Nov 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Globalization;
using System.Net;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

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
                logger.LogError("Exception SIPMonitorMachineEvent ParseEventCSV. " + excp.Message);
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
                logger.LogError("Exception SIPMonitorMachineEvent ToCSV. " + excp.Message);
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
                logger.LogError("Exception SIPMonitorMachineEvent ToAnonymousCSV. " + excp.Message);
                return null;
            }
        }
    }
}
