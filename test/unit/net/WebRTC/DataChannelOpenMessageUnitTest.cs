//-----------------------------------------------------------------------------
// Filename: DataChannelOpenMessageUnitTest.cs
//
// Description: Unit tests for the DataChannelOpenMessage class.
//
// History:
// 14 Aug 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class DataChannelOpenMessageUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public DataChannelOpenMessageUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Builds a DCEP OPEN message with the supplied label and protocol lengths. The lengths are
        /// set independently of the payload so that malformed messages can be constructed.
        /// </summary>
        private static byte[] GetDcepOpenBuffer(ushort labelLength, ushort protocolLength, byte[] payload)
        {
            byte[] buffer = new byte[DataChannelOpenMessage.DCEP_OPEN_FIXED_PARAMETERS_LENGTH + (payload?.Length ?? 0)];

            buffer[0] = (byte)DataChannelMessageTypes.OPEN;
            buffer[1] = (byte)DataChannelTypes.DATA_CHANNEL_RELIABLE;

            NetConvert.ToBuffer(labelLength, buffer, 8);
            NetConvert.ToBuffer(protocolLength, buffer, 10);

            payload?.CopyTo(buffer, DataChannelOpenMessage.DCEP_OPEN_FIXED_PARAMETERS_LENGTH);

            return buffer;
        }

        /// <summary>
        /// Tests that a well formed DCEP OPEN message round trips.
        /// </summary>
        [Fact]
        public void RoundtripDcepOpenUnitTest()
        {
            var dcepOpen = new DataChannelOpenMessage
            {
                MessageType = (byte)DataChannelMessageTypes.OPEN,
                ChannelType = (byte)DataChannelTypes.DATA_CHANNEL_RELIABLE,
                Label = "label",
                Protocol = "proto"
            };

            byte[] buffer = new byte[dcepOpen.GetLength()];
            dcepOpen.WriteTo(buffer, 0);

            var parsed = DataChannelOpenMessage.Parse(buffer, 0);

            Assert.Equal("label", parsed.Label);
            Assert.Equal("proto", parsed.Protocol);
        }

        /// <summary>
        /// Tests that a label length larger than the buffer is rejected as a malformed message
        /// rather than being passed on to the string conversion.
        /// </summary>
        [Fact]
        public void ParseLabelLengthExceedsBufferUnitTest()
        {
            byte[] buffer = GetDcepOpenBuffer(0xFFFF, 0, null);

            Assert.Equal(DataChannelOpenMessage.DCEP_OPEN_FIXED_PARAMETERS_LENGTH, buffer.Length);

            var excp = Assert.Throws<SipSorceryException>(() => DataChannelOpenMessage.Parse(buffer, 0));

            logger.LogDebug("Parse failed with {Message}", excp.Message);
        }

        /// <summary>
        /// Tests that a protocol length larger than the buffer is rejected.
        /// </summary>
        [Fact]
        public void ParseProtocolLengthExceedsBufferUnitTest()
        {
            byte[] buffer = GetDcepOpenBuffer(0, 0xFFFF, null);

            Assert.Throws<SipSorceryException>(() => DataChannelOpenMessage.Parse(buffer, 0));
        }

        /// <summary>
        /// Tests that label and protocol lengths that each fit in the buffer, but overflow it when
        /// summed, are rejected.
        /// </summary>
        [Fact]
        public void ParseCombinedLengthsExceedBufferUnitTest()
        {
            // Ten bytes of payload but the two lengths claim six each.
            byte[] buffer = GetDcepOpenBuffer(6, 6, Encoding.UTF8.GetBytes("0123456789"));

            Assert.Throws<SipSorceryException>(() => DataChannelOpenMessage.Parse(buffer, 0));
        }

        /// <summary>
        /// Tests that a buffer without the fixed parameters is rejected.
        /// </summary>
        [Fact]
        public void ParseShortBufferUnitTest()
        {
            byte[] buffer = new byte[DataChannelOpenMessage.DCEP_OPEN_FIXED_PARAMETERS_LENGTH - 1];

            Assert.Throws<SipSorceryException>(() => DataChannelOpenMessage.Parse(buffer, 0));
        }

        /// <summary>
        /// Tests that the label and protocol are read relative to the start position rather than
        /// from a fixed offset in the buffer.
        /// </summary>
        [Fact]
        public void ParseAtNonZeroPositionUnitTest()
        {
            var dcepOpen = new DataChannelOpenMessage
            {
                MessageType = (byte)DataChannelMessageTypes.OPEN,
                ChannelType = (byte)DataChannelTypes.DATA_CHANNEL_RELIABLE,
                Label = "label",
                Protocol = "proto"
            };

            const int posn = 4;

            byte[] buffer = new byte[posn + dcepOpen.GetLength()];
            dcepOpen.WriteTo(buffer, posn);

            var parsed = DataChannelOpenMessage.Parse(buffer, posn);

            Assert.Equal("label", parsed.Label);
            Assert.Equal("proto", parsed.Protocol);
        }

        /// <summary>
        /// Tests that a start position leaving fewer than the fixed parameters in the buffer is
        /// rejected.
        /// </summary>
        [Fact]
        public void ParseAtPositionPastEndOfBufferUnitTest()
        {
            byte[] buffer = GetDcepOpenBuffer(0, 0, null);

            Assert.Throws<SipSorceryException>(() => DataChannelOpenMessage.Parse(buffer, 1));
        }
    }
}
