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
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPMonitorEventUnitTest
    {
        [Fact]
        public void SerializeMeassageOnlyProxyEventHeadersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "Test",
                null, null, null, null, SIPCallDirection.None);

            string serialisedEvent = monitorEvent.ToCSV();
            SIPMonitorEvent desMonitorEvent = SIPMonitorConsoleEvent.ParseClientControlEventCSV(serialisedEvent);

            Assert.True(monitorEvent.Message ==  desMonitorEvent.Message, "The event message was not serialised/desrialised correctly.");
            Assert.True(monitorEvent.Created.ToString() == desMonitorEvent.Created.ToString(), "The event created was not serialised/desrialised correctly.");
        }
    }
}
