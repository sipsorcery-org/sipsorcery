//-----------------------------------------------------------------------------
// Filename: RtpUlpfecConfig.cs
//
// Description: Configuration options and policies for RTP ULPFEC (RFC 5109)
// and RED (RFC 2198) video forward error correction.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Configuration options and protection policy for RTP ULPFEC (RFC 5109)
    /// encapsulated in RED (RFC 2198).
    /// </summary>
    public class RtpUlpfecConfig
    {
        /// <summary>
        /// Default FEC overhead fraction (20% of media packet count, or ~16.7% of total wire packets).
        /// For example, 1 repair packet emitted for every 5 media packets.
        /// </summary>
        public const float DEFAULT_OVERHEAD_FRACTION = 0.20f;

        /// <summary>
        /// Maximum media packets that can be protected in a single RFC 5109 Level 0 group.
        /// RFC 5109 short masks support up to 16 packets; long masks support up to 48.
        /// Default is 16 to keep mask size at 2 bytes (short mask).
        /// </summary>
        public const int DEFAULT_MAX_GROUP_PACKETS = 16;

        /// <summary>
        /// Minimum number of media packets required in a group before FEC is emitted,
        /// unless a frame boundary (MarkerBit = 1) is reached.
        /// </summary>
        public const int DEFAULT_MIN_GROUP_PACKETS = 1;

        /// <summary>
        /// Maximum duration to buffer a partial protection group before flushing repair packets,
        /// preventing stalls on low frame rate or sparse streams.
        /// </summary>
        public static readonly TimeSpan DEFAULT_MAX_REPAIR_AGE = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// Target repair-to-media ratio (e.g. 0.20 = 20% parity, 1 parity packet per 5 media packets).
        /// Range is 0.0f (no FEC) to 1.0f (100% parity, 1 parity packet per media packet).
        /// </summary>
        public float TargetOverheadFraction { get; set; } = DEFAULT_OVERHEAD_FRACTION;

        /// <summary>
        /// Maximum number of media packets covered by a single FEC repair packet.
        /// Valid range: 1 to 48. Default is 16.
        /// </summary>
        public int MaxGroupPackets { get; set; } = DEFAULT_MAX_GROUP_PACKETS;

        /// <summary>
        /// Minimum number of media packets before generating FEC for a non-boundary group.
        /// </summary>
        public int MinGroupPackets { get; set; } = DEFAULT_MIN_GROUP_PACKETS;

        /// <summary>
        /// Maximum age of a partial protection group before forcing an emission.
        /// </summary>
        public TimeSpan MaxRepairAge { get; set; } = DEFAULT_MAX_REPAIR_AGE;

        /// <summary>
        /// Maximum time to wait for missing received video packets before abandoning an incomplete
        /// frame. Complete frames are delivered immediately. Zero disables waiting, not ordering.
        /// </summary>
        public TimeSpan MaxReceiveRepairWait { get; set; } = TimeSpan.FromMilliseconds(20);

        /// <summary>
        /// If true, use RFC 5109 long mask (48-bit mask / 8-byte Level 0 header) when the group
        /// span exceeds 16 packets. If false, limits group size to 16 packets (short mask).
        /// </summary>
        public bool AllowLongMask { get; set; } = true;

        /// <summary>
        /// Creates a deep copy of this configuration.
        /// </summary>
        public RtpUlpfecConfig Clone()
        {
            return new RtpUlpfecConfig
            {
                TargetOverheadFraction = TargetOverheadFraction,
                MaxGroupPackets = MaxGroupPackets,
                MinGroupPackets = MinGroupPackets,
                MaxRepairAge = MaxRepairAge,
                MaxReceiveRepairWait = MaxReceiveRepairWait,
                AllowLongMask = AllowLongMask
            };
        }
    }
}
