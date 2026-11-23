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
        private readonly Dictionary<long, RTPPacket> _receivedMedia = new Dictionary<long, RTPPacket>();
        private readonly List<PendingFec> _pendingFec = new List<PendingFec>();
        private readonly List<RepairIdentity> _seenRepairs = new List<RepairIdentity>();
        private long? _highestSequenceNumber;
        private uint? _syncSource;

        private enum RecoveryResult
        {
            Invalid,
            Complete,
            Pending,
            Recovered,
            Expired
        }

        private sealed class RepairIdentity
        {
            public long SequenceNumber;
            public byte[] Payload;
        }

        private sealed class PendingFec
        {
            public long SequenceNumber;
            public long[] ProtectedSequenceNumbers;
            public FecHeaderInfo Header;
            public byte[] Payload;
        }

        internal int MediaHistoryCount
        {
            get { lock (_lock) { return _receivedMedia.Count; } }
        }

        internal int PendingFecCount
        {
            get { lock (_lock) { return _pendingFec.Count; } }
        }

        public int RedPayloadType { get; set; }
        public int UlpfecPayloadType { get; set; }

        public RtpUlpfecDecoder(int redPayloadType, int ulpfecPayloadType)
        {
            RedPayloadType = redPayloadType;
            UlpfecPayloadType = ulpfecPayloadType;
        }

        /// <summary>
        /// Registers received media and checks pending repairs. Duplicate and expired media
        /// do not change decoder history or produce a second recovery.
        /// </summary>
        public void AddReceivedMedia(RTPPacket mediaPacket, out List<RTPPacket> recoveredPackets)
        {
            TryAddReceivedMedia(mediaPacket, out recoveredPackets);
        }

        /// <summary>
        /// Returns true only when this original media packet is new and can be delivered.
        /// A late original of a recovered packet must not be delivered a second time.
        /// </summary>
        internal bool TryAddReceivedMedia(RTPPacket mediaPacket, out List<RTPPacket> recoveredPackets)
        {
            recoveredPackets = new List<RTPPacket>();
            if (mediaPacket?.Header == null)
            {
                return false;
            }

            lock (_lock)
            {
                if (!AcceptSource(mediaPacket.Header.SyncSource))
                {
                    return false;
                }

                long sequence = ExtendSequence(mediaPacket.Header.SequenceNumber,
                    _highestSequenceNumber ?? mediaPacket.Header.SequenceNumber);
                if (_highestSequenceNumber.HasValue && sequence < HistoryFloor)
                {
                    return false;
                }

                AdvanceWindow(sequence);
                if (_receivedMedia.ContainsKey(sequence))
                {
                    return false;
                }

                _receivedMedia.Add(sequence, mediaPacket);
                CheckPendingFecRecovery(recoveredPackets);
                return true;
            }
        }

        /// <summary>
        /// Registers a valid repair. Only incomplete groups are retained; completed,
        /// malformed, expired and duplicate repairs are discarded.
        /// </summary>
        public void AddReceivedFec(RTPPacket fecPacket, out List<RTPPacket> recoveredPackets)
        {
            TryAddReceivedFec(fecPacket, out recoveredPackets);
        }

        /// <summary>
        /// Returns true only for a newly admitted valid repair, allowing the stream's
        /// ordering buffer to ignore invalid, expired and repeated repair positions.
        /// </summary>
        internal bool TryAddReceivedFec(RTPPacket fecPacket, out List<RTPPacket> recoveredPackets)
        {
            recoveredPackets = new List<RTPPacket>();
            if (!TryReadFecPacket(fecPacket, out var payload, out var header))
            {
                return false;
            }

            lock (_lock)
            {
                if (_syncSource.HasValue && _syncSource.Value != fecPacket.Header.SyncSource)
                {
                    return false;
                }

                long sequence = ExtendSequence(fecPacket.Header.SequenceNumber,
                    _highestSequenceNumber ?? fecPacket.Header.SequenceNumber);
                if (_highestSequenceNumber.HasValue && sequence < HistoryFloor)
                {
                    return false;
                }

                long sequenceBase = ExtendSequence(header.SequenceNumberBase, sequence);
                var protectedSequences = new long[header.ProtectedSequenceNumbers.Count];
                for (int i = 0; i < protectedSequences.Length; i++)
                {
                    protectedSequences[i] = sequenceBase + (ushort)(header.ProtectedSequenceNumbers[i] - header.SequenceNumberBase);
                    // A RED redundant repair can include the packet's already registered
                    // primary media. A repair-only packet cannot protect its own sequence.
                    if (protectedSequences[i] > sequence ||
                        (protectedSequences[i] == sequence && !_receivedMedia.ContainsKey(sequence)))
                    {
                        return false;
                    }
                }

                foreach (var seen in _seenRepairs)
                {
                    if (seen.SequenceNumber == sequence && PayloadEquals(seen.Payload, payload))
                    {
                        return false;
                    }
                }
                foreach (var pending in _pendingFec)
                {
                    if (pending.SequenceNumber == sequence && PayloadEquals(pending.Payload, payload))
                    {
                        return false;
                    }
                }

                var repair = new PendingFec
                {
                    SequenceNumber = sequence,
                    ProtectedSequenceNumbers = protectedSequences,
                    Header = header,
                    Payload = (byte[])payload.Clone()
                };
                long nextHighestSequence = Math.Max(_highestSequenceNumber ?? sequence, sequence);
                var result = EvaluateRepair(repair, nextHighestSequence - MAX_MEDIA_HISTORY + 1,
                    fecPacket.Header.SyncSource, out var recovered, out long recoveredSequence);
                if (result == RecoveryResult.Invalid || result == RecoveryResult.Expired)
                {
                    return false;
                }

                AcceptSource(fecPacket.Header.SyncSource);
                AdvanceWindow(sequence);
                if (_seenRepairs.Count == MAX_MEDIA_HISTORY)
                {
                    _seenRepairs.RemoveAt(0);
                }
                _seenRepairs.Add(new RepairIdentity { SequenceNumber = sequence, Payload = repair.Payload });
                if (result == RecoveryResult.Pending)
                {
                    if (_pendingFec.Count == MAX_PENDING_FEC)
                    {
                        _pendingFec.RemoveAt(0);
                    }
                    _pendingFec.Add(repair);
                }
                else if (result == RecoveryResult.Recovered)
                {
                    _receivedMedia.Add(recoveredSequence, recovered);
                    recoveredPackets.Add(recovered);
                    CheckPendingFecRecovery(recoveredPackets);
                }
                return true;
            }
        }

        private long HistoryFloor => _highestSequenceNumber.Value - MAX_MEDIA_HISTORY + 1;

        private bool AcceptSource(uint syncSource)
        {
            if (_syncSource.HasValue)
            {
                return _syncSource.Value == syncSource;
            }
            _syncSource = syncSource;
            return true;
        }

        private static long ExtendSequence(ushort sequence, long reference)
        {
            return reference + unchecked((short)(sequence - (ushort)reference));
        }

        private void AdvanceWindow(long sequence)
        {
            if (_highestSequenceNumber.HasValue && sequence <= _highestSequenceNumber.Value)
            {
                return;
            }
            _highestSequenceNumber = sequence;
            List<long> expiredMedia = null;
            foreach (long key in _receivedMedia.Keys)
            {
                if (key < HistoryFloor)
                {
                    (expiredMedia ??= new List<long>()).Add(key);
                }
            }
            if (expiredMedia != null)
            {
                foreach (long key in expiredMedia)
                {
                    _receivedMedia.Remove(key);
                }
            }
            // Retire a group before history eviction can make a received packet look lost.
            _pendingFec.RemoveAll(IsExpired);
            _seenRepairs.RemoveAll(repair => repair.SequenceNumber < HistoryFloor);
        }

        private bool IsExpired(PendingFec repair)
        {
            return IsExpired(repair, HistoryFloor);
        }

        private static bool IsExpired(PendingFec repair, long historyFloor)
        {
            foreach (long sequence in repair.ProtectedSequenceNumbers)
            {
                if (sequence < historyFloor)
                {
                    return true;
                }
            }
            return false;
        }

        private RecoveryResult EvaluateRepair(PendingFec repair, long historyFloor, uint syncSource,
            out RTPPacket recovered, out long missingSequence)
        {
            recovered = null;
            missingSequence = 0;
            if (IsExpired(repair, historyFloor))
            {
                return RecoveryResult.Expired;
            }

            int missingCount = 0;
            var presentPackets = new List<RTPPacket>();
            foreach (long sequence in repair.ProtectedSequenceNumbers)
            {
                if (_receivedMedia.TryGetValue(sequence, out var packet))
                {
                    presentPackets.Add(packet);
                }
                else
                {
                    missingSequence = sequence;
                    missingCount++;
                }
            }
            return RecoverPacket(repair.Header, syncSource, presentPackets,
                missingCount, (ushort)missingSequence, out recovered);
        }

        private void CheckPendingFecRecovery(List<RTPPacket> recoveredPackets)
        {
            bool recoveredAny;
            do
            {
                recoveredAny = false;
                for (int i = _pendingFec.Count - 1; i >= 0; i--)
                {
                    var result = EvaluateRepair(_pendingFec[i], HistoryFloor, _syncSource.Value,
                        out var recovered, out long sequence);
                    if (result == RecoveryResult.Pending)
                    {
                        continue;
                    }
                    _pendingFec.RemoveAt(i);
                    if (result == RecoveryResult.Recovered)
                    {
                        _receivedMedia.Add(sequence, recovered);
                        recoveredPackets.Add(recovered);
                        recoveredAny = true;
                    }
                }
            } while (recoveredAny);
        }

        private static bool PayloadEquals(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Resets media, pending repairs, source identity and extended sequence state.
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _receivedMedia.Clear();
                _pendingFec.Clear();
                _seenRepairs.Clear();
                _highestSequenceNumber = null;
                _syncSource = null;
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
                if ((b0 & 0x80) != 0)
                {
                    return false;
                }
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

                if (protectedSeqNums.Count == 0)
                {
                    return false;
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
            if (receivedMediaPackets == null || !TryReadFecPacket(fecPacket, out _, out var header))
            {
                return false;
            }

            ushort missingSequence = 0;
            int missingCount = 0;
            var presentPackets = new List<RTPPacket>();
            foreach (ushort sequence in header.ProtectedSequenceNumbers)
            {
                if (receivedMediaPackets.TryGetValue(sequence, out var packet))
                {
                    presentPackets.Add(packet);
                }
                else
                {
                    missingSequence = sequence;
                    missingCount++;
                }
            }
            return RecoverPacket(header, fecPacket.Header.SyncSource, presentPackets,
                missingCount, missingSequence, out recoveredPacket) == RecoveryResult.Recovered;
        }

        private bool TryReadFecPacket(RTPPacket packet, out byte[] payload, out FecHeaderInfo header)
        {
            payload = null;
            header = null;
            if (packet?.Header == null)
            {
                return false;
            }
            payload = packet.GetPayloadBytes();
            if (packet.Header.PayloadType == RedPayloadType)
            {
                if (!RtpUlpfecEncoder.TryUnwrapRed(payload, out byte innerPayloadType, out payload) ||
                    innerPayloadType != UlpfecPayloadType)
                {
                    return false;
                }
            }
            else if (packet.Header.PayloadType != UlpfecPayloadType)
            {
                return false;
            }
            return TryParseFecPayload(payload, out header) && header.ProtectedSequenceNumbers.Count > 0;
        }

        private static RecoveryResult RecoverPacket(FecHeaderInfo headerInfo, uint syncSource,
            List<RTPPacket> presentPackets, int missingCount, ushort missingSeqNum, out RTPPacket recoveredPacket)
        {
            recoveredPacket = null;
            if (missingCount == 0)
            {
                return RecoveryResult.Complete;
            }
            if (missingCount > 1)
            {
                return RecoveryResult.Pending;
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
                if (present?.Header == null || present.Header.SyncSource != syncSource)
                {
                    return RecoveryResult.Invalid;
                }
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
                return RecoveryResult.Invalid;
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
            BinaryPrimitives.WriteUInt32BigEndian(fullPacket.AsSpan(8), syncSource);

            if (recoveredPost12Length > 0)
            {
                Buffer.BlockCopy(recoveredPayloadParity, 0, fullPacket, RTPHeader.MIN_HEADER_LEN, recoveredPost12Length);
            }

            int headerLength = RTPHeader.MIN_HEADER_LEN + ccRec * 4;
            if (headerLength > fullPacket.Length)
            {
                return RecoveryResult.Invalid;
            }
            if (xRec != 0)
            {
                if (headerLength + 4 > fullPacket.Length)
                {
                    return RecoveryResult.Invalid;
                }
                headerLength += 4 + BinaryPrimitives.ReadUInt16BigEndian(fullPacket.AsSpan(headerLength + 2)) * 4;
                if (headerLength > fullPacket.Length)
                {
                    return RecoveryResult.Invalid;
                }
            }
            if (pRec != 0 && (headerLength == fullPacket.Length || fullPacket[fullPacket.Length - 1] == 0 ||
                fullPacket[fullPacket.Length - 1] > fullPacket.Length - headerLength))
            {
                return RecoveryResult.Invalid;
            }

            recoveredPacket = new RTPPacket(fullPacket);
            return RecoveryResult.Recovered;
        }
    }
}
