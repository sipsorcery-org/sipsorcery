//-----------------------------------------------------------------------------
// Filename: AV1Depacketiser.cs
//
// Description: Reassembles RTP payloads using the AV1 RTP payload format.
//
// Based on the Alliance for Open Media RTP Payload Format for AV1:
// https://aomediacodec.github.io/av1-rtp-spec/
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
using System.IO;

namespace SIPSorcery.Net;

public class AV1Depacketiser
{
    private const byte OBU_ELEMENT_COUNT_MASK = 0x30; // W field.
    private const int OBU_ELEMENT_COUNT_SHIFT = 4;
    private const int SEQUENCE_NUMBER_WRAP_WINDOW = 2000;

    private uint _previousTimestamp;
    private readonly List<KeyValuePair<int, byte[]>> _temporaryRtpPayloads = new List<KeyValuePair<int, byte[]>>();

    public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markerBit, out bool isKeyFrame)
    {
        if (_previousTimestamp != timestamp && _previousTimestamp > 0)
        {
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
        }

        _temporaryRtpPayloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtpPayload));

        if (markerBit == 1)
        {
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort((a, b) =>
                    (Math.Abs(b.Key - a.Key) > (ushort.MaxValue - SEQUENCE_NUMBER_WRAP_WINDOW)) ? -a.Key.CompareTo(b.Key) : a.Key.CompareTo(b.Key));
            }

            byte[] frame = ProcessAV1PayloadFrame(_temporaryRtpPayloads, out isKeyFrame);
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;

            if (frame == null)
            {
                return null;
            }

            return new MemoryStream(frame);
        }

        isKeyFrame = false;
        _previousTimestamp = timestamp;
        return null;
    }

    protected virtual byte[] ProcessAV1PayloadFrame(List<KeyValuePair<int, byte[]>> rtpPayloads, out bool isKeyFrame)
    {
        var fragments = new List<ReadOnlyMemory<byte>>();
        var completedObus = new List<CompletedObu>();
        var packetElements = new List<ReadOnlyMemory<byte>>();
        int pendingObuStart = 0;
        isKeyFrame = false;

        foreach (var rtpPayload in rtpPayloads)
        {
            var payload = rtpPayload.Value;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            var header = (AV1AggregationHeaderFlags)payload[0];
            bool firstObuIsContinuation = header.HasFlag(AV1AggregationHeaderFlags.FirstObuIsContinuation);
            bool lastObuContinues = header.HasFlag(AV1AggregationHeaderFlags.LastObuContinues);
            int obuElementCount = (payload[0] & OBU_ELEMENT_COUNT_MASK) >> OBU_ELEMENT_COUNT_SHIFT;

            if (header.HasFlag(AV1AggregationHeaderFlags.NewCodedVideoSequence))
            {
                isKeyFrame = true;
            }

            packetElements.Clear();
            ParseObuElements(payload, obuElementCount, packetElements);
            AddPacketElements(packetElements, firstObuIsContinuation, lastObuContinues,
                fragments, ref pendingObuStart, completedObus);
        }

        if (completedObus.Count == 0)
        {
            return null;
        }

        int totalLength = 0;
        for (int i = 0; i < completedObus.Count; i++)
        {
            totalLength = checked(totalLength + completedObus[i].OutputLength);
        }

        var frame = new byte[totalLength];
        int offset = 0;
        for (int i = 0; i < completedObus.Count; i++)
        {
            var obu = completedObus[i];
            obu.CopyTo(fragments, frame.AsSpan(offset, obu.OutputLength));
            offset += obu.OutputLength;
        }

        return frame;
    }

    private static void ParseObuElements(ReadOnlyMemory<byte> payload, int obuElementCount,
        List<ReadOnlyMemory<byte>> elements)
    {
        int offset = AV1Packetiser.AV1_AGGREGATION_HEADER_LENGTH;
        for (int elementIndex = 0; offset < payload.Length && (obuElementCount == 0 || elementIndex < obuElementCount); elementIndex++)
        {
            int length;
            if (obuElementCount != 0 && elementIndex == obuElementCount - 1)
            {
                length = payload.Length - offset;
            }
            else if (!AV1Packetiser.TryReadLeb128(payload.Span, ref offset, out length, out _))
            {
                break;
            }

            if (length > payload.Length - offset)
            {
                break;
            }

            elements.Add(payload.Slice(offset, length));
            offset += length;
        }
    }

    private static void AddPacketElements(List<ReadOnlyMemory<byte>> elements,
        bool firstObuIsContinuation, bool lastObuContinues, List<ReadOnlyMemory<byte>> fragments,
        ref int pendingObuStart, List<CompletedObu> completedObus)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (!(i == 0 && firstObuIsContinuation))
            {
                // A new OBU cannot continue an unfinished OBU from an earlier packet.
                pendingObuStart = fragments.Count;
            }

            fragments.Add(elements[i]);
            if (i < elements.Count - 1 || !lastObuContinues)
            {
                AddCompletedObu(fragments, pendingObuStart, completedObus);
                pendingObuStart = fragments.Count;
            }
        }
    }

    private static void AddCompletedObu(List<ReadOnlyMemory<byte>> fragments, int firstFragment,
        List<CompletedObu> completedObus)
    {
        int fragmentCount = fragments.Count - firstFragment;
        int length = 0;
        ReadOnlySpan<byte> header = default;
        for (int i = firstFragment; i < fragments.Count; i++)
        {
            var fragment = fragments[i];
            if (header.IsEmpty && !fragment.IsEmpty)
            {
                header = fragment.Span;
            }
            length = checked(length + fragment.Length);
        }

        if (length == 0)
        {
            return;
        }

        var obuType = AV1Packetiser.GetObuType(header);
        if (obuType is AV1Packetiser.AV1ObuType.TemporalDelimiter or AV1Packetiser.AV1ObuType.TileList)
        {
            return;
        }

        var obuHeader = (AV1ObuHeaderFlags)header[0];
        int headerLength = AV1Packetiser.OBU_HEADER_LENGTH +
            (obuHeader.HasFlag(AV1ObuHeaderFlags.Extension) ? AV1Packetiser.OBU_EXTENSION_HEADER_LENGTH : 0);
        if (length < headerLength)
        {
            return;
        }

        int sizeFieldLength = obuHeader.HasFlag(AV1ObuHeaderFlags.HasSizeField)
            ? 0 : AV1Packetiser.GetLeb128Size(length - headerLength);
        completedObus.Add(new CompletedObu(firstFragment, fragmentCount, length, headerLength, sizeFieldLength));
    }

    private readonly struct CompletedObu
    {
        private int FirstFragment { get; }
        private int FragmentCount { get; }
        private int Length { get; }
        private int HeaderLength { get; }
        private int SizeFieldLength { get; }
        public int OutputLength => checked(Length + SizeFieldLength);

        public CompletedObu(int firstFragment, int fragmentCount, int length, int headerLength, int sizeFieldLength)
        {
            FirstFragment = firstFragment;
            FragmentCount = fragmentCount;
            Length = length;
            HeaderLength = headerLength;
            SizeFieldLength = sizeFieldLength;
        }

        public void CopyTo(List<ReadOnlyMemory<byte>> fragments, Span<byte> destination)
        {
            int sourceOffset = 0;
            for (int i = FirstFragment; i < FirstFragment + FragmentCount; i++)
            {
                var source = fragments[i].Span;
                int headerBytes = Math.Min(source.Length, Math.Max(0, HeaderLength - sourceOffset));
                source.Slice(0, headerBytes).CopyTo(destination.Slice(sourceOffset));
                source.Slice(headerBytes).CopyTo(destination.Slice(sourceOffset + headerBytes + SizeFieldLength));
                sourceOffset += source.Length;
            }

            if (SizeFieldLength > 0)
            {
                // Restore obu_size directly in the final low-overhead bitstream.
                destination[0] = (byte)((AV1ObuHeaderFlags)destination[0] | AV1ObuHeaderFlags.HasSizeField);
                AV1Packetiser.WriteLeb128(Length - HeaderLength, destination.Slice(HeaderLength, SizeFieldLength));
            }
        }
    }
}
