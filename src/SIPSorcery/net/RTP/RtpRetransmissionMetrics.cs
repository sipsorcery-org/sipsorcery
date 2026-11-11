//-----------------------------------------------------------------------------
// Filename: RtpRetransmissionMetrics.cs
//
// Description: Counters for generic NACKs (RFC 4585) received for a stream and
// the packets sent again because of them.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading;

namespace SIPSorcery.Net;

/// <summary>
/// Counters for the generic NACKs (RFC 4585) received for a stream and the packets sent again because of them.
/// </summary>
public class RtpRetransmissionMetrics
{
    private long _nacksReceived;
    private long _retransmissionsRequested;
    private long _packetsRetransmitted;
    private long _wireBytesRetransmitted;
    private long _packetsNotKept;
    private long _retransmissionsSuppressed;
    private long _retransmissionsOverBudget;

    /// <summary>
    /// Generic NACK messages received for the stream.
    /// </summary>
    public long NacksReceived => Interlocked.Read(ref _nacksReceived);

    /// <summary>
    /// Sequence numbers those NACKs reported lost, counting each time one was reported.
    /// </summary>
    public long RetransmissionsRequested => Interlocked.Read(ref _retransmissionsRequested);

    /// <summary>
    /// Packets sent again.
    /// </summary>
    public long PacketsRetransmitted => Interlocked.Read(ref _packetsRetransmitted);

    /// <summary>
    /// Bytes sent again, as on the wire.
    /// </summary>
    public long WireBytesRetransmitted => Interlocked.Read(ref _wireBytesRetransmitted);

    /// <summary>
    /// Requested packets that could not be sent again because they were no longer kept, or never were.
    /// </summary>
    public long PacketsNotKept => Interlocked.Read(ref _packetsNotKept);

    /// <summary>
    /// Requests ignored because the packet had been sent again less than a round trip ago.
    /// </summary>
    public long RetransmissionsSuppressed => Interlocked.Read(ref _retransmissionsSuppressed);

    /// <summary>
    /// Requests refused because sending the packet again would have taken the bytes resent past the bytes sent.
    /// </summary>
    public long RetransmissionsOverBudget => Interlocked.Read(ref _retransmissionsOverBudget);

    internal void RecordNack(int retransmissionsRequested)
    {
        Interlocked.Increment(ref _nacksReceived);
        Interlocked.Add(ref _retransmissionsRequested, retransmissionsRequested);
    }

    internal void RecordRetransmission(int packetLength)
    {
        Interlocked.Increment(ref _packetsRetransmitted);
        Interlocked.Add(ref _wireBytesRetransmitted, packetLength);
    }

    internal void RecordNotKept() => Interlocked.Increment(ref _packetsNotKept);

    internal void RecordSuppressed() => Interlocked.Increment(ref _retransmissionsSuppressed);

    internal void RecordOverBudget() => Interlocked.Increment(ref _retransmissionsOverBudget);

    public override string ToString() =>
        $"Nacks: {NacksReceived}, Requested: {RetransmissionsRequested}, Retransmitted: {PacketsRetransmitted}, " +
        $"RetransmittedWireBytes: {WireBytesRetransmitted}, NotKept: {PacketsNotKept}, Suppressed: {RetransmissionsSuppressed}, " +
        $"OverBudget: {RetransmissionsOverBudget}";
}
