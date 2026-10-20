//-----------------------------------------------------------------------------
// Filename: RtpUlpfecDecoder.cs
//
// Description: RFC 5109 (ULPFEC) and RFC 2198 (RED) forward error correction
// decoder and packet recovery oracle for video RTP streams.
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Decodes RFC 5109 ULPFEC packets (wrapped in RFC 2198 RED) and recovers lost
    /// media RTP packets using XOR parity.
    /// </summary>
    public class RtpUlpfecDecoder
    {
        private static readonly ILogger logger = LogFactory.CreateLogger<RtpUlpfecDecoder>();

        public const int MAX_MEDIA_HISTORY = 128;
        public const int MAX_PENDING_FEC = 32;

        private readonly object _lock = new object();
        private readonly Dictionary<ushort, RTPPacket> _receivedMedia = new Dictionary<ushort, RTPPacket>();
        private readonly Queue<ushort> _receivedMediaOrder = new Queue<ushort>();
        private readonly List<RTPPacket> _pendingFec = new List<RTPPacket>();

        public int RedPayloadType { get; set; }
        public int UlpfecPayloadType { get; set; }

        public RtpUlpfecDecoder(int redPayloadType = 122, int ulpfecPayloadType = 127)
        {
            RedPayloadType = redPayloadType;
            UlpfecPayloadType = ulpfecPayloadType;
        }

        /// <summary>
        /// Registers a received media packet with the sliding window and checks if any pending
        /// FEC packets can now recover missing packets.
        /// </summary>
        public void AddReceivedMedia(RTPPacket mediaPacket, out List<RTPPacket> recoveredPackets)
        {
            recoveredPackets = new List<RTPPacket>();
            if (mediaPacket == null)
            {
                return;
            }

            lock (_lock)
            {
                ushort seq = mediaPacket.Header.SequenceNumber;
                if (!_receivedMedia.ContainsKey(seq))
                {
                    _receivedMedia[seq] = mediaPacket;
                    _receivedMediaOrder.Enqueue(seq);

                    while (_receivedMediaOrder.Count > MAX_MEDIA_HISTORY)
                    {
                        ushort oldest = _receivedMediaOrder.Dequeue();
                        _receivedMedia.Remove(oldest);
                    }
                }

                // Check pending FEC packets to see if any can now recover a missing media packet
                CheckPendingFecRecovery(recoveredPackets);
            }
        }

        /// <summary>
        /// Registers a received FEC repair packet and immediately attempts recovery of any
        /// missing media packets. If recovery is not yet possible, the FEC packet is retained
        /// in the pending FEC buffer.
        /// </summary>
        public void AddReceivedFec(RTPPacket fecPacket, out List<RTPPacket> recoveredPackets)
        {
            recoveredPackets = new List<RTPPacket>();
            if (fecPacket == null)
            {
                return;
            }

            lock (_lock)
            {
                if (TryRecoverPacket(fecPacket, _receivedMedia, out var recovered))
                {
                    recoveredPackets.Add(recovered);
                    ushort recSeq = recovered.Header.SequenceNumber;
                    if (!_receivedMedia.ContainsKey(recSeq))
                    {
                        _receivedMedia[recSeq] = recovered;
                        _receivedMediaOrder.Enqueue(recSeq);
                    }

                    // Iterative recovery for any other pending FEC packets
                    CheckPendingFecRecovery(recoveredPackets);
                }
                else
                {
                    _pendingFec.Add(fecPacket);
                    while (_pendingFec.Count > MAX_PENDING_FEC)
                    {
                        _pendingFec.RemoveAt(0);
                    }
                }
            }
        }

        private void CheckPendingFecRecovery(List<RTPPacket> recoveredPackets)
        {
            bool recoveredAny;
            do
            {
                recoveredAny = false;
                for (int i = _pendingFec.Count - 1; i >= 0; i--)
                {
                    var fec = _pendingFec[i];
                    if (TryRecoverPacket(fec, _receivedMedia, out var recovered))
                    {
                        _pendingFec.RemoveAt(i);
                        recoveredPackets.Add(recovered);
                        ushort recSeq = recovered.Header.SequenceNumber;
                        if (!_receivedMedia.ContainsKey(recSeq))
                        {
                            _receivedMedia[recSeq] = recovered;
                            _receivedMediaOrder.Enqueue(recSeq);
                        }
                        recoveredAny = true;
                        break; // Loop again with updated media history
                    }
                }
            } while (recoveredAny);
        }

        /// <summary>
        /// Resets the sliding-window media and pending FEC buffers.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _receivedMedia.Clear();
                _receivedMediaOrder.Clear();
                _pendingFec.Clear();
            }
        }

        /// <summary>
        /// Information parsed from an RFC 5109 FEC packet header.
        /// </summary>
        public class FecHeaderInfo
        {
            public bool LongMask { get; set; }
            public byte PaddingRecovery { get; set; }
            public byte ExtensionRecovery { get; set; }
            public byte CsrcCountRecovery { get; set; }
            public byte MarkerRecovery { get; set; }
            public byte PayloadTypeRecovery { get; set; }
            public ushort SequenceNumberBase { get; set; }
            public uint TimestampRecovery { get; set; }
            public ushort LengthRecovery { get; set; }
            public ushort ProtectionLength { get; set; }
            public byte[] Mask { get; set; }
            public List<ushort> ProtectedSequenceNumbers { get; set; } = new List<ushort>();
            public byte[] ParityPayload { get; set; }
        }

        /// <summary>
        /// Parses an RFC 5109 FEC payload into structured header information.
        /// </summary>
        public static bool TryParseFecPayload(byte[] fecPayload, out FecHeaderInfo headerInfo)
        {
            headerInfo = null;
            if (fecPayload == null || fecPayload.Length < RtpUlpfecEncoder.RFC5109_FEC_HEADER_SIZE + 4)
            {
                return false;
            }

            try
            {
                byte b0 = fecPayload[0];
                bool longMask = (b0 & 0x40) != 0;
                byte pRec = (byte)((b0 >> 5) & 0x01);
                byte xRec = (byte)((b0 >> 4) & 0x01);
                byte ccRec = (byte)(b0 & 0x0F);

                byte b1 = fecPayload[1];
                byte mRec = (byte)((b1 >> 7) & 0x01);
                byte ptRec = (byte)(b1 & 0x7F);

                ushort snBase = BinaryPrimitives.ReadUInt16BigEndian(fecPayload.AsSpan(2));
                uint tsRec = BinaryPrimitives.ReadUInt32BigEndian(fecPayload.AsSpan(4));
                ushort lenRec = BinaryPrimitives.ReadUInt16BigEndian(fecPayload.AsSpan(8));

                int maskSize = longMask ? RtpUlpfecEncoder.RFC5109_LONG_MASK_SIZE : RtpUlpfecEncoder.RFC5109_SHORT_MASK_SIZE;
                int minExpectedLen = RtpUlpfecEncoder.RFC5109_FEC_HEADER_SIZE + 2 + maskSize;
                if (fecPayload.Length < minExpectedLen)
                {
                    return false;
                }

                ushort protLength = BinaryPrimitives.ReadUInt16BigEndian(fecPayload.AsSpan(10));
                byte[] mask = new byte[maskSize];
                Buffer.BlockCopy(fecPayload, 12, mask, 0, maskSize);

                if (fecPayload.Length < minExpectedLen + protLength)
                {
                    return false;
                }

                byte[] parity = new byte[protLength];
                Buffer.BlockCopy(fecPayload, minExpectedLen, parity, 0, protLength);

                var protectedSeqNums = new List<ushort>();
                for (int i = 0; i < maskSize * 8; i++)
                {
                    int byteIdx = i / 8;
                    int bitIdx = 7 - (i % 8);
                    if ((mask[byteIdx] & (1 << bitIdx)) != 0)
                    {
                        protectedSeqNums.Add((ushort)(snBase + i));
                    }
                }

                headerInfo = new FecHeaderInfo
                {
                    LongMask = longMask,
                    PaddingRecovery = pRec,
                    ExtensionRecovery = xRec,
                    CsrcCountRecovery = ccRec,
                    MarkerRecovery = mRec,
                    PayloadTypeRecovery = ptRec,
                    SequenceNumberBase = snBase,
                    TimestampRecovery = tsRec,
                    LengthRecovery = lenRec,
                    ProtectionLength = protLength,
                    Mask = mask,
                    ProtectedSequenceNumbers = protectedSeqNums,
                    ParityPayload = parity
                };

                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse RFC 5109 FEC payload.");
                return false;
            }
        }

        /// <summary>
        /// Attempts to recover a single missing media packet from a received RED/ULPFEC repair packet
        /// and the available received media packets.
        /// </summary>
        /// <param name="fecPacket">The received FEC packet (either RED-encapsulated or raw RFC 5109).</param>
        /// <param name="receivedMediaPackets">Dictionary of available received media packets keyed by sequence number.</param>
        /// <param name="recoveredPacket">The recovered RTP media packet, if recovery succeeded.</param>
        /// <returns>True if a missing packet was recovered; false if no recovery was possible or needed.</returns>
        public bool TryRecoverPacket(RTPPacket fecPacket, IReadOnlyDictionary<ushort, RTPPacket> receivedMediaPackets, out RTPPacket recoveredPacket)
        {
            recoveredPacket = null;
            if (fecPacket == null || receivedMediaPackets == null)
            {
                return false;
            }

            byte[] fecPayload = fecPacket.GetPayloadBytes() ?? fecPacket.Payload;

            // Check if wrapped in RED
            if (fecPacket.Header.PayloadType == RedPayloadType)
            {
                if (!RtpUlpfecEncoder.TryUnwrapRed(fecPayload, out byte innerPt, out byte[] innerPayload))
                {
                    return false;
                }
                if (innerPt != UlpfecPayloadType)
                {
                    return false; // Not ULPFEC
                }
                fecPayload = innerPayload;
            }

            if (!TryParseFecPayload(fecPayload, out var headerInfo))
            {
                return false;
            }

            // Identify missing packets among those covered by the mask
            ushort missingSeqNum = 0;
            int missingCount = 0;
            var presentPackets = new List<RTPPacket>();

            foreach (var seq in headerInfo.ProtectedSequenceNumbers)
            {
                if (receivedMediaPackets.TryGetValue(seq, out var pkt))
                {
                    presentPackets.Add(pkt);
                }
                else
                {
                    missingSeqNum = seq;
                    missingCount++;
                }
            }

            if (missingCount == 0)
            {
                // No packet missing in this protection group
                return false;
            }

            if (missingCount > 1)
            {
                // Too many lost packets for single XOR parity recovery
                logger.LogDebug("Cannot recover: {Count} packets missing in ULPFEC group starting at {BaseSeq}.", missingCount, headerInfo.SequenceNumberBase);
                return false;
            }

            // Exactly ONE packet is missing! We can recover it!
            byte pRec = headerInfo.PaddingRecovery;
            byte xRec = headerInfo.ExtensionRecovery;
            byte ccRec = headerInfo.CsrcCountRecovery;
            byte mRec = headerInfo.MarkerRecovery;
            byte ptRec = headerInfo.PayloadTypeRecovery;
            uint tsRec = headerInfo.TimestampRecovery;
            ushort lenRec = headerInfo.LengthRecovery;

            byte[] recoveredPayloadParity = (byte[])headerInfo.ParityPayload.Clone();

            foreach (var present in presentPackets)
            {
                pRec ^= (byte)present.Header.PaddingFlag;
                xRec ^= (byte)present.Header.HeaderExtensionFlag;
                ccRec ^= (byte)present.Header.CSRCCount;
                mRec ^= (byte)present.Header.MarkerBit;
                ptRec ^= (byte)(present.Header.PayloadType & 0x7F);
                tsRec ^= present.Header.Timestamp;

                byte[] presProtected = RtpUlpfecEncoder.GetProtectedBytes(present);
                lenRec ^= (ushort)presProtected.Length;

                for (int i = 0; i < presProtected.Length && i < recoveredPayloadParity.Length; i++)
                {
                    recoveredPayloadParity[i] ^= presProtected[i];
                }
            }

            int recoveredPost12Length = lenRec;
            if (recoveredPost12Length < 0 || recoveredPost12Length > recoveredPayloadParity.Length)
            {
                logger.LogWarning("Invalid recovered post-12 payload length {Length} (exceeds parity length {ParityLength}).", recoveredPost12Length, recoveredPayloadParity.Length);
                return false;
            }

            // Reconstruct full RFC 3550 RTP packet (12-byte fixed header + recovered post-12 bytes per RFC 5109 §9)
            byte[] fullPacket = new byte[RTPHeader.MIN_HEADER_LEN + recoveredPost12Length];
            ushort firstWord = (ushort)((2 << 14) |
                                       ((pRec & 0x01) << 13) |
                                       ((xRec & 0x01) << 12) |
                                       ((ccRec & 0x0F) << 8) |
                                       ((mRec & 0x01) << 7) |
                                       (ptRec & 0x7F));
            BinaryPrimitives.WriteUInt16BigEndian(fullPacket.AsSpan(0), firstWord);
            BinaryPrimitives.WriteUInt16BigEndian(fullPacket.AsSpan(2), missingSeqNum);
            BinaryPrimitives.WriteUInt32BigEndian(fullPacket.AsSpan(4), tsRec);
            BinaryPrimitives.WriteUInt32BigEndian(fullPacket.AsSpan(8), fecPacket.Header.SyncSource);

            if (recoveredPost12Length > 0)
            {
                Buffer.BlockCopy(recoveredPayloadParity, 0, fullPacket, RTPHeader.MIN_HEADER_LEN, recoveredPost12Length);
            }

            recoveredPacket = new RTPPacket(fullPacket);
            return true;
        }
    }
}
