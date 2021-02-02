//-----------------------------------------------------------------------------
// Filename: SIPUDPChannelUnitTest.cs
//
// Description: Unit tests for the SIPUDPChannel class.
//
// History:
// 13 Oct 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;


namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUDPChannelUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPUDPChannelUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that creating a new SIP UDP channel works correctly.
        /// </summary>
        [Fact]
        public void CreateChannelUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var udpChan = new SIPUDPChannel(IPAddress.Any, 0);

            logger.LogDebug($"Listening end point {udpChan.ListeningSIPEndPoint}.");

            udpChan.Close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that two SIP UDP channels can communicate.
        /// </summary>
        [Fact]
        public async void InterChannelCommsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var udpChan1 = new SIPUDPChannel(IPAddress.Any, 0);
            logger.LogDebug($"Listening end point {udpChan1.ListeningSIPEndPoint}.");
            var udpChan2 = new SIPUDPChannel(IPAddress.Any, 0);
            logger.LogDebug($"Listening end point {udpChan2.ListeningSIPEndPoint}.");

            TaskCompletionSource<bool> gotMessage = new TaskCompletionSource<bool>();
            SIPEndPoint receivedFromEP = null;
            SIPEndPoint receivedOnEP = null;
            udpChan2.SIPMessageReceived = (SIPChannel sipChannel, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer) =>
            {
                logger.LogDebug($"SIP message received from {remoteEndPoint}.");
                logger.LogDebug($"SIP message received on {localSIPEndPoint}.");
                logger.LogDebug(Encoding.UTF8.GetString(buffer));

                receivedFromEP = remoteEndPoint;
                receivedOnEP = localSIPEndPoint;
                gotMessage.SetResult(true);
                return Task.CompletedTask;
            };

            var dstEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.Loopback, udpChan2.Port);
            var optionsReq = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, new SIPURI(SIPSchemesEnum.sip, dstEndPoint));

            logger.LogDebug($"Attempting to send OPTIONS request to {dstEndPoint}.");

            // Give sockets a chance to start up.
            //await Task.Delay(500);

            await udpChan1.SendAsync(dstEndPoint, Encoding.UTF8.GetBytes(optionsReq.ToString()), false, null);

            bool res = gotMessage.Task.Wait(1000);

            Assert.True(res);
            Assert.NotNull(receivedFromEP);
            Assert.NotNull(receivedOnEP);
            Assert.Equal(IPAddress.Loopback, receivedFromEP.Address);
            Assert.Equal(IPAddress.Any, receivedOnEP.Address);

            udpChan1.Close();
            udpChan2.Close();

            logger.LogDebug("-----------------------------------------");
        }
    }
}
