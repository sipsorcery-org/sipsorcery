//-----------------------------------------------------------------------------
// Filename: SctpDataSenderUnitTest.cs
//
// Description: Unit tests for the SctpDataSender class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpDataSenderUnitTest
    {
        private ILogger logger = null;

        public SctpDataSenderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a small buffer, less than MTU, gets processed correctly.
        /// </summary>
        [Fact]
        public async Task SmallBufferSend()
        {
            BlockingCollection<byte[]> inStm = new BlockingCollection<byte[]>();
            BlockingCollection<byte[]> outStm = new BlockingCollection<byte[]>();
            var mockTransport = new MockB2BSctpTransport(outStm, inStm);
            SctpAssociation assoc = new SctpAssociation(mockTransport, null, 5000, 5000, 1400, 0);

            SctpDataSender sender = new SctpDataSender("dummy", assoc.SendChunk, 1400, 0, 1000);
            sender.StartSending();
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });

            await Task.Delay(100);

            Assert.Single(outStm);

            byte[] sendBuffer = outStm.Single();
            SctpPacket pkt = SctpPacket.Parse(sendBuffer, 0, sendBuffer.Length);

            Assert.NotNull(pkt);
            Assert.NotNull(pkt.Chunks.Single() as SctpDataChunk);

            var datachunk = pkt.Chunks.Single() as SctpDataChunk;

            Assert.Equal("000102", datachunk.UserData.HexStr());
        }
    }
}
