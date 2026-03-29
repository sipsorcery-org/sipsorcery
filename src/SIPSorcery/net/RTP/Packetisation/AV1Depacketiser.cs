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
    private const byte Z_MASK = 0x80;
    private const byte Y_MASK = 0x40;
    private const byte N_MASK = 0x08;

    private uint _previousTimestamp;
    private readonly List<KeyValuePair<int, byte[]>> _temporaryRtpPayloads = new List<KeyValuePair<int, byte[]>>();
    private readonly MemoryStream _fragmentedObu = new MemoryStream();

    public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markerBit, out bool isKeyFrame)
    {
        if (_previousTimestamp != timestamp && _previousTimestamp > 0)
        {
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
            _fragmentedObu.SetLength(0);
        }

        _temporaryRtpPayloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtpPayload));

        if (markerBit == 1)
        {
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort((a, b) =>
                    (Math.Abs(b.Key - a.Key) > (0xFFFF - 2000)) ? -a.Key.CompareTo(b.Key) : a.Key.CompareTo(b.Key));
            }

            byte[] frame = ProcessAV1PayloadFrame(_temporaryRtpPayloads, out isKeyFrame);
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
            _fragmentedObu.SetLength(0);

            if (frame == null)
            {
                return null;
            }

            var frameStream = new MemoryStream(frame.Length);
            frameStream.Write(frame, 0, frame.Length);
            frameStream.Position = 0;
            return frameStream;
        }

        isKeyFrame = false;
        _previousTimestamp = timestamp;
        return null;
    }

    protected virtual byte[] ProcessAV1PayloadFrame(List<KeyValuePair<int, byte[]>> rtpPayloads, out bool isKeyFrame)
    {
        var obuElements = new List<byte[]>();
        isKeyFrame = false;

        foreach (var rtpPayload in rtpPayloads)
        {
            var payload = rtpPayload.Value;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            bool z = (payload[0] & Z_MASK) != 0;
            bool y = (payload[0] & Y_MASK) != 0;
            int w = (payload[0] >> 4) & 0x03;
            bool n = (payload[0] & N_MASK) != 0;

            if (n)
            {
                isKeyFrame = true;
            }

            var packetElements = ParseObuElements(payload, w);
            AddPacketElements(packetElements, z, y, obuElements);
        }

        if (_fragmentedObu.Length > 0)
        {
            _fragmentedObu.SetLength(0);
        }

        if (obuElements.Count == 0)
        {
            return null;
        }

        int totalLength = 0;
        for (int i = 0; i < obuElements.Count; i++)
        {
            totalLength += obuElements[i].Length;
        }

        var frame = new byte[totalLength];
        int offset = 0;
        for (int i = 0; i < obuElements.Count; i++)
        {
            Buffer.BlockCopy(obuElements[i], 0, frame, offset, obuElements[i].Length);
            offset += obuElements[i].Length;
        }

        return frame;
    }

    private List<byte[]> ParseObuElements(byte[] payload, int w)
    {
        var obuElements = new List<byte[]>();
        int offset = 1;

        if (w == 0)
        {
            while (offset < payload.Length)
            {
                if (!AV1Packetiser.TryReadLeb128(payload, ref offset, out int obuElementLength, out _))
                {
                    break;
                }

                if (offset + obuElementLength > payload.Length)
                {
                    break;
                }

                var obuElement = new byte[obuElementLength];
                Buffer.BlockCopy(payload, offset, obuElement, 0, obuElementLength);
                offset += obuElementLength;
                obuElements.Add(obuElement);
            }
        }
        else
        {
            for (int elementIndex = 0; elementIndex < w && offset < payload.Length; elementIndex++)
            {
                int obuElementLength;
                if (elementIndex == w - 1)
                {
                    obuElementLength = payload.Length - offset;
                }
                else if (!AV1Packetiser.TryReadLeb128(payload, ref offset, out obuElementLength, out _))
                {
                    break;
                }

                if (offset + obuElementLength > payload.Length)
                {
                    break;
                }

                var obuElement = new byte[obuElementLength];
                Buffer.BlockCopy(payload, offset, obuElement, 0, obuElementLength);
                offset += obuElementLength;
                obuElements.Add(obuElement);
            }
        }

        return obuElements;
    }

    private void AddPacketElements(List<byte[]> packetElements, bool z, bool y, List<byte[]> completedObus)
    {
        if (packetElements.Count == 0)
        {
            return;
        }

        int startIndex = 0;
        int endExclusive = packetElements.Count;

        if (z)
        {
            _fragmentedObu.Write(packetElements[0], 0, packetElements[0].Length);

            if (!(y && packetElements.Count == 1))
            {
                AddCompletedObu(_fragmentedObu.ToArray(), completedObus);
                _fragmentedObu.SetLength(0);
            }

            startIndex = 1;
        }

        if (y && packetElements.Count > startIndex)
        {
            endExclusive = packetElements.Count - 1;
        }

        for (int i = startIndex; i < endExclusive; i++)
        {
            AddCompletedObu(packetElements[i], completedObus);
        }

        if (y && packetElements.Count > startIndex)
        {
            byte[] lastElement = packetElements[packetElements.Count - 1];
            _fragmentedObu.Write(lastElement, 0, lastElement.Length);
        }
    }

    private static void AddCompletedObu(byte[] obu, List<byte[]> completedObus)
    {
        if (obu == null || obu.Length == 0)
        {
            return;
        }

        var obuType = AV1Packetiser.GetObuType(obu);
        if (obuType != AV1Packetiser.AV1ObuType.TemporalDelimiter &&
            obuType != AV1Packetiser.AV1ObuType.TileList)
        {
            completedObus.Add(obu);
        }
    }
}
