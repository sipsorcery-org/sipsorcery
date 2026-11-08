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
        /// Tests that the H265 parser drops NALs too short to have the two byte NAL header, wherever they are, and flags
        /// the last NAL it does return as the last one.
        /// </summary>
        [Fact]
        public void ParseH265NalsDropsNalsShorterThanHeaderUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] accessUnit =
            {
                0x00, 0x00, 0x01, 0x26,
                0x00, 0x00, 0x01, 0x40, 0x01, 0xAA,
                0x00, 0x00, 0x00, 0x01, 0x26, 0x01, 0xBB,
                0x00, 0x00, 0x01, 0x02
            };

            var nals = H265Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(new byte[] { 0x40, 0x01, 0xAA }, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(new byte[] { 0x26, 0x01, 0xBB }, nals[1].NAL);
            Assert.True(nals[1].IsLast);
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
        /// Tests that a one byte NAL in front of NALs that are aggregated, which used to fail the whole frame with an
        /// IndexOutOfRangeException, is dropped and the other NALs are sent in one aggregation packet.
        /// </summary>
        [Fact]
        public void SendH265OneByteNalBeforeSmallNalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] vps = CreateNal(new byte[] { 0x40, 0x01 }, 24);
            byte[] sps = CreateNal(new byte[] { 0x42, 0x01 }, 40);
            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x01, 0x26, 0x00, 0x00, 0x01 }
                .Concat(vps)
                .Concat(new byte[] { 0x00, 0x00, 0x01 })
                .Concat(sps)
                .ToArray();

            var packets = Send("H265", accessUnit);

            var packet = Assert.Single(packets);
            Assert.Equal(48, H265PayloadType(packet));
            Assert.Equal(new[] { vps, sps }, ReadAggregation(packet.Payload));
            Assert.Equal(1, packet.Header.MarkerBit);
        }

        /// <summary>
        /// Tests that when the last NAL of an access unit is too short to send, the last packet of the NAL before it
        /// carries the marker bit, so the receiver still sees where the frame ends.
        /// </summary>
        [Fact]
        public void SendH265OneByteLastNalUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] idr = CreateNal(new byte[] { 0x26, 0x01 }, 3000);
            byte[] accessUnit = AccessUnit(idr).Concat(new byte[] { 0x00, 0x00, 0x01, 0x02 }).ToArray();

            var packets = Send("H265", accessUnit);

            Assert.Equal(3, packets.Count);
            Assert.Equal(new[] { 0, 0, 1 }, packets.Select(p => (int)p.Header.MarkerBit).ToArray());
            Assert.Equal(idr.Skip(2).ToArray(), packets.SelectMany(p => p.Payload.Skip(3)).ToArray());
        }

        /// <summary>
        /// Tests that an aggregation packet, counting its payload header and the size field in front of each NAL, is
        /// never longer than the RTP payload limit. NALs of 600 and 594 bytes fill one exactly; NALs of 600 and 596
        /// bytes would make it 1202 bytes long, so each is sent on its own.
        /// </summary>
        [Theory]
        [InlineData(594, 1)]
        [InlineData(596, 2)]
        public void SendH265AggregationPacketSizeUnitTest(int secondNalLength, int expectedPackets)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] first = CreateNal(new byte[] { 0x02, 0x01 }, 600);
            byte[] second = CreateNal(new byte[] { 0x02, 0x01 }, secondNalLength);

            var packets = Send("H265", AccessUnit(first, second));

            Assert.Equal(expectedPackets, packets.Count);
            Assert.All(packets, p => Assert.True(p.Payload.Length <= RTPSession.RTP_MAX_PAYLOAD));

            if (expectedPackets == 1)
            {
                Assert.Equal(48, H265PayloadType(packets[0]));
                Assert.Equal(RTPSession.RTP_MAX_PAYLOAD, packets[0].Payload.Length);
                Assert.Equal(new[] { first, second }, ReadAggregation(packets[0].Payload));
            }
            else
            {
                Assert.Equal(first, packets[0].Payload);
                Assert.Equal(second, packets[1].Payload);
            }
        }

        /// <summary>
        /// Tests that consecutive small NALs share aggregation packets for as long as they fit, and that a NAL with no
        /// neighbour to share one with is sent as a single NAL unit packet: RFC 7798 section 4.4.2 does not allow an
        /// aggregation packet holding a single NAL.
        /// </summary>
        [Fact]
        public void SendH265AggregationPacketsHoldTwoOrMoreNalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] a = CreateNal(new byte[] { 0x02, 0x01 }, 300);
            byte[] b = CreateNal(new byte[] { 0x02, 0x01 }, 400);
            byte[] c = CreateNal(new byte[] { 0x02, 0x01 }, 700);
            byte[] d = CreateNal(new byte[] { 0x02, 0x01 }, 200);
            byte[] e = CreateNal(new byte[] { 0x02, 0x01 }, 700);

            // 300 and 400 fit together. 700 does not fit with them, but does with the 200 after it.
            var shared = Send("H265", AccessUnit(a, b, c, d));

            Assert.Equal(2, shared.Count);
            Assert.All(shared, p => Assert.Equal(48, H265PayloadType(p)));
            Assert.Equal(new[] { a, b }, ReadAggregation(shared[0].Payload));
            Assert.Equal(new[] { c, d }, ReadAggregation(shared[1].Payload));
            Assert.Equal(new[] { 0, 1 }, shared.Select(p => (int)p.Header.MarkerBit).ToArray());

            // Two NALs of 700 bytes do not fit in one aggregation packet, so each is sent on its own.
            var alone = Send("H265", AccessUnit(c, e));

            Assert.Equal(2, alone.Count);
            Assert.Equal(c, alone[0].Payload);
            Assert.Equal(e, alone[1].Payload);
            Assert.Equal(new[] { 0, 1 }, alone.Select(p => (int)p.Header.MarkerBit).ToArray());
        }

        /// <summary>
        /// Tests the public H265Packetiser.CreateAggregated: parameter sets share an aggregation packet, a NAL too large
        /// to share one is returned as it is, a NAL too short to have a NAL header is never aggregated, and two NALs
        /// that exactly fill the payload limit share one.
        /// </summary>
        [Fact]
        public void CreateAggregatedUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] vps = CreateNal(new byte[] { 0x40, 0x01 }, 24);
            byte[] sps = CreateNal(new byte[] { 0x42, 0x01 }, 40);
            byte[] pps = CreateNal(new byte[] { 0x44, 0x01 }, 8);
            byte[] idr = CreateNal(new byte[] { 0x26, 0x01 }, 4000);
            byte[] tooShort = { 0x02 };
            byte[] first = CreateNal(new byte[] { 0x02, 0x01 }, 600);
            byte[] second = CreateNal(new byte[] { 0x02, 0x01 }, 594);

            var input = new[] { vps, sps, pps, idr, tooShort, first, second }
                .Select((nal, i) => new H265Packetiser.H265Nal(nal, i == 6))
                .ToList();

            var output = H265Packetiser.CreateAggregated(input, RTPSession.RTP_MAX_PAYLOAD).ToList();

            Assert.Equal(4, output.Count);
            Assert.Equal(new[] { vps, sps, pps }, ReadAggregation(output[0].NAL));
            Assert.Same(idr, output[1].NAL);
            Assert.Same(tooShort, output[2].NAL);
            Assert.Equal(RTPSession.RTP_MAX_PAYLOAD, output[3].NAL.Length);
            Assert.Equal(new[] { first, second }, ReadAggregation(output[3].NAL));
            Assert.Equal(new[] { false, false, false, true }, output.Select(n => n.IsLast).ToArray());
        }

        /// <summary>
        /// Tests that an aggregation packet's payload header is built from all the NALs it holds, as RFC 7798 section
        /// 4.4.2 requires: the F bit set if any NAL has it set, type 48, and the lowest LayerId and TID of any NAL.
        /// </summary>
        [Theory]
        // F 0, LayerId 2, TID 3 and F 1, LayerId 1, TID 2 give F 1, LayerId 1, TID 2.
        [InlineData(new byte[] { 0x40, 0x13 }, new byte[] { 0xC2, 0x0A }, new byte[] { 0xE0, 0x0A })]
        // LayerId 33, TID 4 and LayerId 40, TID 1, which use the LayerId bit in the first byte, give LayerId 33, TID 1.
        [InlineData(new byte[] { 0x03, 0x0C }, new byte[] { 0x03, 0x41 }, new byte[] { 0x61, 0x09 })]
        public void CreateAggregatedPayloadHeaderUnitTest(byte[] firstHeader, byte[] secondHeader, byte[] expectedHeader)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var nals = new[] { CreateNal(firstHeader, 10), CreateNal(secondHeader, 10) }
                .Select((nal, i) => new H265Packetiser.H265Nal(nal, i == 1));

            var aggregation = Assert.Single(H265Packetiser.CreateAggregated(nals, RTPSession.RTP_MAX_PAYLOAD));

            Assert.Equal(expectedHeader, aggregation.NAL.Take(2).ToArray());
        }

        /// <summary>
        /// An Annex B access unit holding the given NALs, each after a four byte start code.
        /// </summary>
        private static byte[] AccessUnit(params byte[][] nals)
        {
            return nals.SelectMany(nal => new byte[] { 0x00, 0x00, 0x00, 0x01 }.Concat(nal)).ToArray();
        }

        /// <summary>
        /// The type in an H265 packet's payload header: 48 for an aggregation packet, 49 for a fragmentation unit, or
        /// the type of the NAL in a single NAL unit packet.
        /// </summary>
        private static int H265PayloadType(RTPPacket packet)
        {
            return (packet.Payload[0] >> 1) & 0x3F;
        }

        /// <summary>
        /// The NALs in an H265 aggregation packet, read back from their size fields.
        /// </summary>
        private static List<byte[]> ReadAggregation(byte[] payload)
        {
            var nals = new List<byte[]>();
            for (int position = 2; position < payload.Length;)
            {
                int length = (payload[position] << 8) | payload[position + 1];
                nals.Add(payload.Skip(position + 2).Take(length).ToArray());
                position += 2 + length;
            }
            return nals;
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
