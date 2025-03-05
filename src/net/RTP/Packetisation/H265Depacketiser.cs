//-----------------------------------------------------------------------------
// Filename: H265Depacketiser.cs
//
// Description: Implements depacktizer of H265 units. The implementation follows the RFC7798.
// The main focus is on handling Aggregated Units (AU) and Fragmentation Units (FU).
// The implementation does not support PACI packets.
//
// Author(s):
// Henrik Vincent Hein (helu@milestone.dk)
//
// History:
// 28 Feb 2025    Henrik Vincent Hein	Created, Copenhagen, Denmark.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Implements depacktizer of H265 units. The implementation follows the RFC7798.
    ///
    /// The main focus is on handling Aggregated Units (AU) and Fragmentation Units (FU). The implementation does not support PACI packets.
    /// </summary>
    public class H265Depacketiser 
    {
        const int VPS = 32;
        const int SPS = 33;
        const int PPS = 34;

        //Payload Helper Fields
        uint previous_timestamp = 0;
        List<KeyValuePair<int, byte[]>> temporary_rtp_payloads = new List<KeyValuePair<int, byte[]>>(); // used to assemble the RTP packets that form one RTP Frame

        int writeNumber = 0;
        public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
        {
            List<byte[]> nal_units = ProcessRTPPayloadAsNals(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);

            if (nal_units != null)
            {
                //Calculate total buffer size
                long totalBufferSize = 0;
                for (int i = 0; i < nal_units.Count; i++)
                {
                    var nal = nal_units[i];
                    long remaining = nal.Length;

                    if (remaining > 0)
                    {
                        totalBufferSize += remaining + 4; //nal + 0001
                    }
                    else
                    {
                        nal_units.RemoveAt(i);
                        i--;
                    }
                }

                //Merge nals in same buffer using Annex-B separator (0001)
                MemoryStream data = new MemoryStream(new byte[totalBufferSize]);
                foreach (var nal in nal_units)
                {
                    data.WriteByte(0);
                    data.WriteByte(0);
                    data.WriteByte(0);
                    data.WriteByte(1);
                    data.Write(nal, 0, nal.Length);
                }
                return data;
            }
            return null;
        }

        public virtual List<byte[]> ProcessRTPPayloadAsNals(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
        {
            List<byte[]> nal_units = ProcessH265Payload(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);

            return nal_units;
        }

        protected virtual List<byte[]> ProcessH265Payload(byte[] rtp_payload, ushort seqNum, uint rtp_timestamp, int rtp_marker, out bool isKeyFrame)
        {
            if (previous_timestamp != rtp_timestamp && previous_timestamp > 0)
            {
                temporary_rtp_payloads.Clear();
                previous_timestamp = 0;
            }

            // Add to the list of payloads for the current Frame of video
            temporary_rtp_payloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtp_payload)); // TODO could optimise this and go direct to Process Frame if just 1 packet in frame
            if (rtp_marker == 1)
            {
                //Reorder to prevent UDP incorrect package order
                if (temporary_rtp_payloads.Count > 1)
                {
                    temporary_rtp_payloads.Sort((a, b) =>
                    {
                        // Detect wraparound of sequence to sort packets correctly (Assumption that no more then 2000 packets per frame)
                        return (Math.Abs(b.Key - a.Key) > (0xFFFF - 2000)) ? -a.Key.CompareTo(b.Key) : a.Key.CompareTo(b.Key);
                    });
                }

                // End Marker is set. Process the list of RTP Packets (forming 1 RTP frame) and save the NALs to a file
                List<byte[]> nal_units = ProcessH265PayloadFrame(temporary_rtp_payloads, out isKeyFrame);
                temporary_rtp_payloads.Clear();
                previous_timestamp = 0;

                return nal_units;
            }
            else
            {
                isKeyFrame = false;
                previous_timestamp = rtp_timestamp;
                return null; // we don't have a frame yet. Keep accumulating RTP packets
            }
        }

        // Process a RTP Frame. A RTP Frame can consist of several RTP Packets which have the same Timestamp
        // Returns a list of NAL Units (with no 00 00 00 01 header and with no Size header)
        protected virtual List<byte[]> ProcessH265PayloadFrame(List<KeyValuePair<int, byte[]>> rtp_payloads, out bool isKeyFrame)
        {
            bool? isKeyFrameNullable = null;
            List<byte[]> nal_units = new List<byte[]>(); // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.

            //check payload for Payload headers 48 and 49
            for (int payload_index = 0; payload_index < rtp_payloads.Count; payload_index++)
            {
                // The first two bytes of the NAL unit contain the NAL header
                byte nalHeader1 = rtp_payloads[payload_index].Value[0];
                byte nalHeader2 = rtp_payloads[payload_index].Value[1];

                // Extract the fields from the NAL header
                int nal_header_f_bit = (nalHeader1 >> 7) & 0x01;
                int nal_header_type = (nalHeader1 >> 1) & 0x3F;
                int nuhLayerId = ((nalHeader1 & 0x01) << 5) | ((nalHeader2 >> 3) & 0x1F);
                int nuhTemporalIdPlus1 = nalHeader2 & 0x07;

                List<byte[]> nalUnits = new List<byte[]>();
                if (nal_header_type == 48)
                {
                    //aggregated RTP Payload
                    nalUnits = ExtractNalUnitsFromAggregatedRTP(rtp_payloads[payload_index].Value);
                    foreach (var nalUnit in nalUnits)
                    {
                        nal_units.Add(nalUnit);
                    }
                }
                else if (nal_header_type == 49)
                {
                    
                    byte[] nalUnit = MergeFUNalUnitsAccrossMultipleRTPPackages(rtp_payloads.Select(x => x.Value).ToList(), ref payload_index);
                    if (nalUnit != null)
                    {
                        nal_units.Add(nalUnit);
                    }
                }
                else
                {
                    nal_units.Add(rtp_payloads[payload_index].Value);
                }
            }
            isKeyFrame = CheckKeyFrame(nal_units);

            // Output all the NALs that form one RTP Frame (one frame of video)
            return nal_units;
        }

        private byte[] MergeFUNalUnitsAccrossMultipleRTPPackages(List<byte[]> rtp_payloads, ref int payload_index)
        {
            using (MemoryStream fuNal = new MemoryStream())
            {
                bool nalUnitComplete = false;

                while (!nalUnitComplete)
                {
                    if (payload_index >= rtp_payloads.Count())
                    {
                        //Invalid FU, havn't found fu_endOfNal
                        return null;
                    }
                    byte[] payload = rtp_payloads[payload_index];


                    byte fuHeader = payload[2];
                    int fu_startOfNal = (fuHeader >> 7) & 0x01;  // start marker
                    int fu_endOfNal = (fuHeader >> 6) & 0x01;  // end marker
                    int fu_type = (fuHeader >> 1) & 0x3F; // FU Type
                    if (fu_startOfNal == 1)
                    {
                        byte nalHeader1 = payload[0];
                        byte nalHeader2 = payload[1];
                        int nal_header_f_bit = (nalHeader1 >> 7) & 0x01;
                        int nuhLayerId = ((nalHeader1 & 0x01) << 5) | ((nalHeader2 >> 3) & 0x1F);
                        int nuhTemporalIdPlus1 = nalHeader2 & 0x07;
                        var fu_nalHeader1 = (byte)((nal_header_f_bit << 7) + (fu_type << 2) + (nuhLayerId >> 7));
                        var fu_nalHeader2 = (byte)((nuhLayerId << 1) + (nuhTemporalIdPlus1));
                        fuNal.WriteByte(fu_nalHeader1);
                        fuNal.WriteByte(fu_nalHeader2);
                    }

                    if (fuNal.Length == 0 && fu_startOfNal != 1)
                    {
                        //Invalid FU, first package should be start package
                        return null;
                    }

                    //Copy payload except Payload header and FU Header
                    fuNal.Write(payload, 3, rtp_payloads[payload_index].Length - 3);

                    if (fu_endOfNal == 1)
                    {
                        nalUnitComplete = true;
                    }
                    else
                    {
                        payload_index++;
                    }
                }
                return fuNal.ToArray();
            }
        }

        private List<byte[]> ExtractNalUnitsFromAggregatedRTP(byte[] rtpPayload)
        {
            List<byte[]> nalUnits = new List<byte[]>();
            int startIndex = 2; //First two bytes are Payload Header, ignore
            while (startIndex < rtpPayload.Length)
            {
                if (startIndex + 2 >= rtpPayload.Length)
                {
                    //Not enough data for NAL size
                    break;
                }

                int nalSize =  rtpPayload[startIndex] << 8 | rtpPayload[startIndex+1];
                startIndex += 2; //NALUnit size read

                if(startIndex + nalSize > rtpPayload.Length)
                {
                    //Not enough data for NALUnit
                    break;
                }

                byte[] nal = new byte[nalSize];
                Buffer.BlockCopy(rtpPayload, startIndex, nal, 0, nalSize);
                nalUnits.Add(nal);
                startIndex += nalSize;
            }
            return nalUnits;
        }

        protected bool CheckKeyFrame(List<byte[]> nalUnits)
        {
            foreach(var nalUnit in nalUnits)
            {
                int nal_type = (nalUnit[0] >> 1) & 0x3F; ;
                if(nal_type == SPS ||
                    nal_type == PPS ||
                    nal_type == VPS)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
