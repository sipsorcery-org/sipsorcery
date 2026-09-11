//-----------------------------------------------------------------------------
// Filename: RtpStreamStats.cs
//
// Description: Tracks RTP sequence numbers for one media stream to detect
// gaps, reordering and duplicates. Shared by the verbs that receive media
// (webrtc whep, webrtc whip-server).
//
// A gap (lost packet) at this layer means the packet never reached the
// application: genuine network loss, but also packets discarded inside the
// library, e.g. SRTP authentication failures, which makes a non-zero value a
// prompt to rerun with --verbose and look closer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class RtpStreamStats
{
    public enum RecordKind
    {
        InOrder,
        OutOfOrder,
        Duplicate
    }

    public readonly record struct RecordOutcome(RecordKind Kind, ushort PreviousHighestSeq);

    private readonly object _lock = new();
    private bool _hasFirst;
    private ushort _highestSeq;
    private long _cycles;            // count of 16 bit sequence number wraps observed.
    private long _firstExtended;
    private long _highestExtended;

    public int Packets { get; private set; }
    public int OutOfOrder { get; private set; }
    public int Duplicates { get; private set; }

    /// <summary>Expected packet count from first to highest sequence number, inclusive.</summary>
    public long Expected
    {
        get { lock (_lock) { return _hasFirst ? _highestExtended - _firstExtended + 1 : 0; } }
    }

    /// <summary>Sequence gaps: expected minus received (never negative).</summary>
    public long Lost => Math.Max(0, Expected - Packets);

    public RecordOutcome Record(ushort seq)
    {
        lock (_lock)
        {
            if (!_hasFirst)
            {
                _hasFirst = true;
                _highestSeq = seq;
                _firstExtended = seq;
                _highestExtended = seq;
                Packets = 1;
                return new RecordOutcome(RecordKind.InOrder, seq);
            }

            Packets++;
            ushort previousHighest = _highestSeq;

            // Extend the 16 bit sequence number relative to the highest seen, allowing for
            // wraps in both directions (the same estimation problem as RFC 3711 Appendix A).
            long extended;
            if (seq >= _highestSeq)
            {
                extended = seq - _highestSeq < 32768
                    ? _cycles * 65536 + seq            // in order or small forward jump.
                    : (_cycles - 1) * 65536 + seq;     // straggler from before a recent wrap.
            }
            else
            {
                if (_highestSeq - seq < 32768)
                {
                    extended = _cycles * 65536 + seq;  // late packet in the current cycle.
                }
                else
                {
                    _cycles++;                         // the sequence number wrapped forward.
                    extended = _cycles * 65536 + seq;
                }
            }

            if (extended > _highestExtended)
            {
                _highestExtended = extended;
                _highestSeq = seq;
                return new RecordOutcome(RecordKind.InOrder, previousHighest);
            }

            // A repeat of the current highest is reported as a duplicate (a duplicate of an
            // older packet cannot be distinguished from a late arrival without a full window
            // and is reported as out of order).
            if (extended == _highestExtended)
            {
                Duplicates++;
                return new RecordOutcome(RecordKind.Duplicate, previousHighest);
            }

            OutOfOrder++;
            return new RecordOutcome(RecordKind.OutOfOrder, previousHighest);
        }
    }

    /// <summary>
    /// Creates an RTP packet handler that records audio and video packets against the supplied
    /// stats and logs each anomaly at debug level.
    /// </summary>
    public static Action<System.Net.IPEndPoint, SDPMediaTypesEnum, RTPPacket> CreateRtpHandler(
        RtpStreamStats audioStats, RtpStreamStats videoStats, ILogger logger)
    {
        return (remoteEndPoint, mediaType, rtpPacket) =>
        {
            var stats = mediaType == SDPMediaTypesEnum.audio ? audioStats
                      : mediaType == SDPMediaTypesEnum.video ? videoStats
                      : null;

            if (stats != null)
            {
                ushort seq = (ushort)rtpPacket.Header.SequenceNumber;
                var outcome = stats.Record(seq);

                if (outcome.Kind != RecordKind.InOrder)
                {
                    logger.LogDebug("{MediaType} packet seq {Seq} arrived {Kind} (highest seen {Highest}, ssrc {Ssrc}).",
                        mediaType, seq, outcome.Kind, outcome.PreviousHighestSeq, rtpPacket.Header.SyncSource);
                }
            }
        };
    }
}
