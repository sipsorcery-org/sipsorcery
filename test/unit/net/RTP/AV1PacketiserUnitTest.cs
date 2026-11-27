//-----------------------------------------------------------------------------
// Filename: AV1PacketiserUnitTest.cs
//
// Description: Unit tests for AV1 RTP packetisation and depacketisation.
//
// Author(s):
// OpenAI
//
// History:
// 28 Mar 2026  OpenAI         Created, Vancouver.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class AV1PacketiserUnitTest
    {
        private const byte AGGREGATION_HEADER_NON_FLAG_MASK = 0x37; // W field and reserved bits.
        private const int OBU_TYPE_SHIFT = 3;

        [Fact]
        public void PacketizationAcceptsStackBackedSliceAndOwnsItsOutputBuffers()
        {
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[] { 1, 2, 3, 4, 5 });
            Span<byte> source = stackalloc byte[32];
            source.Fill(0xff); // Bytes outside the slice must not be parsed as OBUs.
            var slice = source.Slice(5, frame.Length);
            frame.AsSpan().CopyTo(slice);

            var packets = AV1Packetiser.Packetize((ReadOnlySpan<byte>)slice, 4);

            Assert.Equal(frame, slice.ToArray());
            source.Clear();
            var depacketiser = new AV1Depacketiser();
            byte[] reconstructed = null;
            for (ushort i = 0; i < packets.Count; i++)
            {
                reconstructed = depacketiser.ProcessRTPPayload(packets[i].Payload, i, 90000,
                    packets[i].IsLast ? 1 : 0, out _)?.ToArray();
            }
            Assert.Equal(frame, reconstructed);
        }

        [Fact]
        public void PacketizationPreservesInputAndOwnsItsOutputBuffers()
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 1, 2, 3 });
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[1500]);
            var temporalUnit = sequenceHeader.Concat(frame).ToArray();
            var original = temporalUnit.ToArray();

            var packets = AV1Packetiser.Packetize(temporalUnit.AsSpan(), 200);

            Assert.Equal(original, temporalUnit);
            var payloads = packets.Select(packet => packet.Payload.ToArray()).ToArray();
            Array.Clear(temporalUnit, 0, temporalUnit.Length);
            for (int i = 0; i < packets.Count; i++)
            {
                Assert.Equal(payloads[i], packets[i].Payload);
            }
        }

        [Fact]
        public void PublicObuParserPreservesSizeFieldsAndReturnsIndependentArrays()
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 1, 2, 3 });
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[128]);
            var temporalUnit = sequenceHeader.Concat(frame).ToArray();

            var obus = AV1Packetiser.ParseObus(temporalUnit).ToArray();

            Assert.Equal(2, obus.Length);
            Assert.Equal(sequenceHeader, obus[0]);
            Assert.Equal(frame, obus[1]);
            obus[0][0] = 0;
            Assert.Equal(sequenceHeader[0], temporalUnit[0]);
        }

        [Fact]
        public void FragmentedKeyFrameStartsWithSequenceHeaderAndFrameFragment()
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 1, 2, 3 });
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[1500]);

            var packets = AV1Packetiser.Packetize(sequenceHeader.Concat(frame).ToArray().AsSpan(), 200);

            Assert.True(packets.Count > 1);
            Assert.Equal(0x48, packets[0].Payload[0]); // N=1, Y=1, Z=0, W=0.
            Assert.Equal(200, packets[0].Payload.Length);
            int offset = 1;
            Assert.True(AV1Packetiser.TryReadLeb128(packets[0].Payload, ref offset, out int sequenceLength, out _));
            offset += sequenceLength;
            Assert.True(AV1Packetiser.TryReadLeb128(packets[0].Payload, ref offset, out int frameLength, out _));
            Assert.True(frameLength > 0);
            Assert.Equal(AV1Packetiser.AV1ObuType.Frame, AV1Packetiser.GetObuType(new[] { packets[0].Payload[offset] }));
            AssertPacketLimitsAndRoundTrip(sequenceHeader.Concat(frame).ToArray(), 200, true);
        }

        [Theory]
        [InlineData(126, 129, 1)] // A 127-byte element uses a one-byte length.
        [InlineData(127, 130, 2)] // A 128-byte element needs two length bytes.
        [InlineData(127, 131, 1)]
        [InlineData(128, 132, 1)]
        public void PacketSizeIncludesLeb128LengthAtExactBoundaries(int payloadLength, int maxPayloadSize, int expectedPackets)
        {
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[payloadLength]);
            Assert.Equal(expectedPackets, AV1Packetiser.Packetize(frame.AsSpan(), maxPayloadSize).Count);
            AssertPacketLimitsAndRoundTrip(frame, maxPayloadSize, false);
        }

        [Fact]
        public void AggregationAndFragmentationRespectSmallPayloadLimits()
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 1, 2, 3 });
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, Enumerable.Range(0, 1500).Select(x => (byte)x).ToArray());
            var metadata = CreateObu(AV1Packetiser.AV1ObuType.Metadata, new byte[] { 5, 6 });
            var temporalUnit = sequenceHeader.Concat(frame).Concat(metadata).ToArray();

            for (int maxPayloadSize = 4; maxPayloadSize <= 140; maxPayloadSize++)
            {
                AssertPacketLimitsAndRoundTrip(temporalUnit, maxPayloadSize, true);
                AssertPacketLimitsAndRoundTrip(frame, maxPayloadSize, false);
            }
        }

        [Fact]
        public void FrameContinuationCanAggregateTheFollowingObu()
        {
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[15]);
            var metadata = CreateObu(AV1Packetiser.AV1ObuType.Metadata, new byte[] { 1 });
            var temporalUnit = frame.Concat(metadata).ToArray();
            var packets = AV1Packetiser.Packetize(temporalUnit.AsSpan(), 12);

            Assert.Equal(2, packets.Count);
            Assert.Equal(0x40, packets[0].Payload[0]);
            Assert.Equal(0x80, packets[1].Payload[0]);
            AssertPacketLimitsAndRoundTrip(temporalUnit, 12, false);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)] // After the sequence header only one OBU-header byte would fit.
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(128)]
        public void ExtendedObuHeaderIsPreservedAndNeverSplit(int maxPayloadSize)
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, Array.Empty<byte>());
            var frame = new byte[] { 0x36, 0x28, 0x0a }.Concat(new byte[10]).ToArray();
            var temporalUnit = sequenceHeader.Concat(frame).ToArray();
            var packets = AV1Packetiser.Packetize(temporalUnit.AsSpan(), maxPayloadSize);

            foreach (var packet in packets)
            {
                int offset = 1;
                bool continuation = ((AV1AggregationHeaderFlags)packet.Payload[0]).HasFlag(AV1AggregationHeaderFlags.FirstObuIsContinuation);
                while (offset < packet.Payload.Length)
                {
                    Assert.True(AV1Packetiser.TryReadLeb128(packet.Payload, ref offset, out int length, out _));
                    if (!continuation && ((AV1ObuHeaderFlags)packet.Payload[offset]).HasFlag(AV1ObuHeaderFlags.Extension))
                    {
                        Assert.True(length >= 2);
                        Assert.Equal(0x28, packet.Payload[offset + 1]);
                    }
                    offset += length;
                    continuation = false;
                }
            }
            AssertPacketLimitsAndRoundTrip(temporalUnit, maxPayloadSize, true);
        }

        [Theory]
        [InlineData(AV1Packetiser.AV1ObuType.TemporalDelimiter)]
        [InlineData(AV1Packetiser.AV1ObuType.TileList)]
        [InlineData(AV1Packetiser.AV1ObuType.Padding)]
        public void NonTransmittedObusAreRemoved(AV1Packetiser.AV1ObuType type)
        {
            var skipped = CreateObu(type, Array.Empty<byte>());
            Assert.Empty(AV1Packetiser.Packetize(skipped.AsSpan(), 1200));
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[] { 1 });
            Assert.Equal(AV1Packetiser.Packetize(frame.AsSpan(), 1200)[0].Payload,
                AV1Packetiser.Packetize(skipped.Concat(frame).Concat(skipped).ToArray().AsSpan(), 1200)[0].Payload);
        }

        [Theory]
        [InlineData(new byte[] { 0x36 })] // Missing extension header.
        [InlineData(new byte[] { 0x34 })] // Missing extension on an unsized OBU.
        [InlineData(new byte[] { 0x32 })] // Missing size.
        [InlineData(new byte[] { 0x32, 0x80 })] // Unterminated size.
        [InlineData(new byte[] { 0x32, 0x02, 0x01 })] // Truncated payload.
        [InlineData(new byte[] { 0x32, 0xff, 0xff, 0xff, 0xff, 0x07 })] // Size addition would overflow int.
        [InlineData(new byte[] { 0x32, 0xff, 0xff, 0xff, 0xff, 0x0f })] // Size exceeds int.
        public void MalformedObusAreRejected(byte[] temporalUnit)
        {
            Assert.Throws<FormatException>(() => AV1Packetiser.Packetize(temporalUnit.AsSpan(), 1200));
            Assert.Throws<FormatException>(() => AV1Packetiser.ParseObus(temporalUnit).ToArray());
        }

        [Theory]
        [InlineData(new byte[] { 0x80 })]
        [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
        [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 })]
        [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 })]
        public void Leb128RejectsTruncatedAndOverflowingValues(byte[] encoded)
        {
            int offset = 0;
            Assert.False(AV1Packetiser.TryReadLeb128(encoded, ref offset, out int value, out _));
            Assert.Equal(0, value);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(int.MaxValue)]
        public void Leb128RoundTripsNonNegativeIntegers(int value)
        {
            var encoded = AV1Packetiser.WriteLeb128(value);
            int offset = 0;
            Assert.True(AV1Packetiser.TryReadLeb128(encoded, ref offset, out int decoded, out int length));
            Assert.Equal(value, decoded);
            Assert.Equal(encoded.Length, length);
            Assert.Equal(length, offset);
        }

        [Fact]
        public void UnsizedObuRoundTripsAsSizedLowOverheadBitstream()
        {
            var packets = AV1Packetiser.Packetize(new byte[] { 0x30, 1, 2, 3 }.AsSpan(), 4);
            var depacketiser = new AV1Depacketiser();
            byte[] result = null;
            for (ushort i = 0; i < packets.Count; i++)
            {
                result = depacketiser.ProcessRTPPayload(packets[i].Payload, i, 90000,
                    packets[i].IsLast ? 1 : 0, out _)?.ToArray();
            }
            Assert.Equal(new byte[] { 0x32, 3, 1, 2, 3 }, result);
        }

        [Fact]
        public void DepacketiserStillAcceptsInternalSizeFields()
        {
            var obu = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[] { 1, 2, 3 });
            var payload = new byte[] { 0x10 }.Concat(obu).ToArray(); // W=1, no RTP element length.
            var frame = new AV1Depacketiser().ProcessRTPPayload(payload, 0, 90000, 1, out bool keyFrame);

            Assert.False(keyFrame);
            Assert.Equal(obu, frame.ToArray());
        }

        [Theory]
        [InlineData(new byte[] { 0x80, 3, 0x28, 1, 2, 2, 0x28, 9 })] // W=0: both elements have lengths.
        [InlineData(new byte[] { 0xa0, 3, 0x28, 1, 2, 0x28, 9 })] // W=2: the final length is implicit.
        public void DepacketiserRestoresHeaderSplitAcrossFragmentsAndAggregatesNextObu(byte[] finalPayload)
        {
            var depacketiser = new AV1Depacketiser();
            // W=1, Y=1: only the first byte of an extended Frame OBU header.
            Assert.Null(depacketiser.ProcessRTPPayload(new byte[] { 0x50, 0x34 }, 0, 90000, 0, out _));

            using var result = depacketiser.ProcessRTPPayload(finalPayload, 1, 90000, 1, out _);

            Assert.NotNull(result);
            Assert.Equal(new byte[] { 0x36, 0x28, 2, 1, 2, 0x2a, 1, 9 }, result.ToArray());
        }

        [Fact]
        public void DepacketisedFrameDoesNotRetainPacketBuffersOrChangeWhenReused()
        {
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame,
                Enumerable.Range(0, 1500).Select(x => (byte)x).ToArray());
            var packets = AV1Packetiser.Packetize(frame.AsSpan(), 200);
            var depacketiser = new AV1Depacketiser();
            System.IO.MemoryStream result = null;
            for (ushort i = 0; i < packets.Count; i++)
            {
                result = depacketiser.ProcessRTPPayload(packets[i].Payload, i, 90000,
                    packets[i].IsLast ? 1 : 0, out _);
            }

            Assert.NotNull(result);
            foreach (var packet in packets)
            {
                Array.Clear(packet.Payload, 0, packet.Payload.Length);
            }
            using var nextFrame = depacketiser.ProcessRTPPayload(new byte[] { 0x10, 0x30, 9 }, 0, 180000, 1, out _);
            Assert.Equal(frame, result.ToArray());
            Assert.Equal(new byte[] { 0x32, 1, 9 }, nextFrame.ToArray());
            result.Dispose();
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x01, 0x34 })] // Truncated extension header.
        [InlineData(new byte[] { 0x00, 0xff, 0xff, 0xff, 0xff, 0x07 })] // Length addition would overflow.
        [InlineData(new byte[] { 0x20, 0xff, 0xff, 0xff, 0xff, 0x07 })] // W=2 length addition would overflow.
        [InlineData(new byte[] { 0x00, 0xff, 0xff, 0xff, 0xff, 0x0f })] // Length exceeds int.
        public void DepacketiserIgnoresMalformedObuElements(byte[] payload)
        {
            Assert.Null(new AV1Depacketiser().ProcessRTPPayload(payload, 0, 90000, 1, out _));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PayloadLimitsTooSmallForAnExtendedHeaderAreRejected(int maxPayloadSize)
        {
            Assert.Throws<ArgumentException>(() => AV1Packetiser.Packetize(new byte[] { 0x34, 0x28 }.AsSpan(), maxPayloadSize));
        }

        private static void AssertPacketLimitsAndRoundTrip(byte[] temporalUnit, int maxPayloadSize, bool startsSequence)
        {
            var packets = AV1Packetiser.Packetize(temporalUnit.AsSpan(), maxPayloadSize);
            var depacketiser = new AV1Depacketiser();
            byte[] reconstructed = null;
            bool keyFrame = false;
            for (ushort i = 0; i < packets.Count; i++)
            {
                var packet = packets[i];
                Assert.InRange(packet.Payload.Length, 3, maxPayloadSize);
                Assert.Equal(i == packets.Count - 1, packet.IsLast);
                var header = (AV1AggregationHeaderFlags)packet.Payload[0];
                Assert.Equal(startsSequence && i == 0, header.HasFlag(AV1AggregationHeaderFlags.NewCodedVideoSequence));
                Assert.Equal(i > 0 && ((AV1AggregationHeaderFlags)packets[i - 1].Payload[0]).HasFlag(AV1AggregationHeaderFlags.LastObuContinues),
                    header.HasFlag(AV1AggregationHeaderFlags.FirstObuIsContinuation));
                Assert.Equal(0, packet.Payload[0] & AGGREGATION_HEADER_NON_FLAG_MASK);
                int offset = AV1Packetiser.AV1_AGGREGATION_HEADER_LENGTH;
                bool continuation = header.HasFlag(AV1AggregationHeaderFlags.FirstObuIsContinuation);
                while (offset < packet.Payload.Length)
                {
                    Assert.True(AV1Packetiser.TryReadLeb128(packet.Payload, ref offset, out int length, out _));
                    Assert.InRange(length, 1, packet.Payload.Length - offset);
                    // Every new OBU omits the internal size field on the wire.
                    if (!continuation)
                    {
                        Assert.False(((AV1ObuHeaderFlags)packet.Payload[offset]).HasFlag(AV1ObuHeaderFlags.HasSizeField));
                    }
                    offset += length;
                    continuation = false;
                }
                Assert.Equal(packet.Payload.Length, offset);
                reconstructed = depacketiser.ProcessRTPPayload(packet.Payload, i, 90000, packet.IsLast ? 1 : 0, out keyFrame)?.ToArray();
            }
            Assert.False(((AV1AggregationHeaderFlags)packets.Last().Payload[0]).HasFlag(AV1AggregationHeaderFlags.LastObuContinues));
            Assert.Equal(startsSequence, keyFrame);
            Assert.Equal(temporalUnit, reconstructed);
        }

        [Fact]
        public void PacketiseAndDepacketiseAggregatedAv1FrameUnitTest()
        {
            var temporalDelimiter = CreateObu(AV1Packetiser.AV1ObuType.TemporalDelimiter, Array.Empty<byte>());
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 0x01, 0x02, 0x03 });
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[] { 0x11, 0x22, 0x33, 0x44 });
            var sourceTemporalUnit = temporalDelimiter.Concat(sequenceHeader).Concat(frame).ToArray();
            var expectedTemporalUnit = sequenceHeader.Concat(frame).ToArray();

            var packets = AV1Packetiser.Packetize(sourceTemporalUnit.AsSpan(), 1200);

            Assert.Single(packets);

            var framer = new RtpVideoFramer(VideoCodecsEnum.AV1, 4096);
            byte[] reconstructedFrame = null;

            for (ushort i = 0; i < packets.Count; i++)
            {
                var packet = new RTPPacket
                {
                    Header = new RTPHeader
                    {
                        SequenceNumber = i,
                        Timestamp = 90000,
                        MarkerBit = packets[i].IsLast ? 1 : 0
                    },
                    Payload = packets[i].Payload
                };

                reconstructedFrame = framer.GotRtpPacket(packet);
            }

            Assert.NotNull(reconstructedFrame);
            Assert.Equal(expectedTemporalUnit, reconstructedFrame);
        }

        [Fact]
        public void PacketiseAndDepacketiseFragmentedAv1FrameUnitTest()
        {
            var payload = Enumerable.Range(0, 1500).Select(x => (byte)(x % 251)).ToArray();
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, payload);

            var packets = AV1Packetiser.Packetize(frame.AsSpan(), 200);

            Assert.True(packets.Count > 1);

            var framer = new RtpVideoFramer(VideoCodecsEnum.AV1, 4096);
            byte[] reconstructedFrame = null;

            for (ushort i = 0; i < packets.Count; i++)
            {
                var packet = new RTPPacket
                {
                    Header = new RTPHeader
                    {
                        SequenceNumber = i,
                        Timestamp = 180000,
                        MarkerBit = packets[i].IsLast ? 1 : 0
                    },
                    Payload = packets[i].Payload
                };

                reconstructedFrame = framer.GotRtpPacket(packet);
            }

            Assert.NotNull(reconstructedFrame);
            Assert.Equal(frame, reconstructedFrame);
        }

        [Theory]
        [InlineData(16383, 2)]
        [InlineData(16384, 3)]
        [InlineData(65536, 3)]
        public void LargeFragmentedObuRoundTripRestoresSizeField(int payloadLength, int expectedSizeBytes)
        {
            var payload = Enumerable.Range(0, payloadLength).Select(x => (byte)(x % 251)).ToArray();
            var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, payload);
            var packets = AV1Packetiser.Packetize(frame.AsSpan(), 1200);
            var framer = new RtpVideoFramer(VideoCodecsEnum.AV1, frame.Length);
            byte[] reconstructedFrame = null;

            Assert.True(packets.Count > 1);
            for (ushort i = 0; i < packets.Count; i++)
            {
                Assert.InRange(packets[i].Payload.Length, 3, 1200);
                reconstructedFrame = framer.GotRtpPacket(new RTPPacket
                {
                    Header = new RTPHeader
                    {
                        SequenceNumber = i,
                        Timestamp = 180000,
                        MarkerBit = packets[i].IsLast ? 1 : 0
                    },
                    Payload = packets[i].Payload
                });
                if (i < packets.Count - 1)
                {
                    Assert.Null(reconstructedFrame);
                }
            }

            Assert.NotNull(reconstructedFrame);
            Assert.Equal(frame, reconstructedFrame);
            Assert.Equal(AV1Packetiser.AV1ObuType.Frame, AV1Packetiser.GetObuType(reconstructedFrame));
            Assert.True(((AV1ObuHeaderFlags)reconstructedFrame[0]).HasFlag(AV1ObuHeaderFlags.HasSizeField));
            int offset = AV1Packetiser.OBU_HEADER_LENGTH;
            Assert.True(AV1Packetiser.TryReadLeb128(reconstructedFrame, ref offset, out int size, out int sizeBytes));
            Assert.Equal(payloadLength, size);
            Assert.Equal(expectedSizeBytes, sizeBytes);
            Assert.Equal(payload, reconstructedFrame.Skip(offset).ToArray());
        }

        /// <summary>
        /// Temporal units of several kinds with a payload limit each, and the payloads master's packetiser made for
        /// them at commit e774801c5: their lengths, and the SHA-256 of the payloads one after another.
        /// </summary>
        public static IEnumerable<object[]> BufferWriterCases()
        {
            var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 1, 2, 3 });
            var frame1500 = CreateObu(AV1Packetiser.AV1ObuType.Frame, Enumerable.Range(0, 1500).Select(x => (byte)x).ToArray());
            var extendedFrame = new byte[] { 0x36, 0x28, 0x05, 1, 2, 3, 4, 5 }; // Frame with an extension header.
            var smallObus = Enumerable.Range(1, 30).SelectMany(i => CreateObu(AV1Packetiser.AV1ObuType.Metadata, new byte[i])).ToArray();
            var padding = CreateObu(AV1Packetiser.AV1ObuType.Padding, new byte[20]);
            var unsized = new byte[] { 0x30 }.Concat(new byte[700]).ToArray(); // Frame without a size field.

            yield return new object[] { "SequenceHeaderAndFrame", sequenceHeader.Concat(frame1500).ToArray(), 200,
                new[] { 200, 200, 200, 200, 200, 200, 200, 129 }, "883E958762D9ADC6B070AC4D33E96FB1E09214208BB15B863E13E27C788B2664" };
            yield return new object[] { "Leb128Boundary", CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[127]), 130,
                new[] { 129, 3 }, "5D28B09C380F01FEE6C8576439EDA59EBA428D9B340173AF663F31EACB312587" };
            yield return new object[] { "ExtensionHeaderSmallestPayload", sequenceHeader.Concat(extendedFrame).ToArray(), 4,
                new[] { 4, 4, 4, 4, 4, 3 }, "04E86B5229C5078CCF8EE9DE96D38BDD9C3FF3A8FA5479B1144B41B389C1786A" };
            yield return new object[] { "ExtensionHeader", sequenceHeader.Concat(extendedFrame).ToArray(), 7,
                new[] { 6, 7, 4 }, "C8CC8EA327023057140AF6CA8AAB69E395B7594CA826DE1A07BE501E790268F7" };
            yield return new object[] { "SmallObus", smallObus, 64,
                new[] { 64, 64, 63, 64, 64, 64, 64, 64, 27 }, "FD519B2185BCCFE081E8E05F76B6BCD29B5709DBE87FEA68988756C8C7037245" };
            yield return new object[] { "SkippedObus", padding.Concat(frame1500).ToArray(), 1200,
                new[] { 1200, 307 }, "BA32478281461B18BEFD4A8AF225EE1EF8F3043DDD33BCAC964F140152FE0D24" };
            yield return new object[] { "UnsizedLastObu", sequenceHeader.Concat(unsized).ToArray(), 100,
                new[] { 100, 100, 100, 100, 100, 100, 100, 22 }, "5656C9F565574B7C43F21AB7515300CE1AE17E23C0FDA4DAF4D1AF84C472F2C7" };

            // Limits larger than any payload of the temporal unit could need: everything goes in one payload.
            yield return new object[] { "LargestLimit", sequenceHeader.Concat(frame1500).ToArray(), int.MaxValue,
                new[] { 1509 }, "8CDDD892FBA7B8CCEA53843399D69E325FDC0E0C4A5F619D025A496C4D1983DD" };
            yield return new object[] { "VeryLargeLimit", smallObus, 100_000_000,
                new[] { 526 }, "BF79722294835F71955468815EC1BDD293BC7730B92DA4A09564B9E669B68922" };
        }

        public static IEnumerable<object[]> BufferWriterLimitCases() => BufferWriterCases().Select(c => new[] { c[0], c[1], c[2] });

        /// <summary>
        /// Both overloads make exactly the payloads master made, the list one with only its last payload marked, and
        /// the buffer writer one writing them one after another with their lengths in order.
        /// </summary>
        [Theory]
        [MemberData(nameof(BufferWriterCases))]
        public void BothOverloadsMakeThePayloadsMasterMade(string name, byte[] temporalUnit, int maxPayloadSize, int[] expectedLengths, string expectedDigest)
        {
            var packets = AV1Packetiser.Packetize(temporalUnit.AsSpan(), maxPayloadSize);
            var output = new RecordingBufferWriter();
            var payloadLengths = new List<int>();

            AV1Packetiser.Packetize(temporalUnit, maxPayloadSize, output, payloadLengths);

            Assert.True(packets.Count > 0, name);
            Assert.Equal(expectedLengths, packets.Select(p => p.Payload.Length));
            Assert.Equal(expectedDigest, Sha256Hex(packets.SelectMany(p => p.Payload).ToArray()));
            Assert.Equal(packets.Select((p, i) => i == packets.Count - 1), packets.Select(p => p.IsLast));
            Assert.Equal(expectedLengths, payloadLengths);
            Assert.Equal(expectedDigest, Sha256Hex(output.WrittenSpan.ToArray()));
        }

        /// <summary>
        /// Each payload is written with one GetSpan and one Advance of its length. The span asked for is the maximum
        /// payload size, or the most a payload of the temporal unit can need when that is less: the aggregation
        /// header, at most twice the temporal unit, and the 8 bytes of the longest leb128.
        /// </summary>
        [Theory]
        [MemberData(nameof(BufferWriterLimitCases))]
        public void BufferWriterIsAskedForOneSpanPerPayload(string name, byte[] temporalUnit, int maxPayloadSize)
        {
            var output = new RecordingBufferWriter();
            var payloadLengths = new List<int>();

            AV1Packetiser.Packetize(temporalUnit, maxPayloadSize, output, payloadLengths);

            int expectedSize = (int)Math.Min(maxPayloadSize, 1 + 2L * temporalUnit.Length + 8);
            Assert.True(payloadLengths.Count > 0, name);
            Assert.Equal(Enumerable.Repeat(expectedSize, payloadLengths.Count), output.SizeHints);
            Assert.Equal(payloadLengths, output.Advances);
        }

        /// <summary>
        /// The payloads go after what the writer already holds, and their lengths after those already in the list.
        /// </summary>
        [Fact]
        public void BufferWriterOutputIsAppendedToWhatIsAlreadyThere()
        {
            var temporalUnit = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[300]);
            var expected = AV1Packetiser.Packetize(temporalUnit.AsSpan(), 128);
            var output = new RecordingBufferWriter();
            new byte[] { 9, 9, 9 }.CopyTo(output.GetSpan(3));
            output.Advance(3);
            var payloadLengths = new List<int> { 42 };

            AV1Packetiser.Packetize(temporalUnit, 128, output, payloadLengths);

            Assert.Equal(new[] { 42 }.Concat(expected.Select(p => p.Payload.Length)), payloadLengths);
            Assert.Equal(new byte[] { 9, 9, 9 }.Concat(expected.SelectMany(p => p.Payload)).ToArray(), output.WrittenSpan.ToArray());
        }

        [Fact]
        public void BufferWriterNullArgumentsAreRejected()
        {
            var temporalUnit = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[10]);

            var noOutput = Assert.Throws<ArgumentNullException>(() => AV1Packetiser.Packetize(temporalUnit, 1200, null, new List<int>()));
            var noLengths = Assert.Throws<ArgumentNullException>(() => AV1Packetiser.Packetize(temporalUnit, 1200, new RecordingBufferWriter(), null));

            Assert.Equal("output", noOutput.ParamName);
            Assert.Equal("payloadLengths", noLengths.ParamName);
        }

        /// <summary>
        /// An empty temporal unit, or one with nothing to send, writes nothing. Only a temporal unit with something in
        /// it has its maximum payload size checked, as with the list overload.
        /// </summary>
        [Fact]
        public void BufferWriterTemporalUnitWithNothingToSendWritesNothing()
        {
            var output = new RecordingBufferWriter();
            var payloadLengths = new List<int>();
            var delimiterAndPadding = CreateObu(AV1Packetiser.AV1ObuType.TemporalDelimiter, new byte[0])
                .Concat(CreateObu(AV1Packetiser.AV1ObuType.Padding, new byte[8])).ToArray();

            AV1Packetiser.Packetize(ReadOnlySpan<byte>.Empty, 1, output, payloadLengths);
            AV1Packetiser.Packetize(delimiterAndPadding, 1200, output, payloadLengths);

            Assert.Equal(0, output.WrittenCount);
            Assert.Empty(payloadLengths);
            Assert.Throws<ArgumentException>(() => AV1Packetiser.Packetize(delimiterAndPadding, 3, output, payloadLengths));
        }

        [Theory]
        [InlineData(new byte[] { 0x36 })] // Missing extension header.
        [InlineData(new byte[] { 0x32 })] // Missing size.
        [InlineData(new byte[] { 0x32, 0x02, 0x01 })] // Truncated payload.
        public void BufferWriterMalformedObusAreRejected(byte[] temporalUnit)
        {
            Assert.Throws<FormatException>(() => AV1Packetiser.Packetize(temporalUnit, 1200, new RecordingBufferWriter(), new List<int>()));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static byte[] CreateObu(AV1Packetiser.AV1ObuType obuType, byte[] payload)
        {
            byte obuHeader = (byte)(((byte)obuType << OBU_TYPE_SHIFT) | (byte)AV1ObuHeaderFlags.HasSizeField);
            var leb128Size = AV1Packetiser.WriteLeb128(payload.Length);
            return new[] { obuHeader }.Concat(leb128Size).Concat(payload).ToArray();
        }
    }
}
