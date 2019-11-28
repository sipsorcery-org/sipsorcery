// ============================================================================
// FileName: SIPMonitorEvent.cs
//
// Description:
// Base class for the SIP Monitor events. Super classes will include different
// information when serialised.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 14 Nov 2008  Aaron Clauson   Renamed from ProxyMonitorEvent to SIPMonitorEvent.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public enum SIPMonitorClientTypesEnum
    {
        Console = 1,    // Connection from a human user.
        Machine = 2,    // Connection from an application that will be sent events.
    }

    public class SIPMonitorClientTypes
    {
        public static SIPMonitorClientTypesEnum GetSIPMonitorClientType(string eventTypeName)
        {
            return (SIPMonitorClientTypesEnum)Enum.Parse(typeof(SIPMonitorClientTypesEnum), eventTypeName, true);
        }

        public static SIPMonitorClientTypesEnum GetSIPMonitorClientTypeForId(int eventTypeId)
        {
            return (SIPMonitorClientTypesEnum)Enum.Parse(typeof(SIPMonitorClientTypesEnum), eventTypeId.ToString(), true);
        }
    }

    public enum SIPMonitorServerTypesEnum
    {
        Unknown = 0,
        SIPProxy = 1,
        Registrar = 2,
        NATKeepAlive = 3,
        NotifierAgent = 4,
        Monitor = 5,
        RegisterAgent = 8,
        SIPTransactionLayer = 9,
        UserAgentClient = 10,
        UserAgentServer = 10,
        AppServer = 11,
        Authoriser = 12,
        Notifier = 13,
        NotifierClient = 14,
        RTCC = 15,
    }

    public enum SIPMonitorEventTypesEnum
    {
        NewCall = 1,
        Registrar = 2,
        NATKeepAlive = 3,
        Error = 4,
        NATConnection = 5,
        RegisterSuccess = 6,
        Redirect = 7,
        ProxyForward = 8,
        FullSIPTrace = 9,
        OptionsBounce = 10,
        Timing = 11,
        SIPInvalid = 13,
        Monitor = 24,
        RegisterFail = 25,
        RegistrarTiming = 26,
        ParseSIPMessage = 27,
        SIPMessageArrivalStats = 28,
        HealthCheck = 29,
        RegisterDuplicate = 35,
        PINGResponse = 37,
        ContactRegistered = 38,
        ContactRegisterInProgress = 39,
        ContactRegisterFailed = 40,
        MediatedCall = 41,
        MWI = 42,
        RegistrarCache = 43,
        RegistrarPersistence = 44,
        RegistrarLookup = 45,
        STUNPrimary = 46,
        STUNSecondary = 47,
        UnrecognisedMessage = 48,
        ContactRemoval = 49,
        DNS = 50,
        BadSIPMessage = 51,
        UserSpecificSIPTrace = 52,
        DialPlan = 53,
        BindingExpired = 54,
        BindingRemoval = 55,
        Switch = 57,
        SIPTransaction = 58,
        Warn = 59,
        BindingInProgress = 60,
        BindingFailed = 61,
        ContactRefresh = 62,
        NATKeepAliveRelay = 63,
        CallDispatcher = 64,
        SubscribeQueued = 65,
        SubscribeAuth = 66,
        SubscribeAccept = 67,
        SubscribeFailed = 68,
        SubscribeRenew = 69,
        NotifySent = 70,
    }

    public class SIPMonitorEventTypes
    {
        public static SIPMonitorEventTypesEnum GetProxyEventType(string eventTypeName)
        {
            return (SIPMonitorEventTypesEnum)Enum.Parse(typeof(SIPMonitorEventTypesEnum), eventTypeName, true);
        }

        public static SIPMonitorEventTypesEnum GetProxyEventTypeForId(int eventTypeId)
        {
            return (SIPMonitorEventTypesEnum)Enum.Parse(typeof(SIPMonitorEventTypesEnum), eventTypeId.ToString(), true);
        }
    }

    public class SIPMonitorServerTypes
    {
        public static SIPMonitorServerTypesEnum GetProxyServerType(string serverTypeName)
        {
            return (SIPMonitorServerTypesEnum)Enum.Parse(typeof(SIPMonitorServerTypesEnum), serverTypeName, true);
        }

        public static SIPMonitorServerTypesEnum GetProxyServerTypeForId(int serverTypeId)
        {
            return (SIPMonitorServerTypesEnum)Enum.Parse(typeof(SIPMonitorServerTypesEnum), serverTypeId.ToString(), true);
        }
    }

    public enum SIPMonitorMachineEventTypesEnum
    {
        SIPAccountUpdate = 1,
        SIPAccountDelete = 2,
        SIPRegistrarBindingUpdate = 3,
        SIPRegistrarBindingRemoval = 4,
        SIPRegistrationAgentBindingUpdate = 5,
        SIPRegistrationAgentBindingRemoval = 6,
        SIPDialogueCreated = 7,
        SIPDialogueRemoved = 8,
        SIPDialogueUpdated = 9,
        SIPDialogueTransfer = 10,
        Logout = 11,
    }

    public class SIPMonitorMachineEventTypes
    {
        public static SIPMonitorMachineEventTypesEnum GetMonitorMachineEventType(string eventTypeName)
        {
            return (SIPMonitorMachineEventTypesEnum)Enum.Parse(typeof(SIPMonitorMachineEventTypesEnum), eventTypeName, true);
        }

        public static SIPMonitorMachineEventTypesEnum GetMonitorMachineTypeForId(int eventTypeId)
        {
            return (SIPMonitorMachineEventTypesEnum)Enum.Parse(typeof(SIPMonitorMachineEventTypesEnum), eventTypeId.ToString(), true);
        }
    }

    /// <summary>
    /// Describes the types of events that can be sent by the different SIP Servers to SIP
    /// Monitor clients.
    /// </summary>
    public class SIPMonitorEvent
    {
        public const string SERIALISATION_DATETIME_FORMAT = "yyyy-MM-dd HH:mm:ss.ffffff zzz";
        public const string END_MESSAGE_DELIMITER = "##";

        protected static ILogger logger = Log.Logger;

        protected string m_serialisationPrefix = SIPMonitorConsoleEvent.SERIALISATION_PREFIX;    // Default to a control client event.

        public string SessionID;                        // The ID of the user notification session this event corresponds to.
        public SIPMonitorClientTypesEnum ClientType;
        public string Message;
        public SIPEndPoint RemoteEndPoint;
        public DateTimeOffset Created;
        public string Username;
        public string MonitorServerID;                  // The ID of the monitoring server that received this event. Useful when there are multiple monitoring servers.
        public int ProcessID;                        // The ID of the process that generated this event.

        protected SIPMonitorEvent()
        {
            Created = DateTimeOffset.UtcNow;
        }

        public static SIPMonitorEvent ParseEventCSV(string eventCSV)
        {
            if (eventCSV == null || eventCSV.Trim().Length == 0)
            {
                return null;
            }
            else if (eventCSV.Trim().StartsWith(SIPMonitorConsoleEvent.SERIALISATION_PREFIX))
            {
                return SIPMonitorConsoleEvent.ParseClientControlEventCSV(eventCSV);
            }
            else if (eventCSV.Trim().StartsWith(SIPMonitorMachineEvent.SERIALISATION_PREFIX))
            {
                return SIPMonitorMachineEvent.ParseMachineEventCSV(eventCSV);
            }
            else
            {
                logger.LogWarning("The monitor event prefix of " + eventCSV.Trim().Substring(0, 1) + " was not recognised. " + eventCSV);
                return null;
            }
        }

        public virtual string ToCSV()
        {
            throw new NotImplementedException("SIPMonitorEvent ToCSV (this is a virtual method only, you should be using a sub-class).");
        }
    }
}
