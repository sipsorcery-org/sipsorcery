//-----------------------------------------------------------------------------
// Filename: RtpFecMetrics.cs
//
// Description: Observable metrics and statistics for RTP forward error correction.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Thread-safe counters and metrics for tracking primary media, generated FEC repair
    /// packets, and test impairment events.
    /// </summary>
    public class RtpFecMetrics
    {
        private long _mediaPacketsSent;
        private long _mediaBytesSent;
        private long _repairPacketsGenerated;
        private long _repairPacketsSent;
        private long _repairBytesSent;
        private long _packetsDroppedByImpairment;
        private long _transportErrors;
        private long _packetsRecovered;

        /// <summary>
        /// Total primary media packets accepted and submitted to transport.
        /// </summary>
        public long MediaPacketsSent => Interlocked.Read(ref _mediaPacketsSent);

        /// <summary>
        /// Total primary media bytes submitted to transport (including RED and RTP headers).
        /// </summary>
        public long MediaBytesSent => Interlocked.Read(ref _mediaBytesSent);

        /// <summary>
        /// Total RFC 5109 repair packets produced by the FEC encoder.
        /// </summary>
        public long RepairPacketsGenerated => Interlocked.Read(ref _repairPacketsGenerated);

        /// <summary>
        /// Total RFC 5109 repair packets submitted to transport.
        /// </summary>
        public long RepairPacketsSent => Interlocked.Read(ref _repairPacketsSent);

        /// <summary>
        /// Total RFC 5109 repair bytes submitted to transport (including RED and RTP headers).
        /// </summary>
        public long RepairBytesSent => Interlocked.Read(ref _repairBytesSent);

        /// <summary>
        /// Total lost RTP media packets successfully reconstructed and recovered by the ULPFEC decoder.
        /// </summary>
        public long PacketsRecovered => Interlocked.Read(ref _packetsRecovered);

        /// <summary>
        /// Total packets deliberately dropped by the test impairment hook.
        /// </summary>
        public long PacketsDroppedByImpairment => Interlocked.Read(ref _packetsDroppedByImpairment);

        /// <summary>
        /// Total transport errors encountered while submitting media or repair packets.
        /// </summary>
        public long TransportErrors => Interlocked.Read(ref _transportErrors);

        /// <summary>
        /// Actual repair-to-media packet overhead ratio (RepairPacketsSent / MediaPacketsSent).
        /// </summary>
        public double PacketOverheadRatio
        {
            get
            {
                long media = MediaPacketsSent;
                return media > 0 ? (double)RepairPacketsSent / media : 0.0;
            }
        }

        /// <summary>
        /// Actual repair-to-media byte overhead ratio (RepairBytesSent / MediaBytesSent).
        /// </summary>
        public double ByteOverheadRatio
        {
            get
            {
                long mediaBytes = MediaBytesSent;
                return mediaBytes > 0 ? (double)RepairBytesSent / mediaBytes : 0.0;
            }
        }

        public void RecordMediaPacket(int byteCount)
        {
            Interlocked.Increment(ref _mediaPacketsSent);
            Interlocked.Add(ref _mediaBytesSent, byteCount);
        }

        public void RecordRepairGenerated()
        {
            Interlocked.Increment(ref _repairPacketsGenerated);
        }

        public void RecordRepairSent(int byteCount)
        {
            Interlocked.Increment(ref _repairPacketsSent);
            Interlocked.Add(ref _repairBytesSent, byteCount);
        }

        public void RecordPacketRecovered()
        {
            Interlocked.Increment(ref _packetsRecovered);
        }

        public void RecordDroppedByImpairment()
        {
            Interlocked.Increment(ref _packetsDroppedByImpairment);
        }

        public void RecordTransportError()
        {
            Interlocked.Increment(ref _transportErrors);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _mediaPacketsSent, 0);
            Interlocked.Exchange(ref _mediaBytesSent, 0);
            Interlocked.Exchange(ref _repairPacketsGenerated, 0);
            Interlocked.Exchange(ref _repairPacketsSent, 0);
            Interlocked.Exchange(ref _repairBytesSent, 0);
            Interlocked.Exchange(ref _packetsDroppedByImpairment, 0);
            Interlocked.Exchange(ref _transportErrors, 0);
            Interlocked.Exchange(ref _packetsRecovered, 0);
        }

        public override string ToString()
        {
            return $"MediaPackets: {MediaPacketsSent}, MediaBytes: {MediaBytesSent}, " +
                   $"FecGenerated: {RepairPacketsGenerated}, FecSent: {RepairPacketsSent}, FecBytes: {RepairBytesSent}, " +
                   $"Recovered: {PacketsRecovered}, " +
                   $"OverheadPkt: {PacketOverheadRatio:P1}, OverheadBytes: {ByteOverheadRatio:P1}, " +
                   $"ImpairmentDrops: {PacketsDroppedByImpairment}, Errors: {TransportErrors}";
        }
    }
}
