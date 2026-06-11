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
using System.Buffers;
using System.Linq;
using CommunityToolkit.HighPerformance.Buffers;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class AV1PacketiserUnitTest
{
    [Fact]
    public void PacketiseAndDepacketiseAggregatedAv1FrameUnitTest()
    {
        // Arrange

        var temporalDelimiter = CreateObu(AV1Packetiser.AV1ObuType.TemporalDelimiter, Array.Empty<byte>());
        var sequenceHeader = CreateObu(AV1Packetiser.AV1ObuType.SequenceHeader, new byte[] { 0x01, 0x02, 0x03 });
        var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, new byte[] { 0x11, 0x22, 0x33, 0x44 });
        var sourceTemporalUnit = temporalDelimiter.Concat(sequenceHeader).Concat(frame).ToArray();
        var expectedTemporalUnit = sequenceHeader.Concat(frame).ToArray();

        // Act

        var packets = AV1Packetiser.Packetize(sourceTemporalUnit, 1200);

        // Assert

        Assert.Single(packets);

        var framer = new RtpVideoFramer(VideoCodecsEnum.AV1, 4096);
        using var bufferWriter = new ArrayPoolBufferWriter<byte>();
        bool gotFrame = false;

        for (ushort i = 0; i < packets.Count; i++)
        {
            var header = new RTPHeader
            {
                SequenceNumber = i,
                Timestamp = 90000,
                MarkerBit = packets[i].IsLast ? 1 : 0
            };
            var packet = new RTPPacket(header, packets[i].Payload);

            gotFrame = framer.GotRtpPacket(bufferWriter, packet);
        }

        Assert.True(gotFrame);
        Assert.Equal(expectedTemporalUnit, bufferWriter.WrittenMemory.ToArray());
    }

    [Fact]
    public void PacketiseAndDepacketiseFragmentedAv1FrameUnitTest()
    {
        // Arrange

        var payload = Enumerable.Range(0, 1500).Select(x => (byte)(x % 251)).ToArray();
        var frame = CreateObu(AV1Packetiser.AV1ObuType.Frame, payload);

        // Act

        var packets = AV1Packetiser.Packetize(frame, 200);

        // Assert

        Assert.True(packets.Count > 1);

        var framer = new RtpVideoFramer(VideoCodecsEnum.AV1, 4096);
        using var bufferWriter = new ArrayPoolBufferWriter<byte>();
        bool gotFrame = false;

        for (ushort i = 0; i < packets.Count; i++)
        {
            var header = new RTPHeader
            {
                SequenceNumber = i,
                Timestamp = 180000,
                MarkerBit = packets[i].IsLast ? 1 : 0
            };
            var packet = new RTPPacket(header, packets[i].Payload);

            gotFrame = framer.GotRtpPacket(bufferWriter, packet);
        }

        Assert.True(gotFrame);
        Assert.Equal(frame, bufferWriter.WrittenMemory.ToArray());
    }

    private static byte[] CreateObu(AV1Packetiser.AV1ObuType obuType, byte[] payload)
    {
        byte obuHeader = (byte)(((byte)obuType << 3) | 0x02);
        int leb128Length = AV1Packetiser.GetLeb128Length(payload.Length);
        var obu = new byte[1 + leb128Length + payload.Length];
        obu[0] = obuHeader;
        AV1Packetiser.WriteLeb128(obu.AsSpan(1), payload.Length);
        payload.CopyTo(obu.AsSpan(1 + leb128Length));
        return obu;
    }
}
