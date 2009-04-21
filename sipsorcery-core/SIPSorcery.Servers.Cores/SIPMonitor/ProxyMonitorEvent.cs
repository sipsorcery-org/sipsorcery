// ============================================================================
// FileName: ProxyMonitorEvent.cs
//
// Description:
// Logs proxy events so that the proxy can be monitored and events watched/logged.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created.
// ============================================================================

using System;
using System.Collections;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using BlueFace.Sys.Net;
using BlueFace.VoIP.Authentication;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.SIP;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace BlueFace.VoIP.SIPServer
{
    public delegate void ProxyLogDelegate(ProxyMonitorEvent logEvent);

    public enum ProxyServerTypesEnum
	{
		Unknown = 0,
		StatelessProxy = 1,
		Registrar = 2,
		NATKeepAlive = 3,
		NotifierAgent = 4,
		Monitor = 5,
		RegistrarLoadTest = 6,
		StatefulProxy = 7,
        RegisterAgent = 8,
        SIPTransactionLayer = 9
	}
	
	public enum ProxyEventTypesEnum
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
	}

	public class ProxyEventTypes
	{
		public static ProxyEventTypesEnum GetProxyEventType(string eventTypeName)
		{
			return (ProxyEventTypesEnum)Enum.Parse(typeof(ProxyEventTypesEnum), eventTypeName, true);
		}

        public static ProxyEventTypesEnum GetProxyEventTypeForId(int eventTypeId)
        {
            return (ProxyEventTypesEnum)Enum.Parse(typeof(ProxyEventTypesEnum), eventTypeId.ToString(), true);
        }
	}

	public class ProxyServerTypes
	{
		public static ProxyServerTypesEnum GetProxyServerType(string serverTypeName)
		{
			return (ProxyServerTypesEnum)Enum.Parse(typeof(ProxyServerTypesEnum), serverTypeName, true);
		}

        public static ProxyServerTypesEnum GetProxyServerTypeForId(int serverTypeId)
        {
            return (ProxyServerTypesEnum)Enum.Parse(typeof(ProxyServerTypesEnum), serverTypeId.ToString(), true);
        }
	}
	
	public class ProxyMonitorEvent
	{
		public const string SERIALISATION_DATETIME_FORMAT = "dd MMM yyyy HH:mm:ss:fff";
		public const string END_MESSAGE_DELIMITER = "##";
        private const string CALLDIRECTION_IN_STRING = "<-";
        private const string CALLDIRECTION_OUT_STRING = "->";
		
		private static ILog logger = log4net.LogManager.GetLogger("SIPServers");
		
		public ProxyServerTypesEnum ServerType;
		public ProxyEventTypesEnum EventType;
		public string Message;
		//public SIPRequest SIPRequestForEvent;
		//public SIPResponse SIPResponseForEvent;
        public IPEndPoint ServerEndPoint;           // Socket the request was received on by the server.
		public IPEndPoint RemoteEndPoint;
		public IPEndPoint DestinationEndPoint;
		public DateTime Created;
        public string Username;

		private ProxyMonitorEvent()
		{}

        public ProxyMonitorEvent(ProxyServerTypesEnum serverType, ProxyEventTypesEnum eventType, string message, string username)
        {
            ServerType = serverType;
            EventType = eventType;
            Message = message;
            Username = username;
            Created = DateTime.Now;
        }

        public ProxyMonitorEvent(ProxyServerTypesEnum serverType, ProxyEventTypesEnum eventType, string message, IPEndPoint serverSocket, IPEndPoint fromSocket, IPEndPoint toSocket)
        {
            ServerType = serverType;
            EventType = eventType;
            Message = message;
            ServerEndPoint = serverSocket;
            RemoteEndPoint = fromSocket;
            DestinationEndPoint = toSocket;
            Created = DateTime.Now;
        }

        public ProxyMonitorEvent(ProxyServerTypesEnum serverType, ProxyEventTypesEnum eventType, string message, SIPRequest sipRequest, SIPResponse sipResponse, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPCallDirection callDirection)
        {
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

		public static ProxyMonitorEvent ParseEventCSV(string eventCSV)
		{
			ProxyMonitorEvent monitorEvent = new ProxyMonitorEvent();
			
			if(eventCSV.IndexOf(END_MESSAGE_DELIMITER) != -1)
			{
				eventCSV.Remove(eventCSV.Length-2, 2);
			}

			string[] eventFields = eventCSV.Split(new char[] {'|'});

			//logger.Debug(eventFields[0] + "|" + eventFields[1] + "|" + eventFields[2] + ".");

			monitorEvent.ServerType = ProxyServerTypes.GetProxyServerType(eventFields[0]);
			monitorEvent.EventType = ProxyEventTypes.GetProxyEventType(eventFields[1]);
			monitorEvent.Created = DateTime.ParseExact(eventFields[2], SERIALISATION_DATETIME_FORMAT, CultureInfo.InvariantCulture);
			//Console.WriteLine("Created=" + monitorEvent.Created.ToString(SERIALISATION_DATETIME_FORMAT));

            string serverEndPointStr = eventFields[3];
            if (serverEndPointStr != null && serverEndPointStr.Trim().Length > 0)
            {
                monitorEvent.ServerEndPoint = IPSocket.ParseSocketString(serverEndPointStr);
            }

			string remoteEndPointStr = eventFields[4];
			if(remoteEndPointStr != null && remoteEndPointStr.Trim().Length > 0)
			{
				monitorEvent.RemoteEndPoint = IPSocket.ParseSocketString(remoteEndPointStr);
			}

			string dstEndPointStr = eventFields[5];
			if(dstEndPointStr != null && dstEndPointStr.Trim().Length > 0)
			{
				monitorEvent.DestinationEndPoint = IPSocket.ParseSocketString(dstEndPointStr);
			}

            monitorEvent.Username = eventFields[6];
            monitorEvent.Message = eventFields[7].Trim('#');

			return monitorEvent;
		}

		public string ToCSV()
		{
			try
			{
                string serverEndPointValue = (ServerEndPoint != null) ? IPSocket.GetSocketString(ServerEndPoint) : null;
				string remoteEndPointValue = (RemoteEndPoint != null) ? IPSocket.GetSocketString(RemoteEndPoint) : null;
				string dstEndPointValue = (DestinationEndPoint != null) ? IPSocket.GetSocketString(DestinationEndPoint) : null;
				//string sipRequestValue = (SIPRequestForEvent != null) ? SIPRequestForEvent.ToString() : null;
				//string sipResponseValue = (SIPResponseForEvent != null) ? SIPResponseForEvent.ToString() : null;
			
				// Can only have one of a request or response with a csv event, requests take priority.
				//string sipMessageValue = (sipRequestValue != null) ? sipRequestValue : sipResponseValue;

				string csvEvent = 
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
			catch(Exception excp)
			{
				logger.Error("Exception ProxyMonitorEvent ToCSV. " + excp.Message);
				return null;
			}
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class ProxyMonitorEventUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

		
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			/*
			[Test]
			public void SerializeMeassageOnlyProxyEventHeadersTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				ProxyMonitorEvent monitorEvent = new ProxyMonitorEvent(ProxyEventTypesEnum.FullSIPTrace, "Test", null, null, null, null);

				MemoryStream memoryStream = new MemoryStream();

				BinaryFormatter binaryFormatter = new BinaryFormatter();
				binaryFormatter.Serialize(memoryStream, monitorEvent);

				memoryStream.Position = 0;
				ProxyMonitorEvent desMonitorEvent = (ProxyMonitorEvent)binaryFormatter.Deserialize(memoryStream);

				Assert.AreEqual(monitorEvent.Message, desMonitorEvent.Message, "The event message was not serialised/desrialised correctly.");
				Assert.AreEqual(monitorEvent.Created, desMonitorEvent.Created, "The event created was not serialised/desrialised correctly.");
			}
			*/
		}

		#endif

		#endregion
	}
}
