//-----------------------------------------------------------------------------
// Filename: RtpPacketResendBuffer.cs
//
// Description: The RTP packets a stream sent recently, kept to be sent again
// when a generic NACK (RFC 4585) reports them lost.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net;

/// <summary>
/// The RTP packets a stream sent recently, exactly as they went on the wire (SRTP protected on a secure
/// session), so the ones a generic NACK (RFC 4585) reports lost can be sent again unchanged.
/// </summary>
internal sealed class RtpPacketResendBuffer
{
    /// <summary>
    /// The most packets kept. Chrome's SRTP replay window is 1024 packets too, so it would refuse older ones.
    /// </summary>
    internal const int CAPACITY = 1024;

    /// <summary>
    /// Packets sent longer ago than this are not sent again.
    /// </summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(1);

    internal enum ClaimResult
    {
        Claimed,
        NotKept,
        RecentlyResent,
        OverBudget
    }

    private record struct Entry(byte[] Packet, ushort SequenceNumber, uint PayloadLength, TimeSpan SentAt, TimeSpan? ResentAt = null);

    private readonly Entry[] _entries = new Entry[CAPACITY];
    private int _keptBytes;

    /// <summary>
    /// The bytes that may still be sent again. Every packet sent adds its length, up to the bytes kept, so however
    /// often packets are asked for, resends add at most one burst of every packet kept and then the stream's own rate.
    /// </summary>
    private int _resendBudget;

    /// <param name="packet">The packet as sent. It must not change afterwards.</param>
    /// <param name="payloadLength">The RTP payload length, which sender reports count.</param>
    public void Add(ushort sequenceNumber, byte[] packet, uint payloadLength, TimeSpan sentAt)
    {
        lock (_entries)
        {
            ref var entry = ref _entries[sequenceNumber % CAPACITY];
            _keptBytes += packet.Length - (entry.Packet?.Length ?? 0);
            _resendBudget = Math.Min(_resendBudget + packet.Length, _keptBytes);
            entry = new(packet, sequenceNumber, payloadLength, sentAt);
        }
    }

    public void Clear()
    {
        lock (_entries)
        {
            Array.Clear(_entries, 0, CAPACITY);
            _keptBytes = 0;
            _resendBudget = 0;
        }
    }

    /// <summary>
    /// Gets a packet to send again and records it as resent now, unless it was already resent less than
    /// <paramref name="minResendInterval"/> ago, in which case that resend may still be on its way.
    /// </summary>
    public ClaimResult ClaimForResend(ushort sequenceNumber, TimeSpan now, TimeSpan minResendInterval, out byte[] packet, out uint payloadLength)
    {
        packet = null;
        payloadLength = 0;
        lock (_entries)
        {
            ref var entry = ref _entries[sequenceNumber % CAPACITY];
            if (entry.Packet == null || entry.SequenceNumber != sequenceNumber || now - entry.SentAt > MaxAge)
            {
                return ClaimResult.NotKept;
            }

            if (entry.ResentAt is { } resentAt && now - resentAt < minResendInterval)
            {
                return ClaimResult.RecentlyResent;
            }

            if (entry.Packet.Length > _resendBudget)
            {
                return ClaimResult.OverBudget;
            }

            _resendBudget -= entry.Packet.Length;
            entry.ResentAt = now;
            packet = entry.Packet;
            payloadLength = entry.PayloadLength;
            return ClaimResult.Claimed;
        }
    }
}
