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

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var udpChan = new SIPUDPChannel(IPAddress.Any, 0);

            logger.LogDebug("Listening end point {ListeningSIPEndPoint}.", udpChan.ListeningSIPEndPoint);

            udpChan.Close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that two SIP UDP channels can communicate.
        /// </summary>
        [Fact]
        public async Task InterChannelCommsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var udpChan1 = new SIPUDPChannel(IPAddress.Any, 0);
            logger.LogDebug("Listening end point {ListeningSIPEndPoint}.", udpChan1.ListeningSIPEndPoint);
            var udpChan2 = new SIPUDPChannel(IPAddress.Any, 0);
            logger.LogDebug("Listening end point {ListeningSIPEndPoint}.", udpChan2.ListeningSIPEndPoint);

            TaskCompletionSource<bool> gotMessage = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SIPEndPoint receivedFromEP = null;
            SIPEndPoint receivedOnEP = null;
            udpChan2.SIPMessageReceived = (SIPChannel sipChannel, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer) =>
            {
                logger.LogDebug("SIP message received from {remoteEndPoint}.", remoteEndPoint);
                logger.LogDebug("SIP message received on {localSIPEndPoint}.", localSIPEndPoint);
                logger.LogDebug("{Buffer}", Encoding.UTF8.GetString(buffer));

                receivedFromEP = remoteEndPoint;
                receivedOnEP = localSIPEndPoint;
                // TrySet because the send is retried and more than one copy of the request may arrive.
                gotMessage.TrySetResult(true);
                return Task.CompletedTask;
            };

            var dstEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, IPAddress.Loopback, udpChan2.Port);
            var optionsReq = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, new SIPURI(SIPSchemesEnum.sip, dstEndPoint));

            logger.LogDebug("Attempting to send OPTIONS request to {dstEndPoint}.", dstEndPoint);

            // UDP delivery is not guaranteed, even on loopback a datagram can be dropped under
            // buffer pressure, and a single 1 second wait is not reliable on a loaded CI agent.
            // Retry the send periodically until the message arrives or an overall deadline expires.
            bool received = false;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                await udpChan1.SendAsync(dstEndPoint, Encoding.UTF8.GetBytes(optionsReq.ToString()), false, null);

                var completed = await Task.WhenAny(gotMessage.Task, Task.Delay(500));
                if (completed == gotMessage.Task)
                {
                    received = true;
                    break;
                }

                logger.LogDebug("No message received after {Elapsed:0.##}s, resending.", deadline.Elapsed.TotalSeconds);
            }

            Assert.True(received, "Timeout waiting for message to be received.");

            logger.LogDebug("Message received successfully.");

            Assert.True(await gotMessage.Task);
            Assert.NotNull(receivedFromEP);
            Assert.NotNull(receivedOnEP);
            Assert.Equal(IPAddress.Loopback, receivedFromEP.Address);
            Assert.Equal(IPAddress.Any, receivedOnEP.Address);

            udpChan1.Close();
            udpChan2.Close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that getting a default contact URI for a SIP channel works correctly.
        /// </summary>
        [Fact]
        public void GetDefaultContactURIUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var udpChan = new SIPUDPChannel(IPAddress.Any, 0);

            logger.LogDebug("Listening end point {ListeningSIPEndPoint}.", udpChan.ListeningSIPEndPoint);

            var contactURI = udpChan.GetContactURI(SIPSchemesEnum.sip, new SIPEndPoint(udpChan.SIPProtocol, SIPChannel.InternetDefaultAddress, 0));

            Assert.NotNull(contactURI);

            logger.LogDebug("Contact URI: {contactURI}.", contactURI);

            udpChan.Close();

            logger.LogDebug("-----------------------------------------");
        }
    }
}
