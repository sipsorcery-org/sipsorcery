//-----------------------------------------------------------------------------
// Filename: ReceptionReport.cs
//
// Description: One or more reception report blocks are included in each
// RTCP Sender and Receiver report.

//
//        RTCP Reception Report Block
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// report |                 SSRC_1(SSRC of first source)                  |
// block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  1     | fraction lost |       cumulative number of packets lost       |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |           extended highest sequence number received           |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                      interarrival jitter                      |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         last SR(LSR)                          |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                   delay since last SR(DLSR)                   |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents a point in time sample for a reception report.
    /// </summary>
    public class ReceptionReportSample
    {
        public const int PAYLOAD_SIZE = 24;

        /// <summary>
        /// Data source being reported.
        /// </summary>
        public uint SSRC;

        /// <summary>
        /// Fraction lost since last SR/RR.
        /// </summary>
        public byte FractionLost;

        /// <summary>
        /// Cumulative number of packets lost (signed!).
        /// </summary>
        public int PacketsLost;

        /// <summary>
        /// Extended last sequence number received.
        /// </summary>
        public uint ExtendedHighestSequenceNumber;

        /// <summary>
        /// Interarrival jitter.
        /// </summary>
        public uint Jitter;

        /// <summary>
        /// Last SR packet from this source.
        /// </summary>
        public uint LastSenderReportTimestamp;

        /// <summary>
        /// Delay since last SR packet.
        /// </summary>
        public uint DelaySinceLastSenderReport;

        /// <summary>
        /// Creates a new Reception Report object.
        /// </summary>
        /// <param name="ssrc">The synchronisation source this reception report is for.</param>
        /// <param name="fractionLost">The fraction of RTP packets lost since the previous Sender or Receiver
        /// Report was sent.</param>
        /// <param name="packetsLost">The total number of RTP packets that have been lost since the
        /// beginning of reception.</param>
        /// <param name="highestSeqNum">Extended highest sequence number received from source.</param>
        /// <param name="jitter">Interarrival jitter of the RTP packets received within the last reporting period.</param>
        /// <param name="lastSRTimestamp">The timestamp from the most recent RTCP Sender Report packet
        /// received.</param>
        /// <param name="delaySinceLastSR">The delay between receiving the last Sender Report packet and the sending
        /// of this Reception Report.</param>
        public ReceptionReportSample(
            uint ssrc,
            byte fractionLost,
            int packetsLost,
            uint highestSeqNum,
            uint jitter,
            uint lastSRTimestamp,
            uint delaySinceLastSR)
        {
            SSRC = ssrc;
            FractionLost = fractionLost;
            PacketsLost = packetsLost;
            ExtendedHighestSequenceNumber = highestSeqNum;
            Jitter = jitter;
            LastSenderReportTimestamp = lastSRTimestamp;
            DelaySinceLastSenderReport = delaySinceLastSR;
        }

        public ReceptionReportSample(byte[] packet)
        {
            if (BitConverter.IsLittleEndian)
            {
                SSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 0));
                FractionLost = packet[4];
                PacketsLost = NetConvert.DoReverseEndian(BitConverter.ToInt32(new byte[] { 0x00, packet[5], packet[6], packet[7] }, 0));
                ExtendedHighestSequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 8));
                Jitter = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 12));
                LastSenderReportTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 16));
                DelaySinceLastSenderReport = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 20));
            }
            else
            {
                SSRC = BitConverter.ToUInt32(packet, 4);
                FractionLost = packet[4];
                PacketsLost = BitConverter.ToInt32(new byte[] { 0x00, packet[5], packet[6], packet[7] }, 0);
                ExtendedHighestSequenceNumber = BitConverter.ToUInt32(packet, 8);
                Jitter = BitConverter.ToUInt32(packet, 12);
                LastSenderReportTimestamp = BitConverter.ToUInt32(packet, 16);
                LastSenderReportTimestamp = BitConverter.ToUInt32(packet, 20);
            }
        }

        /// <summary>
        /// Serialises the reception report block to a byte array.
        /// </summary>
        /// <returns>A byte array.</returns>
        public byte[] GetBytes()
        {
            byte[] payload = new byte[24];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, payload, 0, 4);
                payload[4] = FractionLost;
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PacketsLost)), 1, payload, 5, 3);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtendedHighestSequenceNumber)), 0, payload, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Jitter)), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(LastSenderReportTimestamp)), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(DelaySinceLastSenderReport)), 0, payload, 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, payload, 0, 4);
                payload[4] = FractionLost;
                Buffer.BlockCopy(BitConverter.GetBytes(PacketsLost), 1, payload, 5, 3);
                Buffer.BlockCopy(BitConverter.GetBytes(ExtendedHighestSequenceNumber), 0, payload, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(Jitter), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(LastSenderReportTimestamp), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(DelaySinceLastSenderReport), 0, payload, 20, 4);
            }

            return payload;
        }
    }

    /// <summary>
    /// Maintains the reception statistics for a received RTP stream.
    /// </summary>
    public class ReceptionReport
    {
        //private const int MAX_DROPOUT = 3000;
        //private const int MAX_MISORDER = 100;
        //private const int MIN_SEQUENTIAL = 2;
        private const int RTP_SEQ_MOD = 1 << 16;
        //private const int MAX_POSITIVE_LOSS = 0x7fffff;
        //private const int MAX_NEGATIVE_LOSS = 0x800000;
        private const int SEQ_NUM_WRAP_LOW = 256;
        private const int SEQ_NUM_WRAP_HIGH = 65280;

        /// <summary>
        /// Data source being reported.
        /// </summary>
        public uint SSRC;

        /// <summary>
        /// highest seq. number seen
        /// </summary>
        private ushort m_max_seq;

        /// <summary>
        /// Increments by UInt16.MaxValue each time the sequence number wraps around.
        /// </summary>
        private ulong m_cycles;

        /// <summary>
        /// The first sequence number received.
        /// </summary>
        private uint m_base_seq;

        /// <summary>
        /// last 'bad' seq number + 1.
        /// </summary>
        private uint m_bad_seq;

        /// <summary>
        /// sequ. packets till source is valid.
        /// </summary>
        //private uint m_probation;

        /// <summary>
        /// packets received.
        /// </summary>
        private uint m_received;

        /// <summary>
        /// packet expected at last interval.
        /// </summary>
        private ulong m_expected_prior;

        /// <summary>
        /// packet received at last interval.
        /// </summary>
        private uint m_received_prior;

        /// <summary>
        /// relative trans time for prev pkt.
        /// </summary>
        private uint m_transit;

        /// <summary>
        /// Estimated jitter.
        /// </summary>
        private uint m_jitter;

        /// <summary>
        /// Last SR packet from this source.
        /// </summary>
        private uint m_lastSenderReportTimestamp;

        /// <summary>
        /// Datetime the last sender report was received at.
        /// </summary>
        private DateTime m_lastSenderReportReceivedAt = DateTime.MinValue;

        /// <summary>
        /// Creates a new Reception Report object.
        /// </summary>
        /// <param name="ssrc">The synchronisation source this reception report is for.</param>
        public ReceptionReport(uint ssrc)
        {
            SSRC = ssrc;
        }

        /// <summary>
        /// Updates the state when an RTCP sender report is received from the remote party.
        /// </summary>
        /// <param name="srNtpTimestamp">The sender report timestamp.</param>
        internal void RtcpSenderReportReceived(ulong srNtpTimestamp)
        {
            m_lastSenderReportTimestamp = (uint)((srNtpTimestamp >> 16) & 0xFFFFFFFF);
            m_lastSenderReportReceivedAt = DateTime.Now;
        }

        /// <summary>
        /// Carries out the calculations required to measure properties related to the reception of 
        /// received RTP packets. The algorithms employed are:
        ///  - RFC3550 A.1 RTP Data Header Validity Checks (for sequence number calculations).
        ///  - RFC3550 A.3 Determining Number of Packets Expected and Lost.
        ///  - RFC3550 A.8 Estimating the Interarrival Jitter.
        /// </summary>
        /// <param name="seq">The sequence number in the RTP header.</param>
        /// <param name="rtpTimestamp">The timestamp in the RTP header.</param>
        /// <param name="arrivalTimestamp">The current timestamp in the SAME units as the RTP timestamp.
        /// For example for 8Khz audio the arrival timestamp needs 8000 ticks per second.</param>
        internal void RtpPacketReceived(ushort seq, uint rtpTimestamp, uint arrivalTimestamp)
        {
            // Sequence number calculations and cycles as per RFC3550 Appendix A.1.
            //if (m_received == 0)
            //{
            //    init_seq(seq);
            //    m_max_seq = (ushort)(seq - 1);
            //    m_probation = MIN_SEQUENTIAL;
            //}
            //bool ready = update_seq(seq);

            if(m_received == 0)
            {
                m_base_seq = seq;
            }

            m_received++;

            if (seq == m_max_seq + 1)
            {
                // Packet is in sequence.
                m_max_seq = seq; 
            }
            else if(seq == 0 && m_max_seq == ushort.MaxValue)
            {
                // Packet is in sequence and a wrap around has occurred.
                m_max_seq = seq;
                m_cycles += RTP_SEQ_MOD;
            }
            else
            {
                // Out of order, duplicate or skipped sequence number.
                if(seq > m_max_seq)
                {
                    // Seqnum is greater than expected. RTP packet is dropped or out of order.
                    m_max_seq = seq;
                }
                else if(seq < SEQ_NUM_WRAP_LOW && m_max_seq > SEQ_NUM_WRAP_HIGH)
                {
                    // Seqnum is out of order and has wrapped.
                    m_max_seq = seq;
                    m_cycles += RTP_SEQ_MOD;
                }
                else
                {
                    // Remaining conditions are:
                    // - seqnum == m_max_seq indicating a duplicate RTP packet, or
                    // - is seqnum is more than 1 less than m_max_seqnum. Which most 
                    //   likely indicates an RTP packet was delivered out of order.
                    m_bad_seq++;
                }
             }

            // Estimating the Interarrival Jitter as defined in RFC3550 Appendix A.8.
            uint transit = arrivalTimestamp - rtpTimestamp;
            int d = (int)(transit - m_transit);
            m_transit = transit;
            if (d < 0)
            {
                d = -d;
            }
            m_jitter += (uint)(d - ((m_jitter + 8) >> 4));

            //return ready;
        }

        /// <summary>
        /// Gets a point in time sample for the reception report.
        /// </summary>
        /// <returns>A reception report sample.</returns>
        public ReceptionReportSample GetSample(uint ntpTimestampNow)
        {
            // Determining the number of packets expected and lost in RFC3550 Appendix A.3.
            ulong extended_max = m_cycles + m_max_seq;
            ulong expected = extended_max - m_base_seq + 1;
            //int lost = (m_received == 0) ? 0 : (int)(expected - m_received);

            ulong expected_interval = expected - m_expected_prior;
            m_expected_prior = expected;
            uint received_interval = m_received - m_received_prior;
            m_received_prior = m_received;
            ulong lost_interval = (m_received == 0) ? 0 : expected_interval - received_interval;
            byte fraction = (byte)((expected_interval == 0 || lost_interval <= 0) ? 0 : (lost_interval << 8) / expected_interval);

            // In this case, the estimate is sampled for the reception report as:
            uint jitter = m_jitter >> 4;

            uint delay = 0;
            if (m_lastSenderReportReceivedAt != DateTime.MinValue)
            {
                delay = ntpTimestampNow - m_lastSenderReportTimestamp;
            }

            return new ReceptionReportSample(SSRC, fraction, (int)lost_interval, m_max_seq, jitter, m_lastSenderReportTimestamp, delay);
        }

        /// <summary>
        /// NOTE 20 Dec 2020: This algorigthm. from RFC3550 Appendix A.1 is intended as part of determining when a new
        /// RTP source should be accepted as valid. The intention is not necessarily to be used to determine when 
        /// a reception report can be generated, which was wat it was being used for here.
        /// 
        /// Initialises the sequence number state for the reception RTP stream.
        /// This method is from RFC3550 Appendix A.1 "RTP Data Header Validity Checks".
        /// </summary>
        /// <param name="seq">The sequence number from the received RTP packet that triggered this update.</param>
        //void init_seq(ushort seq)
        //{
        //    m_base_seq = seq;
        //    m_max_seq = seq;
        //    m_bad_seq = RTP_SEQ_MOD + 1;   /* so seq == bad_seq is false */
        //    m_cycles = 0;
        //    m_received = 0;
        //    m_received_prior = 0;
        //    m_expected_prior = 0;
        //}

        /// <summary>
        /// NOTE 20 Dec 2020: This algorigthm. from RFC3550 Appendix A.1 is intended to decide when a new RTP
        /// source should be accepted as valid. The intention is not necessarily to be used to determine when 
        /// a reception report can be generated, which was wat it was being used for here.
        /// 
        /// Update the sequence number state for the reception RTP stream.
        /// This method is from RFC3550 Appendix A.1 "RTP Data Header Validity Checks".
        /// </summary>
        /// <param name="seq">The sequence number from the received RTP packet that triggered this update.</param>
        /// <returns>True when the required number of packets have been received and a report can be generated. False
        /// indicates not yet enough data.</returns>
        //bool update_seq(ushort seq)
        //{
        //    ushort udelta = (ushort)(seq - m_max_seq);

        //    /*
        //     * Source is not valid until MIN_SEQUENTIAL packets with
        //     * sequential sequence numbers have been received.
        //     */
        //    if (m_probation > 0)
        //    {
        //        /* packet is in sequence */
        //        if (seq == m_max_seq + 1)
        //        {
        //            m_probation--;
        //            m_max_seq = seq;
        //            if (m_probation == 0)
        //            {
        //                init_seq(seq);
        //                m_received++;
        //                return false;
        //            }
        //        }
        //        else
        //        {
        //            m_probation = MIN_SEQUENTIAL - 1;
        //            m_max_seq = seq;
        //        }
        //        return true;
        //    }
        //    else if (udelta < MAX_DROPOUT)
        //    {
        //        /* in order, with permissible gap */
        //        if (seq < m_max_seq)
        //        {
        //            /*
        //             * Sequence number wrapped - count another 64K cycle.
        //             */
        //            m_cycles += RTP_SEQ_MOD;
        //        }
        //        m_max_seq = seq;
        //    }
        //    else if (udelta <= RTP_SEQ_MOD - MAX_MISORDER)
        //    {
        //        /* the sequence number made a very large jump */
        //        if (seq == m_bad_seq)
        //        {
        //            /*
        //             * Two sequential packets -- assume that the other side
        //             * restarted without telling us so just re-sync
        //             * (i.e., pretend this was the first packet).
        //             */
        //            init_seq(seq);
        //        }
        //        else
        //        {
        //            m_bad_seq = (uint)((seq + 1) & (RTP_SEQ_MOD - 1));
        //            return true;
        //        }
        //    }
        //    else
        //    {
        //        /* duplicate or reordered packet */
        //    }
        //    m_received++;
        //    return false;
        //}
    }
}
