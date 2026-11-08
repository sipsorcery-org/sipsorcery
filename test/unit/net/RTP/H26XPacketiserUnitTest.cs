//-----------------------------------------------------------------------------
// Filename: H26XPacketiserUnitTest.cs
//
// Description: Unit tests for the H264 and H265 packetiser NAL parsing. These
// pin the exact NAL boundaries produced from Annex B access units so that any
// change to the buffer slicing logic (e.g. converting LINQ Skip/Take to span
// slices) that introduces an off-by-one or out-of-range length is caught.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Jun 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.net.RTP.Packetisation;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class H26XPacketiserUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public H26XPacketiserUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an H264 Annex B access unit containing a NAL delimited by a 4 byte start
        /// code followed by a NAL delimited by a 3 byte start code is split into the correct NAL
        /// payloads with the correct last-NAL indications.
        /// </summary>
        [Fact]
        public void ParseH264NalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal1 = { 0x67, 0xAA, 0xBB, 0xCC, 0xDD };
            byte[] nal2 = { 0x68, 0xEE, 0x99 };

            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(nal1)
                .Concat(new byte[] { 0x00, 0x00, 0x01 })
                .Concat(nal2)
                .ToArray();

            var nals = H264Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(nal1, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(nal2, nals[1].NAL);
            Assert.True(nals[1].IsLast);
        }

        /// <summary>
        /// Tests that an H265 Annex B access unit containing a NAL delimited by a 4 byte start
        /// code followed by a NAL delimited by a 3 byte start code is split into the correct NAL
        /// payloads with the correct last-NAL indications.
        /// </summary>
        [Fact]
        public void ParseH265NalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal1 = { 0x40, 0x01, 0xAA, 0xBB, 0xCC };
            byte[] nal2 = { 0x42, 0x01, 0xDD };

            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(nal1)
                .Concat(new byte[] { 0x00, 0x00, 0x01 })
                .Concat(nal2)
                .ToArray();

            var nals = H265Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(nal1, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(nal2, nals[1].NAL);
            Assert.True(nals[1].IsLast);
        }

        /// <summary>
        /// Tests that a one byte NAL in front of a three byte start code is returned like any other NAL.
        /// </summary>
        [Fact]
        public void ParseH264NalsOneByteNalUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] accessUnit = { 0x00, 0x00, 0x01, 0x09, 0x00, 0x00, 0x01, 0x41, 0xAA, 0xBB };

            var nals = H264Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(new byte[] { 0x09 }, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(new byte[] { 0x41, 0xAA, 0xBB }, nals[1].NAL);
            Assert.True(nals[1].IsLast);
        }

        /// <summary>
        /// Tests that zero bytes in front of a start code (trailing_zero_8bits), however many, are not left on the end
        /// of the previous NAL.
        /// </summary>
        [Fact]
        public void ParseH264NalsExtraZerosUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] accessUnit = { 0x00, 0x00, 0x00, 0x01, 0x41, 0xAA, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x41, 0xBB };

            var nals = H264Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(new byte[] { 0x41, 0xAA }, nals[0].NAL);
            Assert.Equal(new byte[] { 0x41, 0xBB }, nals[1].NAL);
        }

        /// <summary>
        /// Tests that a NAL whose header byte is 0x01 (a non-reference slice) is not taken for the end of another start
        /// code straight after the one in front of it.
        /// </summary>
        [Fact]
        public void ParseH264NalsHeaderByteOneUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] accessUnit = { 0x00, 0x00, 0x01, 0x01, 0x41, 0xAA };

            var nals = H264Packetiser.ParseNals(accessUnit).ToList();

            Assert.Single(nals);
            Assert.Equal(new byte[] { 0x01, 0x41, 0xAA }, nals[0].NAL);
            Assert.True(nals[0].IsLast);
        }

        /// <summary>
        /// Tests that the last NAL of an access unit that ends in a start code or in zero padding is flagged as the
        /// last, without the trailing zeros.
        /// </summary>
        [Fact]
        public void ParseH264NalsTrailingStartCodeAndPaddingUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var trailingStartCode = H264Packetiser.ParseNals(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0xAA, 0xBB, 0x00, 0x00, 0x00, 0x01 }).ToList();

            Assert.Single(trailingStartCode);
            Assert.Equal(new byte[] { 0x65, 0xAA, 0xBB }, trailingStartCode[0].NAL);
            Assert.True(trailingStartCode[0].IsLast);

            var trailingZeros = H264Packetiser.ParseNals(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0xAA, 0x00, 0x00 }).ToList();

            Assert.Single(trailingZeros);
            Assert.Equal(new byte[] { 0x65, 0xAA }, trailingZeros[0].NAL);
            Assert.True(trailingZeros[0].IsLast);
        }

        /// <summary>
        /// Tests that the H265 parser, which shares the H264 one's NAL finding, handles an access unit ending in a
        /// start code the same way.
        /// </summary>
        [Fact]
        public void ParseH265NalsTrailingStartCodeUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var nals = H265Packetiser.ParseNals(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xAA, 0x00, 0x00, 0x01 }).ToList();

            Assert.Single(nals);
            Assert.Equal(new byte[] { 0x26, 0x01, 0xAA }, nals[0].NAL);
            Assert.True(nals[0].IsLast);
        }

        /// <summary>
        /// Tests that building the H265 fragmentation unit headers leaves the NAL header it is given unchanged, so every
        /// fragment's FU header carries the NAL's own type.
        /// </summary>
        [Fact]
        public void GetH265RtpHeaderUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nalHeader = { 0x26, 0x01 }; // IDR_W_RADL (type 19), layer 0, TID 1.

            byte[] first = H265Packetiser.GetH265RtpHeader(nalHeader, true, false);
            byte[] last = H265Packetiser.GetH265RtpHeader(nalHeader, false, true);

            Assert.Equal(new byte[] { 0x26, 0x01 }, nalHeader);

            // Payload header of type 49 (FU), then the FU header: S or E bit and the NAL type.
            Assert.Equal(new byte[] { 0x62, 0x01, 0x93 }, first);
            Assert.Equal(new byte[] { 0x62, 0x01, 0x53 }, last);
        }

        /// <summary>
        /// Tests that an H264 NAL only just over the RTP payload limit, which would fit in a single FU-A once its header
        /// is removed, is sent as two fragments, the first with only the start bit and the second with only the end bit.
        /// </summary>
        [Fact]
        public void SendH264NalJustOverPayloadLimitUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal = CreateNal(new byte[] { 0x65 }, RTPSession.RTP_MAX_PAYLOAD + 1);

            var packets = Send("H264", new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nal).ToArray());

            Assert.Equal(2, packets.Count);
            Assert.All(packets, p => Assert.Equal(0x7C, p.Payload[0])); // FU indicator: NRI 3, type 28 (FU-A).
            Assert.Equal(0x85, packets[0].Payload[1]); // S bit, NAL type 5.
            Assert.Equal(0x45, packets[1].Payload[1]); // E bit, NAL type 5.
            Assert.Equal(0, packets[0].Header.MarkerBit);
            Assert.Equal(1, packets[1].Header.MarkerBit);
            Assert.Equal(nal.Skip(1).ToArray(), packets.SelectMany(p => p.Payload.Skip(2)).ToArray());
        }

        /// <summary>
        /// Tests that every fragment of a fragmented H265 NAL carries the NAL's type in its FU header, and that the
        /// frame being sent is not modified.
        /// </summary>
        [Fact]
        public void SendH265FragmentedNalUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal = CreateNal(new byte[] { 0x26, 0x01 }, 3000);
            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nal).ToArray();
            byte[] accessUnitCopy = accessUnit.ToArray();

            var packets = Send("H265", accessUnit);

            Assert.Equal(3, packets.Count);
            Assert.All(packets, p => Assert.Equal(new byte[] { 0x62, 0x01 }, p.Payload.Take(2).ToArray()));
            Assert.Equal(new byte[] { 0x93, 0x13, 0x53 }, packets.Select(p => p.Payload[2]).ToArray());
            Assert.Equal(nal.Skip(2).ToArray(), packets.SelectMany(p => p.Payload.Skip(3)).ToArray());
            Assert.Equal(accessUnitCopy, accessUnit);
        }

        /// <summary>
        /// Tests that H265 NALs only just over the RTP payload limit are sent as two fragments rather than as a single
        /// fragment with both the start and the end bit set.
        /// </summary>
        [Theory]
        [InlineData(1201)]
        [InlineData(1202)]
        public void SendH265NalJustOverPayloadLimitUnitTest(int nalLength)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal = CreateNal(new byte[] { 0x02, 0x01 }, nalLength);

            var packets = Send("H265", new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nal).ToArray());

            Assert.Equal(2, packets.Count);
            Assert.Equal(0x81, packets[0].Payload[2]); // S bit, NAL type 1.
            Assert.Equal(0x41, packets[1].Payload[2]); // E bit, NAL type 1.
            Assert.Equal(nal.Skip(2).ToArray(), packets.SelectMany(p => p.Payload.Skip(3)).ToArray());
        }

        /// <summary>
        /// A NAL with the given header and then bytes that can never form a start code.
        /// </summary>
        private static byte[] CreateNal(byte[] header, int length)
        {
            var nal = new byte[length];
            header.CopyTo(nal, 0);
            for (int i = header.Length; i < length; i++)
            {
                nal[i] = (byte)(2 + i % 254);
            }
            return nal;
        }

        /// <summary>
        /// Sends one access unit on a new video stream and returns the RTP packets put on the wire.
        /// </summary>
        private static List<RTPPacket> Send(string codec, byte[] accessUnit)
        {
            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, codec, 90000, 0, codec == "H264" ? "packetization-mode=1" : null);
            var stream = new VideoStream(new RtpSessionConfig(), 0);
            stream.LocalTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { format }, MediaStreamStatusEnum.SendOnly);
            stream.DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 5004);

            var channel = new CapturingRtpChannel();
            stream.AddRtpChannel(channel);
            try
            {
                stream.SendVideo(3000, accessUnit);
                return channel.Packets.Select(p => new RTPPacket(p)).ToList();
            }
            finally
            {
                channel.Close("test complete");
            }
        }
    }
}
