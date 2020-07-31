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

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPMonitorEventUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPMonitorEventUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void SerializeMeassageOnlyProxyEventHeadersTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPMonitorEvent monitorEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.FullSIPTrace, "Test",
                null, null, null, null, SIPCallDirection.None);

            string serialisedEvent = monitorEvent.ToCSV();
            SIPMonitorEvent desMonitorEvent = SIPMonitorConsoleEvent.ParseClientControlEventCSV(serialisedEvent);

            Assert.True(monitorEvent.Message == desMonitorEvent.Message, "The event message was not serialised/deserialised correctly.");
            Assert.True(monitorEvent.Created.ToString() == desMonitorEvent.Created.ToString(), "The event created was not serialised/deserialised correctly.");
        }
    }
}
