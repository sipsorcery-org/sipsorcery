//-----------------------------------------------------------------------------
// Filename: RTCDataChannelReliabilityUnitTest.cs
//
// Description: Unit tests for the RTCDataChannel and RTCDataChannelInit class.
//
// History:
// 28 Mar 2021	Cam Newnham     Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.IntegrationTests
{
    [Trait("Category", "integration")]
    public class RTCPeerConnectionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }


        /// <summary>
        /// Tests that established data channels correctly set reliability parameters
        /// </summary>
        [Theory]
        [InlineData(true, ushort.MaxValue, 1)]
        [InlineData(true, 100, ushort.MaxValue)]
        [InlineData(false, ushort.MaxValue, 1)]
        [InlineData(false, 100, ushort.MaxValue)]
        public async void CheckDataChannelEstablishment(bool ordered, ushort maxPacketLifetime, ushort maxRetransmits)
        {
            var init = new RTCDataChannelInit()
            {
                ordered = ordered,
                maxPacketLifeTime = maxPacketLifetime == ushort.MaxValue ? null : (ushort?)maxPacketLifetime,
                maxRetransmits = maxRetransmits == ushort.MaxValue ? null : (ushort?)maxRetransmits
            };

            var peers = await DataChannelTestHelper.CreateConnectedPeersWithDataChannel(init);

            var bob = peers.Item1;
            var alice = peers.Item2;

            Assert.Single(bob.DataChannels);
            Assert.Single(alice.DataChannels);

            var dc0 = bob.DataChannels.First();
            var dc1 = alice.DataChannels.First();


            Assert.Equal(dc0.maxPacketLifeTime, init.maxPacketLifeTime);
            Assert.Equal(dc0.maxPacketLifeTime, dc1.maxPacketLifeTime);

            Assert.Equal(dc0.maxRetransmits, init.maxRetransmits);
            Assert.Equal(dc0.maxRetransmits, dc1.maxRetransmits);

            Assert.Equal(dc0.ordered, init.ordered);
            Assert.Equal(dc0.ordered, dc1.ordered);

            bob.close();
            alice.close();
        }

        public class DataChannelTestHelper
        {
            public static async Task<(RTCPeerConnection, RTCPeerConnection)> CreateConnectedPeersWithDataChannel(RTCDataChannelInit init)
            {
                var aliceDataConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var bobDataOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                var alice = new RTCPeerConnection();
                var dc = await alice.createDataChannel("dc1", init);
                dc.onopen += () => aliceDataConnected.TrySetResult(true);
                var aliceOffer = alice.createOffer();
                await alice.setLocalDescription(aliceOffer);

                var bob = new RTCPeerConnection();
                RTCDataChannel bobData = null;
                bob.ondatachannel += (chan) =>
                {
                    bobData = chan;
                    bobDataOpened.TrySetResult(true);
                };

                var setOfferResult = bob.setRemoteDescription(aliceOffer);

                var bobAnswer = bob.createAnswer();
                await bob.setLocalDescription(bobAnswer);
                var setAnswerResult = alice.setRemoteDescription(bobAnswer);

                await Task.WhenAny(Task.WhenAll(aliceDataConnected.Task, bobDataOpened.Task), Task.Delay(2000));

                return (bob, alice);
            }
        }
    }
}
