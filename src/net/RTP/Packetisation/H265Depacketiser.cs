using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SIPSorcery.Net;

namespace SIPSorcery.Net
{
    public class H265Depacketiser 
    {
        const int H265NalHeaderSize = 2;

        public H265Depacketiser()
        {
            
        }


        const int SPS = 33;
        const int PPS = 34;
        const int IDR_SLICE = 1;
        const int NON_IDR_SLICE = 5;

        protected readonly int nalHeaderSize;

        //private BinaryWriter _writer = new BinaryWriter(File.Open(@"c:\temp\H264DepacketiserInput.bin", FileMode.Create));

        //Payload Helper Fields
        uint previous_timestamp = 0;
        int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24, decoding = 0; // used for diagnostics stats
        List<KeyValuePair<int, byte[]>> temporary_rtp_payloads = new List<KeyValuePair<int, byte[]>>(); // used to assemble the RTP packets that form one RTP Frame
        MemoryStream fragmented_nal = new MemoryStream(); // used to concatenate fragmented H264 NALs where NALs are splitted over RTP packets

        int writeNumber = 0;
        public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
        {
            //_writer.Write(rtpPayload);
            //_writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x42 });

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
                fragmented_nal.SetLength(0);
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
                fragmented_nal.SetLength(0);

                return nal_units;
            }
            else
            {
                isKeyFrame = false;
                previous_timestamp = rtp_timestamp;
                return null; // we don't have a frame yet. Keep accumulating RTP packets
            }
        }


        private void PrintPartOfArrayToConsole(byte[] array, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                Console.Write(array[i].ToString("X2") + " ");
            }
            Console.WriteLine();
        }

        // Process a RTP Frame. A RTP Frame can consist of several RTP Packets which have the same Timestamp
        // Returns a list of NAL Units (with no 00 00 00 01 header and with no Size header)
        protected virtual List<byte[]> ProcessH265PayloadFrame(List<KeyValuePair<int, byte[]>> rtp_payloads, out bool isKeyFrame)
        {
            bool? isKeyFrameNullable = null;
            List<byte[]> nal_units = new List<byte[]>(); // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.

            List<KeyValuePair<int, byte[]>> tmpNals = new List<KeyValuePair<int, byte[]>>();
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
                    nalUnits = ExtractNalUnitsFromAggregatedRTP(rtp_payloads[payload_index].Value, true);
                    foreach (var nalUnit in nalUnits)
                    {
                        tmpNals.Add(new KeyValuePair<int, byte[]>(0, nalUnit));
                    }
                }
                else if (nal_header_type == 49)
                {
                    byte[] nalUnit = MergeFUNalUnitsAccrossMultipleRTPPackages(rtp_payloads, ref payload_index);
                    if (nalUnit != null)
                    {
                        tmpNals.Add(new KeyValuePair<int, byte[]>(rtp_payloads[payload_index].Key, nalUnit));
                    }
                }
                else
                {
                    tmpNals.Add(rtp_payloads[payload_index]);
                }
            }
            rtp_payloads = tmpNals;



            isKeyFrame = isKeyFrameNullable != null ? isKeyFrameNullable.Value : false;

            // Output all the NALs that form one RTP Frame (one frame of video)
            return rtp_payloads.Select(payload => payload.Value).ToList();
        }

        private byte[] MergeFUNalUnitsAccrossMultipleRTPPackages(List<KeyValuePair<int, byte[]>> rtp_payloads, ref int payload_index)
        {
            List<byte> fuNal = new List<byte>();
            bool nalUnitComplete = false;
            
            while (!nalUnitComplete)
            {
                if(payload_index >= rtp_payloads.Count)
                {
                    //Invalid FU, havn't found fu_endOfNal
                    return null;
                }
                byte fuHeader = rtp_payloads[payload_index].Value[2];
                int fu_startOfNal = (fuHeader >> 7) & 0x01;  // start marker
                int fu_endOfNal = (fuHeader >> 6) & 0x01;  // end marker
                int fu_type = (fuHeader >> 1) & 0x3F; // FU Type
                if(fu_startOfNal == 1)
                {
                    byte nalHeader1 = rtp_payloads[payload_index].Value[0];
                    byte nalHeader2 = rtp_payloads[payload_index].Value[1];
                    int nal_header_f_bit = (nalHeader1 >> 7) & 0x01;
                    int nuhLayerId = ((nalHeader1 & 0x01) << 5) | ((nalHeader2 >> 3) & 0x1F);
                    int nuhTemporalIdPlus1 = nalHeader2 & 0x07;
                    var nalu1 = (byte)((nal_header_f_bit << 7) + (fu_type << 2) + (nuhLayerId >> 7));
                    var nalu2 = (byte)((nuhLayerId << 1) + (nuhTemporalIdPlus1));
                    fuNal.Add(nalu1);
                    fuNal.Add(nalu2);
                }

                if (fuNal.Count == 0 && fu_startOfNal != 1)
                {
                    //Invalid FU, first package should be start package
                    return null;
                }

                //Copy payload except RTPPayload header and FU Header
                byte[] partialNal = new byte[rtp_payloads[payload_index].Value.Length - 3];
                Array.Copy(rtp_payloads[payload_index].Value, 3, partialNal, 0, rtp_payloads[payload_index].Value.Length - 3);
                fuNal.AddRange(partialNal);
                
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

        private List<byte[]> ExtractNalUnitsFromAggregatedRTP(byte[] rtpPayload, bool firstAggregationUnit)
        {
            List<byte[]> nalUnits = new List<byte[]>();
            var foundAllUnits = false;
            int startIndex = 2; //First two bytes are Payload Header
            while (!foundAllUnits)
            {
                //byte[] DONL = new byte[2];
                //if (firstAggregationUnit)
                //{
                //    DONL[0] = rtpPayload[startIndex];
                //    DONL[1] = rtpPayload[startIndex+1];
                //    startIndex += 2; //DONL read
                //}

                UInt16 nalSize =  (UInt16)(rtpPayload[startIndex] << 8 | rtpPayload[startIndex+1]);
                startIndex += 2; //NALUnit size read
                // The first two bytes of the NAL unit contain the NAL header
                byte nalHeader1 = rtpPayload[startIndex];
                byte nalHeader2 = rtpPayload[startIndex+1];

                // Extract the fields from the NAL header
                int nal_header_f_bit = (nalHeader1 >> 7) & 0x01;
                int nal_header_type = (nalHeader1 >> 1) & 0x3F;
                int nuhLayerId = ((nalHeader1 & 0x01) << 5) | ((nalHeader2 >> 3) & 0x1F);
                int nuhTemporalIdPlus1 = nalHeader2 & 0x07;

                byte[] nal = new byte[nalSize];
                Array.Copy(rtpPayload, startIndex, nal, 0, nalSize);
                nalUnits.Add(nal);
                startIndex += nalSize;

                if ((startIndex +2) >= rtpPayload.Length)
                {
                    foundAllUnits = true;
                }
            }
            return nalUnits;
        }

        protected void CheckKeyFrame(int nal_type, ref bool? isKeyFrame)
        {
            if (isKeyFrame == null)
            {
                isKeyFrame = nal_type == SPS || nal_type == PPS ? new bool?(true) :
                    (nal_type == NON_IDR_SLICE ? new bool?(false) : null);
            }
            else
            {
                isKeyFrame = nal_type == SPS || nal_type == PPS ?
                    (isKeyFrame.Value ? isKeyFrame : new bool?(false)) :
                    (nal_type == NON_IDR_SLICE ? new bool?(false) : isKeyFrame);
            }
        }
    }
}
