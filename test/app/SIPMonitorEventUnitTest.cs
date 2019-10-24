//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.App.UnitTests
{
    [TestClass]
    public class SIPMonitorEventUnitTest
    {
        [TestMethod]
        public void SerializeMeassageOnlyProxyEventHeadersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "Test",
                null, null, null, null, SIPCallDirection.None);

            string serialisedEvent = monitorEvent.ToCSV();
            SIPMonitorEvent desMonitorEvent = SIPMonitorConsoleEvent.ParseClientControlEventCSV(serialisedEvent);

            Assert.AreEqual(monitorEvent.Message, desMonitorEvent.Message, "The event message was not serialised/desrialised correctly.");
            Assert.AreEqual(monitorEvent.Created.ToString(), desMonitorEvent.Created.ToString(), "The event created was not serialised/desrialised correctly.");
        }
    }
}
