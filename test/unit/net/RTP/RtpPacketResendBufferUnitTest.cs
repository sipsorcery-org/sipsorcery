//-----------------------------------------------------------------------------
// Filename: RtpPacketResendBufferUnitTest.cs
//
// Description: Unit tests for the buffer of sent RTP packets kept to be sent
// again when a generic NACK (RFC 4585) reports them lost.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class RtpPacketResendBufferUnitTest
{
    private static readonly TimeSpan Start = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinResendInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    [Fact]
    public void ReturnsTheSamePacketUntilItExpires()
    {
        var buffer = new RtpPacketResendBuffer();
        byte[] packet = [1, 2, 3, 4, 5];
        buffer.Add(7, packet, 3, Start);

        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(7, Start + RtpPacketResendBuffer.MaxAge, MinResendInterval, out var found, out var payloadLength));
        Assert.Same(packet, found);
        Assert.Equal(3u, payloadLength);

        var expired = new RtpPacketResendBuffer();
        expired.Add(7, packet, 3, Start);
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.NotKept, expired.ClaimForResend(7, Start + RtpPacketResendBuffer.MaxAge + Tick, MinResendInterval, out _, out _));
    }

    [Fact]
    public void PacketsNeverSentOrOverwrittenAreNotKept()
    {
        var buffer = new RtpPacketResendBuffer();
        buffer.Add(5, new byte[] { 5 }, 1, Start);
        buffer.Add(5 + RtpPacketResendBuffer.CAPACITY, new byte[] { 6 }, 1, Start);

        Assert.Equal(RtpPacketResendBuffer.ClaimResult.NotKept, buffer.ClaimForResend(4, Start, MinResendInterval, out _, out _));
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.NotKept, buffer.ClaimForResend(5, Start, MinResendInterval, out _, out _));
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(5 + RtpPacketResendBuffer.CAPACITY, Start, MinResendInterval, out var packet, out _));
        Assert.Equal(6, packet[0]);
    }

    [Fact]
    public void SequenceNumbersWrap()
    {
        var buffer = new RtpPacketResendBuffer();
        buffer.Add(ushort.MaxValue, new byte[] { 1 }, 1, Start);
        buffer.Add(0, new byte[] { 2 }, 1, Start);

        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(ushort.MaxValue, Start, MinResendInterval, out var last, out _));
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(0, Start, MinResendInterval, out var first, out _));
        Assert.Equal(1, last[0]);
        Assert.Equal(2, first[0]);
    }

    [Fact]
    public void RequestsWhileAResendMayBeInFlightAreSuppressed()
    {
        var buffer = new RtpPacketResendBuffer();
        buffer.Add(9, new byte[] { 9 }, 1, Start);

        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(9, Start, MinResendInterval, out _, out _));
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.RecentlyResent, buffer.ClaimForResend(9, Start + MinResendInterval - Tick, MinResendInterval, out var suppressed, out _));
        Assert.Null(suppressed);
        buffer.Add(10, new byte[] { 10 }, 1, Start);
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(9, Start + MinResendInterval, MinResendInterval, out _, out _));
    }

    /// <summary>
    /// However often packets are asked for, a burst of resends is at most every packet kept, and after it the
    /// bytes resent cannot exceed the bytes sent.
    /// </summary>
    [Fact]
    public void ResendsAreLimitedToTheBytesKeptAndThenToTheBytesSent()
    {
        const int PACKET_LENGTH = 10;
        var buffer = new RtpPacketResendBuffer();
        for (int i = 0; i < 2 * RtpPacketResendBuffer.CAPACITY; i++)
        {
            buffer.Add((ushort)i, new byte[PACKET_LENGTH], 1, Start);
        }

        for (int i = RtpPacketResendBuffer.CAPACITY; i < 2 * RtpPacketResendBuffer.CAPACITY; i++)
        {
            Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend((ushort)i, Start, MinResendInterval, out _, out _));
        }

        var later = Start + MinResendInterval;
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.OverBudget, buffer.ClaimForResend(RtpPacketResendBuffer.CAPACITY, later, MinResendInterval, out _, out _));

        buffer.Add(2 * RtpPacketResendBuffer.CAPACITY, new byte[PACKET_LENGTH], 1, later);
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.Claimed, buffer.ClaimForResend(RtpPacketResendBuffer.CAPACITY + 1, later, MinResendInterval, out _, out _));
        Assert.Equal(RtpPacketResendBuffer.ClaimResult.OverBudget, buffer.ClaimForResend(RtpPacketResendBuffer.CAPACITY + 2, later, MinResendInterval, out _, out _));
    }
}
