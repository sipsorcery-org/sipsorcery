using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.App.UnitTests
{
    [TestClass]
    public class SIPMonitorFilterUnitTest
    {
        private static string m_CRLF = SIPConstants.CRLF;

        [TestMethod]
        public void CreateFilterUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorFilter filter = new SIPMonitorFilter("user test and event full");
            Console.WriteLine(filter.GetFilterDescription());

            Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
            Assert.AreEqual(filter.Username, "test", "The filter username was not correctly set.");
            Assert.AreEqual(filter.EventFilterDescr, "full", "The filter event full was not correctly set.");
        }

        [TestMethod]
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

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, registerRequest, "test");
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsFalse(showEventResult, "The filter should not have shown this event.");
        }

        [TestMethod]
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

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, registerRequest, "test");
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsTrue(showEventResult, "The filter should have shown this event.");
        }

        [TestMethod]
        public void ShowIPAddressUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorFilter filter = new SIPMonitorFilter($"ipaddress 10.0.0.1 and event full and user {SIPMonitorFilter.WILDCARD}");
            Console.WriteLine(filter.GetFilterDescription());

            Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
            Assert.AreEqual(filter.IPAddress, "10.0.0.1", "The filter ip address was not correctly set.");

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, null, SIPEndPoint.ParseSIPEndPoint("10.0.0.1"));
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsTrue(showEventResult, "The filter should have shown this event.");
        }

        [TestMethod]
        public void ShowInternalIPAddressUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorFilter filter = new SIPMonitorFilter($"ipaddress 127.0.0.1 and event full and user {SIPMonitorFilter.WILDCARD}");
            Console.WriteLine(filter.GetFilterDescription());

            Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
            Assert.AreEqual(filter.IPAddress, "127.0.0.1", "The filter ip address was not correctly set.");

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, null, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsTrue(showEventResult, "The filter should have shown this event.");
        }

        [TestMethod]
        public void BlockIPAddressUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorFilter filter = new SIPMonitorFilter("ipaddress 127.0.0.1 and event full");
            Console.WriteLine(filter.GetFilterDescription());

            Assert.IsTrue(filter != null, "The filter was not correctly instantiated.");
            Assert.AreEqual(filter.IPAddress, "127.0.0.1", "The filter ip address was not correctly set.");

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "blah blah", String.Empty, SIPEndPoint.ParseSIPEndPoint("127.0.0.2"), null);
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsFalse(showEventResult, "The filter should not have shown this event.");
        }

        [TestMethod]
        public void FullTraceSIPSwitchUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorFilter filter = new SIPMonitorFilter($"event full and regex :test@ and user {SIPMonitorFilter.WILDCARD}");
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

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, inviteRequest, null);
            bool showEventResult = filter.ShowSIPMonitorEvent(monitorEvent);

            Assert.IsTrue(showEventResult, "The filter should have shown this event.");
        }

        [TestMethod]
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
}
