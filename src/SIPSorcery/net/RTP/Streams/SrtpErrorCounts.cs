//-----------------------------------------------------------------------------
// Filename: SrtpErrorCounts.cs
//
// Description: Counts of the packets on a media stream that couldn't be decrypted,
// by why, for diagnostics.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Nov 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The SRTP (or SRTCP) packets on a media stream that couldn't be unprotected, by why. An authentication
    /// failure means the keys don't match: the remote peer changed them (a re-INVITE with new crypto) without
    /// this side taking them up, or something between rewrote the SDP. A replay is a packet already seen, or
    /// too old for the replay window: a duplicate on the network, or packets from an earlier session arriving
    /// on the same port. Counted whether or not the failure is logged.
    /// </summary>
    public sealed class SrtpErrorCounts
    {
        // SrtpContext's result codes (SIPSorcery.Net.SharpSRTP.SRTP.SrtpContext.ERROR_*).
        private const int ERROR_HMAC_CHECK_FAILED = -3;
        private const int ERROR_REPLAY_CHECK_FAILED = -4;

        private long _authentication;
        private long _replay;
        private long _other;
        private long _lastAtTicks;

        /// <summary>Packets whose authentication tag didn't match: the keys differ.</summary>
        public long Authentication => Interlocked.Read(ref _authentication);

        /// <summary>Packets the replay check turned away: already seen, or too old.</summary>
        public long Replay => Interlocked.Read(ref _replay);

        /// <summary>Packets that failed for any other reason (an unsupported cipher, an MKI that didn't match, a key past its lifetime).</summary>
        public long Other => Interlocked.Read(ref _other);

        /// <summary>All of them.</summary>
        public long Total => Authentication + Replay + Other;

        /// <summary>When the most recent failure was, or null if there has been none.</summary>
        public DateTime? LastAt
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastAtTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Local);
            }
        }

        /// <summary>Counts a failure, from the unprotect function's result.</summary>
        internal void Record(int result)
        {
            switch (result)
            {
                case ERROR_HMAC_CHECK_FAILED: Interlocked.Increment(ref _authentication); break;
                case ERROR_REPLAY_CHECK_FAILED: Interlocked.Increment(ref _replay); break;
                default: Interlocked.Increment(ref _other); break;
            }
            Interlocked.Exchange(ref _lastAtTicks, DateTime.Now.Ticks);
        }

        public override string ToString() => $"{Authentication} authentication, {Replay} replay, {Other} other";
    }
}
