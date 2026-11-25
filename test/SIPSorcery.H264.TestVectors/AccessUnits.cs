//-----------------------------------------------------------------------------
// Filename: AccessUnits.cs
//
// Description: Splits an Annex B stream into access units, the unit
// H264Codec.DecodeVideo takes and an RTP receiver assembles from the packets
// that share a timestamp. Also packetises an access unit into RTP payloads, as
// a WebRTC sender does, for the depacketiser round trip tests.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using SIPSorcery.Net;

namespace SIPSorcery.H264.TestVectors;

public static class AccessUnits
{
    /// <summary>A NAL unit in a stream: where its start code begins, and its header and payload.</summary>
    public readonly record struct Nal(int StartCode, int Offset, int Length)
    {
        public int End => Offset + Length;
    }

    /// <summary>Finds the NAL units of an Annex B stream, after 3 or 4 byte start codes.</summary>
    public static List<Nal> FindNals(ReadOnlySpan<byte> stream)
    {
        var starts = new List<(int StartCode, int Offset)>();
        for (int i = 0; i + 2 < stream.Length; i++)
        {
            if (stream[i] == 0 && stream[i + 1] == 0 && stream[i + 2] == 1)
            {
                int sc = (i > 0 && stream[i - 1] == 0) ? i - 1 : i;
                starts.Add((sc, i + 3));
                i += 2;
            }
        }

        var nals = new List<Nal>(starts.Count);
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1].StartCode : stream.Length;
            // Trailing zero bytes belong to no NAL unit.
            while (end > starts[k].Offset && stream[end - 1] == 0)
            {
                end--;
            }
            if (end > starts[k].Offset)
            {
                nals.Add(new Nal(starts[k].StartCode, starts[k].Offset, end - starts[k].Offset));
            }
        }
        return nals;
    }

    /// <summary>
    /// Splits an Annex B stream into access units (H.264 7.4.1.2.3): a new one starts at an
    /// access unit delimiter, at an SEI, SPS, PPS or prefix NAL after the current unit's first
    /// slice, and at a slice whose first_mb_in_slice is 0 once the current unit has a slice.
    /// Constrained Baseline has no arbitrary slice order, so a picture's first slice is the one
    /// starting at macroblock 0. Each access unit keeps its bytes as they are in the stream.
    /// </summary>
    public static List<byte[]> Split(byte[] stream)
    {
        var aus = new List<byte[]>();
        List<Nal> nals = FindNals(stream);
        int auStart = -1;
        bool auHasSlice = false;

        for (int k = 0; k < nals.Count; k++)
        {
            Nal nal = nals[k];
            int type = stream[nal.Offset] & 0x1f;
            bool isSlice = type == 1 || type == 5;
            bool startsNew = auStart >= 0 && (
                type == 9 ||
                (auHasSlice && (type == 6 || type == 7 || type == 8 || (type >= 14 && type <= 18))) ||
                (auHasSlice && isSlice && FirstMbInSliceIsZero(stream, nal)));

            if (startsNew)
            {
                aus.Add(stream.AsSpan(auStart, nal.StartCode - auStart).ToArray());
                auStart = -1;
                auHasSlice = false;
            }
            if (auStart < 0)
            {
                auStart = nal.StartCode;
            }
            auHasSlice |= isSlice;
        }

        if (auStart >= 0)
        {
            aus.Add(stream.AsSpan(auStart).ToArray());
        }
        return aus;
    }

    // first_mb_in_slice is ue(v), the first field after the NAL header, so it is 0 exactly when
    // the first bit is 1. A slice too short to say is not a picture's first.
    private static bool FirstMbInSliceIsZero(byte[] stream, Nal nal) =>
        nal.Length > 1 && (stream[nal.Offset + 1] & 0x80) != 0;

    /// <summary>
    /// Packetises an access unit as RTP payloads with packetization mode 1, as a browser does:
    /// SPS and PPS together in a STAP-A, other NAL units alone when they fit in
    /// <paramref name="maxPayload"/>, otherwise in FU-A fragments. The marker bit goes on the
    /// last packet.
    /// </summary>
    public static List<(byte[] Payload, bool Marker)> Packetise(byte[] accessUnit, int maxPayload)
    {
        var packets = new List<byte[]>();
        List<Nal> nals = FindNals(accessUnit);

        for (int k = 0; k < nals.Count; k++)
        {
            ReadOnlySpan<byte> nal = accessUnit.AsSpan(nals[k].Offset, nals[k].Length);
            int type = nal[0] & 0x1f;

            if (type == 7 && k + 1 < nals.Count && (accessUnit[nals[k + 1].Offset] & 0x1f) == 8 &&
                nals[k].Length + nals[k + 1].Length + 5 <= maxPayload)
            {
                ReadOnlySpan<byte> pps = accessUnit.AsSpan(nals[k + 1].Offset, nals[k + 1].Length);
                var stap = new byte[1 + 2 + nal.Length + 2 + pps.Length];
                // F is 0, NRI the larger of the two units' (RFC 6184 5.7).
                stap[0] = (byte)(Math.Max(nal[0] & 0x60, pps[0] & 0x60) | 24);
                int pos = PutStapUnit(stap, 1, nal);
                PutStapUnit(stap, pos, pps);
                packets.Add(stap);
                k++;
            }
            else if (nal.Length <= maxPayload)
            {
                packets.Add(nal.ToArray());
            }
            else
            {
                // As VideoStream.SendH26XNal: the NAL header is dropped and rebuilt from the FU
                // headers, and a NAL just over the limit is split in two halves.
                ReadOnlySpan<byte> rest = nal.Slice(1);
                int size = rest.Length > maxPayload - 2 ? maxPayload - 2 : (rest.Length + 1) / 2;
                for (int offset = 0; offset < rest.Length; offset += size)
                {
                    int len = Math.Min(size, rest.Length - offset);
                    byte[] fu = H264Packetiser.GetH264RtpHeader(nal[0], offset == 0, offset + len >= rest.Length);
                    var packet = new byte[fu.Length + len];
                    fu.CopyTo(packet, 0);
                    rest.Slice(offset, len).CopyTo(packet.AsSpan(fu.Length));
                    packets.Add(packet);
                }
            }
        }

        var result = new List<(byte[], bool)>(packets.Count);
        for (int i = 0; i < packets.Count; i++)
        {
            result.Add((packets[i], i == packets.Count - 1));
        }
        return result;
    }

    private static int PutStapUnit(byte[] stap, int pos, ReadOnlySpan<byte> unit)
    {
        stap[pos++] = (byte)(unit.Length >> 8);
        stap[pos++] = (byte)unit.Length;
        unit.CopyTo(stap.AsSpan(pos));
        return pos + unit.Length;
    }
}
