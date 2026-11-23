//-----------------------------------------------------------------------------
// Filename: RtpUlpfecEncoder.cs
//
// Description: RFC 5109 (ULPFEC) and RFC 2198 (RED) forward error correction
// generator for video RTP streams.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Generates RFC 5109 ULPFEC repair packets wrapped in RFC 2198 RED headers
    /// to provide interoperable video forward error correction for WebRTC browsers.
    /// </summary>
    public class RtpUlpfecEncoder
    {
        private static readonly ILogger logger = LogFactory.CreateLogger<RtpUlpfecEncoder>();

        public const int RFC5109_FEC_HEADER_SIZE = 10;
        public const int RFC5109_SHORT_MASK_SIZE = 2; // 16 bits
        public const int RFC5109_LONG_MASK_SIZE = 6;  // 48 bits
        public const int MAX_MEDIA_PACKETS_SHORT_MASK = 16;
        public const int MAX_MEDIA_PACKETS_LONG_MASK = 48;

        private readonly object _lock = new object();
        private readonly List<RTPPacket> _currentGroup = new List<RTPPacket>();
        private long _groupStartTimestampTicks;
        private RtpUlpfecConfig _config;

        public int RedPayloadType { get; private set; }
        public int UlpfecPayloadType { get; private set; }
        public int MediaPayloadType { get; private set; }

        public bool IsActive { get; } = true;

        public RtpUlpfecConfig Config
        {
            get => _config;
            set => _config = value?.Clone() ?? new RtpUlpfecConfig();
        }

        public RtpUlpfecEncoder(int redPayloadType, int ulpfecPayloadType, int mediaPayloadType, RtpUlpfecConfig config = null)
        {
            RedPayloadType = redPayloadType;
            UlpfecPayloadType = ulpfecPayloadType;
            MediaPayloadType = mediaPayloadType;
            _config = config?.Clone() ?? new RtpUlpfecConfig();
            _groupStartTimestampTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Wraps an RTP payload with an RFC 2198 RED header (single-block format).
        /// </summary>
        public static byte[] WrapInRed(byte[] innerPayload, byte innerPayloadType)
        {
            return RtpRedPayload.EncodeSingleBlock(innerPayload, innerPayloadType);
        }

        /// <summary>
        /// Unwraps an RFC 2198 RED packet to extract the primary payload type and data.
        /// Supports both single-block and multi-block RED layouts.
        /// </summary>
        public static bool TryUnwrapRed(byte[] redPayload, out byte innerPayloadType, out byte[] innerPayload)
        {
            return RtpRedPayload.TryUnwrapPrimary(redPayload, out innerPayloadType, out innerPayload);
        }

        /// <summary>
        /// Extracts all bytes after the 12-byte fixed RTP header (CSRC list + Header Extensions + Payload + Padding)
        /// in accordance with RFC 5109 Section 7.
        /// </summary>
        public static byte[] GetProtectedBytes(RTPPacket packet)
        {
            if (packet == null)
            {
                return Array.Empty<byte>();
            }

            // Header.GetBytes() serializes the fixed 12 bytes + CSRC list + Header Extensions (if X == 1).
            byte[] headerBytes = packet.Header?.GetBytes() ?? Array.Empty<byte>();
            byte[] payloadBytes = packet.GetPayloadBytes() ?? packet.Payload ?? Array.Empty<byte>();

            int post12HeaderLen = Math.Max(0, headerBytes.Length - RTPHeader.MIN_HEADER_LEN);
            byte[] protectedBytes = new byte[post12HeaderLen + payloadBytes.Length];

            if (post12HeaderLen > 0)
            {
                Buffer.BlockCopy(headerBytes, RTPHeader.MIN_HEADER_LEN, protectedBytes, 0, post12HeaderLen);
            }

            if (payloadBytes.Length > 0)
            {
                Buffer.BlockCopy(payloadBytes, 0, protectedBytes, post12HeaderLen, payloadBytes.Length);
            }

            return protectedBytes;
        }

        /// <summary>
        /// Adds a finalized primary media packet to the protection window.
        /// If a protection group boundary, frame completion, or threshold is met,
        /// one or more RFC 5109 repair packets are generated and returned.
        /// Primary media forwarding is never delayed.
        /// </summary>
        /// <param name="mediaPacket">The canonical plaintext media RTP packet.</param>
        /// <param name="getNextSeqNum">Delegate to allocate monotonically increasing sequence numbers for repair packets.</param>
        /// <param name="generatedRepairPackets">List of generated repair packets (if any).</param>
        public void AddMediaPacket(RTPPacket mediaPacket, Func<ushort> getNextSeqNum, out List<RTPPacket> generatedRepairPackets)
        {
            generatedRepairPackets = null;

            if (mediaPacket == null || !IsActive || _config.TargetOverheadFraction <= 0.0f)
            {
                return;
            }

            lock (_lock)
            {
                if (_currentGroup.Count == 0)
                {
                    _groupStartTimestampTicks = Stopwatch.GetTimestamp();
                }

                // Check sequence distance from SN base
                if (_currentGroup.Count > 0)
                {
                    ushort baseSeq = _currentGroup[0].Header.SequenceNumber;
                    int distance = (ushort)(mediaPacket.Header.SequenceNumber - baseSeq);
                    int maxAllowedSpan = _config.AllowLongMask ? MAX_MEDIA_PACKETS_LONG_MASK : MAX_MEDIA_PACKETS_SHORT_MASK;

                    if (distance >= maxAllowedSpan || _currentGroup.Count >= _config.MaxGroupPackets)
                    {
                        // Current group reached maximum allowable span; generate repair for existing group first
                        var repair = CreateFecPacketInternal(_currentGroup, getNextSeqNum);
                        if (repair != null)
                        {
                            generatedRepairPackets = new List<RTPPacket> { repair };
                        }
                        _currentGroup.Clear();
                        _groupStartTimestampTicks = Stopwatch.GetTimestamp();
                    }
                }

                _currentGroup.Add(mediaPacket);

                // Check trigger conditions:
                // 1. Frame boundary (MarkerBit = 1)
                // 2. Target ratio reached
                // 3. Max group packets reached
                bool isFrameEnd = mediaPacket.Header.MarkerBit == 1;
                int triggerCount = Math.Max(_config.MinGroupPackets, (int)Math.Round(1.0f / _config.TargetOverheadFraction));

                if (isFrameEnd || _currentGroup.Count >= triggerCount || _currentGroup.Count >= _config.MaxGroupPackets)
                {
                    if (_currentGroup.Count >= _config.MinGroupPackets)
                    {
                        var repair = CreateFecPacketInternal(_currentGroup, getNextSeqNum);
                        if (repair != null)
                        {
                            if (generatedRepairPackets == null)
                            {
                                generatedRepairPackets = new List<RTPPacket> { repair };
                            }
                            else
                            {
                                generatedRepairPackets.Add(repair);
                            }
                        }
                    }
                    _currentGroup.Clear();
                    _groupStartTimestampTicks = Stopwatch.GetTimestamp();
                }
            }
        }

        /// <summary>
        /// Flushes any pending partial protection group if its age exceeds MaxRepairAge or if forced.
        /// </summary>
        public List<RTPPacket> Flush(Func<ushort> getNextSeqNum, bool force = false)
        {
            if (!IsActive)
            {
                return null;
            }

            lock (_lock)
            {
                if (_currentGroup.Count == 0)
                {
                    return null;
                }

                double elapsedMs = (Stopwatch.GetTimestamp() - _groupStartTimestampTicks) * 1000.0 / Stopwatch.Frequency;
                if (force || elapsedMs >= _config.MaxRepairAge.TotalMilliseconds)
                {
                    if (_currentGroup.Count >= _config.MinGroupPackets)
                    {
                        var repair = CreateFecPacketInternal(_currentGroup, getNextSeqNum);
                        _currentGroup.Clear();
                        _groupStartTimestampTicks = Stopwatch.GetTimestamp();
                        return repair != null ? new List<RTPPacket> { repair } : null;
                    }
                    else if (force)
                    {
                        _currentGroup.Clear();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Generates an RFC 5109 ULPFEC packet from the given list of protected media packets,
        /// encapsulated in an RFC 2198 RED RTP packet.
        /// </summary>
        public RTPPacket CreateFecPacket(IReadOnlyList<RTPPacket> mediaPackets, Func<ushort> getNextSeqNum)
        {
            lock (_lock)
            {
                return CreateFecPacketInternal(mediaPackets, getNextSeqNum);
            }
        }

        private RTPPacket CreateFecPacketInternal(IReadOnlyList<RTPPacket> mediaPackets, Func<ushort> getNextSeqNum)
        {
            if (mediaPackets == null || mediaPackets.Count == 0)
            {
                return null;
            }

            ushort snBase = mediaPackets[0].Header.SequenceNumber;
            ushort lastSeq = mediaPackets[mediaPackets.Count - 1].Header.SequenceNumber;
            int span = (ushort)(lastSeq - snBase);

            bool isLongMask = span >= MAX_MEDIA_PACKETS_SHORT_MASK;
            if (isLongMask && !_config.AllowLongMask)
            {
                logger.LogWarning("ULPFEC group span {Span} exceeds 16 packets and long masks are disabled.", span);
                return null;
            }
            if (span >= MAX_MEDIA_PACKETS_LONG_MASK)
            {
                logger.LogWarning("ULPFEC group span {Span} exceeds maximum 48 packets supported by RFC 5109.", span);
                return null;
            }

            int maskBytesCount = isLongMask ? RFC5109_LONG_MASK_SIZE : RFC5109_SHORT_MASK_SIZE;
            byte[] maskBytes = new byte[maskBytesCount];

            // 1. Calculate Protection Length (maximum post-12-byte length across protected media packets per RFC 5109 §7)
            int maxProtectedLength = 0;
            var protectedByteBuffers = new List<byte[]>(mediaPackets.Count);
            foreach (var pkt in mediaPackets)
            {
                byte[] protectedBytes = GetProtectedBytes(pkt);
                protectedByteBuffers.Add(protectedBytes);
                if (protectedBytes.Length > maxProtectedLength)
                {
                    maxProtectedLength = protectedBytes.Length;
                }
            }

            // 2. Compute XOR recovery values
            byte pRecovery = 0;
            byte xRecovery = 0;
            byte ccRecovery = 0;
            byte mRecovery = 0;
            byte ptRecovery = 0;
            uint tsRecovery = 0;
            ushort lengthRecovery = 0;

            byte[] fecPayloadParity = new byte[maxProtectedLength];

            for (int pIdx = 0; pIdx < mediaPackets.Count; pIdx++)
            {
                var pkt = mediaPackets[pIdx];
                byte[] protectedBytes = protectedByteBuffers[pIdx];

                ushort diff = (ushort)(pkt.Header.SequenceNumber - snBase);
                if (diff < maskBytesCount * 8)
                {
                    maskBytes[diff / 8] |= (byte)(1 << (7 - (diff % 8)));
                }

                pRecovery ^= (byte)pkt.Header.PaddingFlag;
                xRecovery ^= (byte)pkt.Header.HeaderExtensionFlag;
                ccRecovery ^= (byte)pkt.Header.CSRCCount;
                mRecovery ^= (byte)pkt.Header.MarkerBit;
                ptRecovery ^= (byte)(pkt.Header.PayloadType & 0x7F);
                tsRecovery ^= pkt.Header.Timestamp;

                lengthRecovery ^= (ushort)protectedBytes.Length;

                for (int i = 0; i < protectedBytes.Length; i++)
                {
                    fecPayloadParity[i] ^= protectedBytes[i];
                }
            }

            // 3. Assemble RFC 5109 Payload
            // FEC Header (10 bytes) + FEC Level 0 Header (2 bytes length + maskBytesCount) + Parity Payload
            int level0HeaderLength = 2 + maskBytesCount;
            int rfc5109Length = RFC5109_FEC_HEADER_SIZE + level0HeaderLength + maxProtectedLength;
            byte[] rfc5109Payload = new byte[rfc5109Length];

            // Byte 0: E (bit 7 = 0), L (bit 6 = 1 for long mask, 0 for short), P, X, CC
            rfc5109Payload[0] = (byte)(((isLongMask ? 1 : 0) << 6) |
                                      ((pRecovery & 0x01) << 5) |
                                      ((xRecovery & 0x01) << 4) |
                                      (ccRecovery & 0x0F));

            // Byte 1: M (bit 7), PT recovery (bits 6..0)
            rfc5109Payload[1] = (byte)(((mRecovery & 0x01) << 7) |
                                      (ptRecovery & 0x7F));

            // Bytes 2-3: SN base
            BinaryPrimitives.WriteUInt16BigEndian(rfc5109Payload.AsSpan(2), snBase);

            // Bytes 4-7: TS recovery
            BinaryPrimitives.WriteUInt32BigEndian(rfc5109Payload.AsSpan(4), tsRecovery);

            // Bytes 8-9: Length recovery
            BinaryPrimitives.WriteUInt16BigEndian(rfc5109Payload.AsSpan(8), lengthRecovery);

            // Level 0 Header:
            // Bytes 10-11: Protection Length
            BinaryPrimitives.WriteUInt16BigEndian(rfc5109Payload.AsSpan(10), (ushort)maxProtectedLength);

            // Bytes 12 to 12+maskBytesCount-1: Mask
            Buffer.BlockCopy(maskBytes, 0, rfc5109Payload, 12, maskBytesCount);

            // Bytes (12+maskBytesCount) to end: Parity Payload
            Buffer.BlockCopy(fecPayloadParity, 0, rfc5109Payload, 12 + maskBytesCount, maxProtectedLength);

            // 4. Wrap RFC 5109 payload into RFC 2198 RED format
            byte[] redPayload = WrapInRed(rfc5109Payload, (byte)UlpfecPayloadType);

            // 5. Construct outgoing repair RTPPacket
            var lastMedia = mediaPackets[mediaPackets.Count - 1];
            ushort fecSeqNum = getNextSeqNum != null ? getNextSeqNum() : (ushort)(lastSeq + 1);

            RTPPacket fecRtpPacket = new RTPPacket(new ArraySegment<byte>(redPayload), 0);
            fecRtpPacket.Header.SyncSource = lastMedia.Header.SyncSource;
            fecRtpPacket.Header.SequenceNumber = fecSeqNum;
            fecRtpPacket.Header.Timestamp = lastMedia.Header.Timestamp;
            fecRtpPacket.Header.MarkerBit = 0;
            fecRtpPacket.Header.PayloadType = RedPayloadType;
            fecRtpPacket.Payload = redPayload;

            return fecRtpPacket;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _currentGroup.Clear();
                _groupStartTimestampTicks = Stopwatch.GetTimestamp();
            }
        }
    }
}
