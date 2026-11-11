//-----------------------------------------------------------------------------
// Filename: RTCPCompoundPacketUnitTest.cs
//
// Description: Unit tests for the RTCPCompoundPacket class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 30 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPCompoundPacketUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPCompoundPacketUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCPCompoundPacket payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPCompoundPacketUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            uint ssrc = 23;
            ulong ntpTs = 1;
            uint rtpTs = 2;
            uint packetCount = 3;
            uint octetCount = 4;

            uint rrSsrc = 5;
            byte fractionLost = 6;
            int packetsLost = 7;
            uint highestSeqNum = 8;
            uint jitter = 9;
            uint lastSRTimestamp = 10;
            uint delaySinceLastSR = 11;

            string cname = "dummy";

            ReceptionReportSample rr = new ReceptionReportSample(rrSsrc, fractionLost, packetsLost, highestSeqNum, jitter, lastSRTimestamp, delaySinceLastSR);
            var sr = new RTCPSenderReport(ssrc, ntpTs, rtpTs, packetCount, octetCount, new List<ReceptionReportSample> { rr });
            RTCPSDesReport sdesReport = new RTCPSDesReport(ssrc, cname);

            RTCPCompoundPacket compoundPacket = new RTCPCompoundPacket(sr, sdesReport);

            byte[] buffer = compoundPacket.GetBytes();

            RTCPCompoundPacket parsedCP = new RTCPCompoundPacket(buffer);
            RTCPSenderReport parsedSR = parsedCP.SenderReport;

            Assert.Equal(ssrc, parsedSR.SSRC);
            Assert.Equal(ntpTs, parsedSR.NtpTimestamp);
            Assert.Equal(rtpTs, parsedSR.RtpTimestamp);
            Assert.Equal(packetCount, parsedSR.PacketCount);
            Assert.Equal(octetCount, parsedSR.OctetCount);
            Assert.True(parsedSR.ReceptionReports.Count == 1);

            Assert.Equal(rrSsrc, parsedSR.ReceptionReports.First().SSRC);
            Assert.Equal(fractionLost, parsedSR.ReceptionReports.First().FractionLost);
            Assert.Equal(packetsLost, parsedSR.ReceptionReports.First().PacketsLost);
            Assert.Equal(highestSeqNum, parsedSR.ReceptionReports.First().ExtendedHighestSequenceNumber);
            Assert.Equal(jitter, parsedSR.ReceptionReports.First().Jitter);
            Assert.Equal(lastSRTimestamp, parsedSR.ReceptionReports.First().LastSenderReportTimestamp);
            Assert.Equal(delaySinceLastSR, parsedSR.ReceptionReports.First().DelaySinceLastSenderReport);

            Assert.Equal(cname, parsedCP.SDesReport.CNAME);
        }

        [Fact]
        public void ParseChromeRtcpPacketUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = TypeExtensions.ParseHexStr("81C9000700000001384B9567000000000000D214000004C900000000000000008FCE0005000000010000000052454D42010A884A384B95678000000BF9CDAEFFBEF60160B98F");

            RTCPCompoundPacket cp = new RTCPCompoundPacket(buffer);

            Assert.NotNull(cp);
        }

        [Fact]
        public void ParseChromeRtcpPacket2UnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = TypeExtensions.ParseHexStr("81C90007FA17FA17761E74C8000000000000F19700000045000000000000000080000001FF6EBFCCFAFB3C6D6291");

            RTCPCompoundPacket cp = new RTCPCompoundPacket(buffer);

            Assert.NotNull(cp);
        }
        
        [Fact]
        public void ParseChromeRtcpPacketWith6SSRCsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var buffer = TypeExtensions.ParseHexStr("81C9000700000001497EB0250000000000009CA200000A0500000000000000008FCE000A000000010000000052454D42060B29711CAB48A626A3FADE2EE30A21497EB0255C6A292A604EEAC8");

            RTCPCompoundPacket cp = new RTCPCompoundPacket(buffer);
            Assert.Equal(6, cp.Feedback.NumSsrcs);
            Assert.Equal(6, cp.Feedback.FeedbackSSRCs.Length);
            Assert.Equal( 8 + 12 + (5*4),  cp.Feedback.SENDER_PAYLOAD_SIZE);//// 8 bytes from (SenderSSRC + MediaSSRC) + extra 12 bytes from REMB Definition +5x extra 4 bytes for SSRCs
            Assert.NotNull(cp);
        }

        [Fact]
        public void ParseSdesWithOptionalToolAndByeUnitTest()
        {
            byte[] packet = BuildCompoundPacketWithToolAndBye();

            var parsed = new RTCPCompoundPacket(packet);

            Assert.NotNull(parsed.ReceiverReport);
            Assert.NotNull(parsed.SDesReport);
            Assert.Equal("linphone@example.test", parsed.SDesReport.CNAME);
            Assert.NotNull(parsed.Bye);
            Assert.Equal(0x10203040U, parsed.Bye.SSRC);
        }

        [Fact]
        public void TryParseSdesWithOptionalToolAndByeUnitTest()
        {
            byte[] packet = BuildCompoundPacketWithToolAndBye();

            bool success = RTCPCompoundPacket.TryParse(
                packet.AsSpan(), out RTCPCompoundPacket parsed, out int consumed);

            Assert.True(success);
            Assert.Equal(packet.Length, consumed);
            Assert.NotNull(parsed.SDesReport);
            Assert.NotNull(parsed.Bye);
        }

        [Fact]
        public void TryParseTruncatedSdesLengthUnitTest()
        {
            byte[] receiverReport = BuildReceiverReport();
            byte[] truncatedSdes = { 0x81, (byte)RTCPReportTypesEnum.SDES, 0x00, 0x05 };
            byte[] packet = receiverReport.Concat(truncatedSdes).ToArray();

            bool success = RTCPCompoundPacket.TryParse(
                packet.AsSpan(), out RTCPCompoundPacket parsed, out int consumed);

            Assert.False(success);
            Assert.Equal(receiverReport.Length, consumed);
            Assert.NotNull(parsed.SDesReport);
            Assert.Null(parsed.Bye);
        }

        [Fact]
        public void ParseTruncatedSdesLengthUnitTest()
        {
            byte[] receiverReport = BuildReceiverReport();
            byte[] truncatedSdes = { 0x81, (byte)RTCPReportTypesEnum.SDES, 0x00, 0x05 };
            byte[] packet = receiverReport.Concat(truncatedSdes).ToArray();

            Exception exception = Record.Exception(() => new RTCPCompoundPacket(packet));

            Assert.Null(exception);
        }

        private static byte[] BuildCompoundPacketWithToolAndBye()
        {
            const uint ssrc = 0x10203040;
            var bytes = new List<byte>();
            bytes.AddRange(BuildReceiverReport());
            bytes.AddRange(BuildSdesReport(
                ssrc,
                "linphone@example.test",
                "Linphone-Desktop/6.0.0"));
            bytes.AddRange(new RTCPBye(ssrc, "completed").GetBytes());
            return bytes.ToArray();
        }

        private static byte[] BuildReceiverReport()
        {
            byte[] packet = new byte[8];
            packet[0] = 0x80;
            packet[1] = (byte)RTCPReportTypesEnum.RR;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), 0x10203040);
            return packet;
        }

        private static byte[] BuildSdesReport(uint ssrc, string cname, string tool)
        {
            var payload = new List<byte>();
            byte[] ssrcBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(ssrcBytes, ssrc);
            payload.AddRange(ssrcBytes);
            AddSdesItem(payload, 1, Encoding.UTF8.GetBytes(cname));
            AddSdesItem(payload, 6, Encoding.UTF8.GetBytes(tool));
            payload.Add(0);

            while ((RTCPHeader.HEADER_BYTES_LENGTH + payload.Count) % 4 != 0)
            {
                payload.Add(0);
            }

            byte[] packet = new byte[RTCPHeader.HEADER_BYTES_LENGTH + payload.Count];
            packet[0] = 0x81;
            packet[1] = (byte)RTCPReportTypesEnum.SDES;
            BinaryPrimitives.WriteUInt16BigEndian(
                packet.AsSpan(2), (ushort)(packet.Length / 4 - 1));
            payload.CopyTo(packet, RTCPHeader.HEADER_BYTES_LENGTH);
            return packet;
        }

        /// <summary>
        /// A generic NACK with several FCI entries followed by a PLI, as Chrome sends when it has
        /// lost a few packets of a frame. Every NACK entry is read past, so the PLI isn't lost, and
        /// an entry whose PID happens to look like a BYE packet type isn't taken for one.
        /// </summary>
        [Fact]
        public void ParseMultiEntryNackThenPliUnitTest()
        {
            byte[] packet = RrNackPli();

            var compound = new RTCPCompoundPacket(packet);

            AssertRrNackPli(compound);
        }

        /// <summary>
        /// As <see cref="ParseMultiEntryNackThenPliUnitTest"/>, through TryParse, which must also
        /// consume the whole compound packet.
        /// </summary>
        [Fact]
        public void TryParseMultiEntryNackThenPliUnitTest()
        {
            byte[] packet = RrNackPli();

            Assert.True(RTCPCompoundPacket.TryParse(packet.AsSpan(), out var compound, out int consumed));

            AssertRrNackPli(compound);
            Assert.Equal(packet.Length, consumed);
        }

        private const uint NACK_TEST_SENDER_SSRC = 0x11111111;
        private const uint NACK_TEST_MEDIA_SSRC = 0x22222222;

        /// <summary>RR (no report blocks), generic NACK with 3 entries, PLI.</summary>
        private static byte[] RrNackPli()
        {
            var packet = new List<byte>();
            void U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); packet.AddRange(b); }
            void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); packet.AddRange(b); }

            // RR, no report blocks: 2 words.
            packet.Add(0x80); packet.Add((byte)RTCPReportTypesEnum.RR); U16(1); U32(NACK_TEST_SENDER_SSRC);

            // Generic NACK (FMT 1), 3 FCI entries: the header, 2 SSRCs and 3 entries are 6 words, so a length of 5. The second
            // entry's PID 0x12CB has 203 (BYE) where a packet type would be.
            packet.Add(0x81); packet.Add((byte)RTCPReportTypesEnum.RTPFB); U16(5); U32(NACK_TEST_SENDER_SSRC); U32(NACK_TEST_MEDIA_SSRC);
            U16(0x1200); U16(0x0003);
            U16(0x12CB); U16(0x0001);
            U16(0x12F0); U16(0x0000);

            // PLI (PSFB, FMT 1): 3 words.
            packet.Add(0x81); packet.Add((byte)RTCPReportTypesEnum.PSFB); U16(2); U32(NACK_TEST_SENDER_SSRC); U32(NACK_TEST_MEDIA_SSRC);

            return packet.ToArray();
        }

        private static void AssertRrNackPli(RTCPCompoundPacket compound)
        {
            Assert.NotNull(compound.ReceiverReport);
            Assert.Equal(NACK_TEST_SENDER_SSRC, compound.ReceiverReport.SSRC);
            Assert.Null(compound.Bye);
            Assert.Null(compound.SenderReport);

            var feedback = compound.FeedbackMessages;
            Assert.Equal(2, feedback.Count);
            Assert.Equal(RTCPReportTypesEnum.RTPFB, feedback[0].Header.PacketType);
            Assert.Equal(RTCPFeedbackTypesEnum.NACK, feedback[0].Header.FeedbackMessageType);
            Assert.Equal((ushort)0x1200, feedback[0].PID);
            Assert.Equal(new ushort[] { 0x1200, 0x1201, 0x1202, 0x12CB, 0x12CC, 0x12F0 }, feedback[0].LostSequenceNumbers);
            Assert.Equal(RTCPReportTypesEnum.PSFB, feedback[1].Header.PacketType);
            Assert.Equal(PSFBFeedbackTypesEnum.PLI, feedback[1].Header.PayloadFeedbackMessageType);
            Assert.Equal(NACK_TEST_MEDIA_SSRC, feedback[1].MediaSSRC);
        }

        private static void AddSdesItem(List<byte> payload, byte type, byte[] value)
        {
            payload.Add(type);
            payload.Add(checked((byte)value.Length));
            payload.AddRange(value);
        }

    }
}
