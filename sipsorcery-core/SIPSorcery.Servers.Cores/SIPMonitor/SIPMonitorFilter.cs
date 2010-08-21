// ============================================================================
// FileName: SIPMonitorFilter.cs
//
// Description:
// Logs proxy events so that the proxy can be monitored and events watched/logged.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 May 2006	Aaron Clauson	Created.
// 14 Nov 2008  Aaron Clauson   Renamed from ProxyMonitorFilter to SIPonitorFilter.
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
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	
	public class SIPMonitorFilter
	{
		public const string WILDCARD = "*";
        public const string DEFAULT_REGEX = ".*";
        public const int DEFAULT_FILEDURATION_MINUTES = 1440;   // 1 Day.
        public const int MAX_FILEDURATION_MINUTES = 4320;       // 3 Days.
		
		public const string EVENTTYPE_FILTER_KEY = "event";
		public const string IPADDRESS_FILTER_KEY = "ipaddr";
        public const string IPADDRESSLONG_FILTER_KEY = "ipaddress";
		public const string USERNAME_FILTER_KEY = "user";
		public const string SIPREQUEST_FILTER_KEY = "request";
		public const string SERVERADDRESS_FILTER_KEY = "ipserver";
		public const string SERVERTYPE_FILTER_KEY = "server";
        public const string REGEX_FILTER_KEY = "regex";

		public const string EVENTTYPE_FULL_VALUE = "full";                  // Full SIP messages except ones to and from loopback IP's.
        public const string EVENTTYPE_FULLINTERNAL_VALUE = "fullinternal";  // Full SIP messages including ones between any internal server agents on the loopback IP address.
		public const string EVENTTYPE_SYSTEM_VALUE = "system";
        public const string EVENTTYPE_TROUBLE_VALUE = "trouble";
    	public const string SIPREQUEST_INVITE_VALUE = "invite";
		public const string SIPREQUEST_REGISTER_VALUE = "register";

        public const string FILELOG_REQUEST_KEY = "file";
        public const string FILELOG_MINUTESDURATION_KEY = "duration";

		public string IPAddress = WILDCARD;
        public string ServerIPAddress = WILDCARD;
        public string Username = WILDCARD;
        public string SIPRequestFilter = WILDCARD;
        public string EventFilterDescr = WILDCARD;
		public int EventTypeId = 0;
		public int ServerTypeId = 0;
        public string RegexFilter = DEFAULT_REGEX;
        public string FileLogname = null;
        public int FileLogDuration = DEFAULT_FILEDURATION_MINUTES;

        public SIPMonitorFilter(string filter)
		{
			if(!filter.IsNullOrBlank())
			{
				string[] filterItems = Regex.Split(filter, @"\s+and\s+");

                if (filterItems != null && filterItems.Length > 0) {
                    foreach (string filterItem in filterItems) {

                        string[] filterPair = filterItem.Trim().Split(' ');

                        if (filterPair != null && filterPair.Length == 2) {
                            string filterName = filterPair[0];
                            string filterValue = filterPair[1];

                            if (filterName == EVENTTYPE_FILTER_KEY) {
                                if (filterValue != null && Regex.Match(filterValue, @"\d{1,2}").Success) {
                                    EventTypeId = Convert.ToInt32(filterValue);
                                }
                                else {
                                    EventFilterDescr = filterValue;
                                }
                            }
                            else if (filterName == SERVERADDRESS_FILTER_KEY) {
                                ServerIPAddress = filterValue;
                            }
                            else if (filterName == IPADDRESS_FILTER_KEY || filterName == IPADDRESSLONG_FILTER_KEY) {
                                IPAddress = filterValue;
                            }
                            else if (filterName == USERNAME_FILTER_KEY) {
                                Username = filterValue;
                            }
                            else if (filterName == SIPREQUEST_FILTER_KEY) {
                                SIPRequestFilter = filterValue;
                            }
                            else if (filterName == SERVERTYPE_FILTER_KEY) {
                                if (filterValue != null && Regex.Match(filterValue, @"\d{1,2}").Success) {
                                    ServerTypeId = Convert.ToInt32(filterValue);
                                }
                            }
                            else if (filterName == FILELOG_REQUEST_KEY) {
                                if (filterValue != null) {
                                    FileLogname = filterValue;
                                }
                            }
                            else if (filterName == FILELOG_MINUTESDURATION_KEY) {
                                if (filterValue != null && Regex.Match(filterValue, @"\d").Success) {
                                    FileLogDuration = Convert.ToInt32(filterValue);

                                    if (FileLogDuration > MAX_FILEDURATION_MINUTES) {
                                        FileLogDuration = MAX_FILEDURATION_MINUTES;
                                    }
                                }
                            }
                            else if (filterName == REGEX_FILTER_KEY) {
                                if (filterValue != null) {
                                    RegexFilter = filterValue;
                                }
                            }
                            else {
                                throw new ApplicationException("Filter " + filterName + " not recgonised.");
                            }
                        }
                        else {
                            throw new ApplicationException("Invalid item in filter: " + filterItem.Trim() + ".");
                        }
                    }
                }
                else {
                    throw new ApplicationException("Invalid filter format: " + filter + ".");
                }
			}
		}

        public bool ShowEvent(SIPMonitorEventTypesEnum eventType, SIPEndPoint serverEndPoint)
		{
			if(EventTypeId != 0)
			{
				if((int)eventType == EventTypeId)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else
			{
                if (EventFilterDescr == EVENTTYPE_FULLINTERNAL_VALUE && (eventType == SIPMonitorEventTypesEnum.FullSIPTrace || eventType == SIPMonitorEventTypesEnum.BadSIPMessage)) {
                    return true;
                }
                else if (EventFilterDescr == EVENTTYPE_FULL_VALUE && (eventType == SIPMonitorEventTypesEnum.FullSIPTrace || eventType == SIPMonitorEventTypesEnum.BadSIPMessage)) {
                    if (serverEndPoint != null && serverEndPoint.SocketEndPoint.Address.ToString() == "127.0.0.1") {
                        return false;
                    }
                    else {
                        return true;
                    }
                }
                else if (EventFilterDescr == EVENTTYPE_SYSTEM_VALUE) {
                    // Assume EVENTTYPE_ALL_VALUE.
                    if (eventType == SIPMonitorEventTypesEnum.Monitor ||
                        eventType == SIPMonitorEventTypesEnum.HealthCheck ||
                        eventType == SIPMonitorEventTypesEnum.ParseSIPMessage ||
                        eventType == SIPMonitorEventTypesEnum.SIPMessageArrivalStats) {
                        return true;
                    }
                    else {
                        return false;
                    }
                }
                else if (EventFilterDescr == EVENTTYPE_TROUBLE_VALUE) {
                    // Assume EVENTTYPE_ALL_VALUE.
                    if (eventType == SIPMonitorEventTypesEnum.Error ||
                        eventType == SIPMonitorEventTypesEnum.Warn ||
                        eventType == SIPMonitorEventTypesEnum.BadSIPMessage) {
                        return true;
                    }
                    else {
                        return false;
                    }
                }
                else {
                    // Assume EVENTTYPE_ALL_VALUE if nothing has been specified by the user, however do not display the full SIP trace messages.
                    if (EventFilterDescr == WILDCARD && eventType != SIPMonitorEventTypesEnum.FullSIPTrace && eventType != SIPMonitorEventTypesEnum.UserSpecificSIPTrace) {
                        return true;
                    }
                    else {
                        return false;
                    }
                }
			}
		}

		public bool ShowServer(SIPMonitorServerTypesEnum eventServer)
		{
			if(ServerTypeId != 0)
			{
				if((int)eventServer == ServerTypeId)
				{
					return true;
				}
				else
				{
					return false;
				}
			}

			return true;
		}

        public bool ShowServerIPAddress(string serverIPAddress)
        {
            if (ServerIPAddress == WILDCARD || serverIPAddress == null)
            {
                return true;
            }
            else
            {
                return serverIPAddress.StartsWith(ServerIPAddress);
            }
        }

		public bool ShowIPAddress(string ipAddress)
		{
            if (IPAddress == WILDCARD)
			{
				return true;
			}
            else if (ipAddress == null || ipAddress.Trim().Length == 0)
            {
                return false;
            }
            else
            {
                return ipAddress.Contains(IPAddress);
            }
		}

		public bool ShowUsername(string username)
		{
            if (Username == WILDCARD)
			{
				return true;
			}
            else if (username == null)
            {
                return false;
            }
            else
            {
                return username.ToUpper() == Username.ToUpper();
            }
		}

        public bool ShowRequest(SIPMethodsEnum sipMethod)
		{
            if (SIPRequestFilter == WILDCARD)
			{
				return true;
			}
			else
			{
                if (SIPRequestFilter == SIPREQUEST_INVITE_VALUE)
				{
                    return (sipMethod == SIPMethodsEnum.INVITE ||
                        sipMethod == SIPMethodsEnum.ACK ||
                        sipMethod == SIPMethodsEnum.BYE ||
                        sipMethod == SIPMethodsEnum.CANCEL);
				}
                else if (SIPRequestFilter == SIPREQUEST_REGISTER_VALUE)
				{
                    return (sipMethod == SIPMethodsEnum.REGISTER);
				}

				return false;
			}
        }

        public bool ShowRegex(string message)
        {
            if (message == null || RegexFilter == DEFAULT_REGEX)
            {
                return true;
            }
            else
            {
                return Regex.Match(message, RegexFilter).Success;
            }
        }

        public string GetFilterDescription()
        {
            string eventStr = (EventTypeId == 0) ? EventFilterDescr : SIPMonitorEventTypes.GetProxyEventTypeForId(EventTypeId).ToString();
            string serverStr = (ServerTypeId == 0) ? WILDCARD : SIPMonitorServerTypes.GetProxyServerTypeForId(ServerTypeId).ToString();

            string filerDescription = 
                "ipaddress=" + IPAddress + ", user=" + Username + ", event=" + eventStr + ", request=" + SIPRequestFilter + ", serveripaddress=" + ServerIPAddress + ", server=" + serverStr + ", regex=" + RegexFilter + ".";

            return filerDescription;
        }

        /// <summary>
        /// Rules for displaying events.
        ///  1. The event type is checked to see if it matches. If no event type has been specified than all events EXCEPT FullSIPTrace are
        ///     matched,
        ///  2. If the event type is FullSIPTrace then the messages can be filtered on the request type. If the request type is not set all
        ///     SIP trace messages are matched otherwise only those pertaining to the request type specified,
        ///  3. The server type is checked, if it's not set all events are matched,
        ///  4. If the event has matched up until this point a decision is now made as to whether to display or reject it:
        ///     a. If the IPAddress filter is set is checked, if it matches the event is displayed otherwise it's rejected,
        ///     b. If the username AND server IP AND request type AND regex filters all match the vent is displayed otherwise rejected. 
        /// </summary>
        /// <param name="proxyEvent"></param>
        /// <returns></returns>
        public bool ShowSIPMonitorEvent(SIPMonitorEvent proxyEvent)
        {
            string serverAddress = (proxyEvent.ServerEndPoint != null) ? proxyEvent.ServerEndPoint.SocketEndPoint.Address.ToString() : null;
            string remoteIPAddress = (proxyEvent.RemoteEndPoint != null) ? proxyEvent.RemoteEndPoint.SocketEndPoint.Address.ToString() : null;
            string dstIPAddress = (proxyEvent.DestinationEndPoint != null) ? proxyEvent.DestinationEndPoint.SocketEndPoint.Address.ToString() : null;
            SIPMethodsEnum sipMethod = SIPMethodsEnum.NONE;

            if (SIPRequestFilter != WILDCARD && proxyEvent.Message != null && proxyEvent.EventType == SIPMonitorEventTypesEnum.FullSIPTrace)
            {
                if(ShowEvent(proxyEvent.EventType, proxyEvent.ServerEndPoint))
                {
                    if (SIPRequestFilter == SIPREQUEST_INVITE_VALUE)
                    {
                        // Do a regex to pick out ACK's, BYE's , CANCEL's and INVITES.
                        if (Regex.Match(proxyEvent.Message, "(ACK|BYE|CANCEL|INVITE) +?sips?:", RegexOptions.IgnoreCase).Success ||
                            Regex.Match(proxyEvent.Message, @"CSeq: \d+ (ACK|BYE|CANCEL|INVITE)(\r|\n)", RegexOptions.IgnoreCase).Success)
                        {
                            return ShowRegex(proxyEvent.Message);
                        }
                        else
                        {
                            string reqPattern = SIPRequestFilter + " +?sips?:";
                            string respPattern = @"CSeq: \d+ " + SIPRequestFilter;

                            if (Regex.Match(proxyEvent.Message, reqPattern, RegexOptions.IgnoreCase).Success ||
                                Regex.Match(proxyEvent.Message, respPattern, RegexOptions.IgnoreCase).Success)
                            {
                                return ShowRegex(proxyEvent.Message);
                            }
                        }
                    }

                    return false;
                }
            }

            if (ShowEvent(proxyEvent.EventType, proxyEvent.ServerEndPoint) && ShowServer(proxyEvent.ServerType))
            {
                if (IPAddress != WILDCARD)
                {
                    if (ShowIPAddress(remoteIPAddress))
                    {
                        return true;
                    }
                    else if (ShowIPAddress(dstIPAddress))
                    {
                         return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                bool showUsername = ShowUsername(proxyEvent.Username);
                bool showServerIP = ShowServerIPAddress(serverAddress);
                bool showRequest = ShowRequest(sipMethod);
                bool showRegex = ShowRegex(proxyEvent.Message);

                if (showUsername && showServerIP && showRequest && showRegex)
                {
                    return true;
                }
            }

            return false;
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
		public class ProxyMonitorFilterUnitTest
		{
            private static string m_CRLF = SIPConstants.CRLF;
            
            [TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void CreateFilterUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				SIPMonitorFilter filter = new SIPMonitorFilter("user test and event full");
                Console.WriteLine(filter.GetFilterDescription());

				Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
				Assert.AreEqual(filter.Username, "test", "The filter username was not correctly set.");
                Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
			}

            [Test]
            public void RequestRejectionUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("user test and event full and request invite ");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.Username, "test", "The filter username was not correctly set.");
                Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
                Assert.AreEqual(filter.SIPRequestFilter, "invite", "The sip request filter was not correctly set.");
                
                string registerRequest =
                     "REGISTER sip:213.200.94.181 SIP/2.0" + m_CRLF +
                    "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                    "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                    "From: aaronxten <sip:aaronxten@213.200.94.181>;tag=774d2561" + m_CRLF +
                    "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                    "CSeq: 2 REGISTER" + m_CRLF +
                    "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                    "Max-Forwards: 69" + m_CRLF +
                    "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                    "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, registerRequest, "test");
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsFalse(showEventResult, "The filter should not have shown this event.");
            }

            [Test]
            public void RequestAcceptUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("user test and event full and request invite ");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.Username, "test", "The filter username was not correctly set.");
                Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
                Assert.AreEqual(filter.SIPRequestFilter, "invite", "The sip request filter was not correctly set.");

                string registerRequest =
                     "INVITE sip:213.200.94.181 SIP/2.0" + m_CRLF +
                    "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                    "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                    "From: test <sip:test@213.200.94.181>;tag=774d2561" + m_CRLF +
                    "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                    "CSeq: 2 REGISTER" + m_CRLF +
                    "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                    "Max-Forwards: 69" + m_CRLF +
                    "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                    "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, registerRequest, "test");
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsTrue(showEventResult, "The filter should have shown this event.");
            }

            [Test]
            public void ShowIPAddressUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("ipaddress 10.0.0.1 and event full");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.IPAddress, "10.0.0.1", "The filter ip address was not correctly set.");

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, null, SIPEndPoint.ParseSIPEndPoint("10.0.0.1"));
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsTrue(showEventResult, "The filter should have shown this event.");
            }

            [Test]
            public void ShowInternalIPAddressUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("ipaddress 127.0.0.1 and event fullinternal");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.IPAddress, "127.0.0.1", "The filter ip address was not correctly set.");

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, null, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsTrue(showEventResult, "The filter should have shown this event.");
            }

            [Test]
            public void BlockIPAddressUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("ipaddress 127.0.0.1 and event full");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.IPAddress, "127.0.0.1", "The filter ip address was not correctly set.");

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, SIPEndPoint.ParseSIPEndPoint("127.0.0.2"), null);
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsFalse(showEventResult, "The filter should not have shown this event.");
            }

            [Test]
            public void FullTraceSIPSwitchUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("event full and regex :test@");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                //Assert.AreEqual(filter.Username, "test", "The filter username was not correctly set.");
                Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
                Assert.AreEqual(filter.RegexFilter, ":test@", "The regex was not correctly set.");

                string inviteRequest =
                    "INVITE sip:213.200.94.181 SIP/2.0" + m_CRLF +
                    "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                    "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                    "From: test <sip:test@213.200.94.181>;tag=774d2561" + m_CRLF +
                    "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                    "CSeq: 2 REGISTER" + m_CRLF +
                    "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                    "Max-Forwards: 69" + m_CRLF +
                    "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                    "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

                SIPMonitorEvent monitorEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, inviteRequest, null);
                bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

                Assert.IsTrue(showEventResult, "The filter should have shown this event.");
            }

            [Test]
            public void UsernameWithAndFilterUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPMonitorFilter filter = new SIPMonitorFilter("user testandtest   and  event full");
                Console.WriteLine(filter.GetFilterDescription());

                Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
                Assert.AreEqual(filter.Username, "testandtest", "The filter username was not correctly set.");
                Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
            }
		}

		#endif

		#endregion
	}
}
