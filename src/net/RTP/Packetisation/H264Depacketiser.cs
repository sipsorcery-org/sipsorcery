
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// Based in https://github.com/BogdanovKirill/RtspClientSharp/blob/master/RtspClientSharp/MediaParsers/H264VideoPayloadParser.cs 
/// Distributed under MIT License
/// 
/// @author raf.csoares@kyubinteractive.com
/// </summary>
public class H264Depacketiser
{
    private const int SPS = 7;
    private const int PPS = 8;
    private const int IDR_SLICE = 1;
    private const int NON_IDR_SLICE = 5;

    //Payload Helper Fields
    private uint previous_timestamp;
    private int norm, fu_a, fu_b, stap_a, stap_b, mtap16, mtap24; // used for diagnostics stats
    private List<KeyValuePair<int, byte[]>> temporary_rtp_payloads = new List<KeyValuePair<int, byte[]>>(); // used to assemble the RTP packets that form one RTP Frame
    private MemoryStream fragmented_nal = new MemoryStream(); // used to concatenate fragmented H264 NALs where NALs are splitted over RTP packets

    public virtual bool ProcessRTPPayload(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
    {
        var nal_units = ProcessRTPPayloadAsNals(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);

        if (nal_units is null)
        {
            return false;
        }

        //Calculate total buffer size
        long totalBufferSize = 0;
        for (var i = 0; i < nal_units.Count; i++)
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
        var data = new MemoryStream(new byte[totalBufferSize]);
        foreach (var nal in nal_units)
        {
            bufferWriter.Write(new ReadOnlySpan<byte>([0, 0, 0, 1]));
            bufferWriter.Write(nal.AsSpan());
        }

        return true;
    }

    public virtual List<byte[]>? ProcessRTPPayloadAsNals(ReadOnlySpan<byte> rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
    {
        var nal_units = ProcessH264Payload(rtpPayload, seqNum, timestamp, markbit, out isKeyFrame);

        return nal_units;
    }

    protected virtual List<byte[]>? ProcessH264Payload(ReadOnlySpan<byte> rtp_payload, ushort seqNum, uint rtp_timestamp, int rtp_marker, out bool isKeyFrame)
    {
        if (previous_timestamp != rtp_timestamp && previous_timestamp > 0)
        {
            temporary_rtp_payloads.Clear();
            previous_timestamp = 0;
            fragmented_nal.SetLength(0);
        }

        // Add to the list of payloads for the current Frame of video
        temporary_rtp_payloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtp_payload.ToArray())); // TODO could optimise this and go direct to Process Frame if just 1 packet in frame
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
            var nal_units = ProcessH264PayloadFrame(temporary_rtp_payloads, out isKeyFrame);
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

    // Process a RTP Frame. A RTP Frame can consist of several RTP Packets which have the same Timestamp
    // Returns a list of NAL Units (with no 00 00 00 01 header and with no Size header)
    protected virtual List<byte[]> ProcessH264PayloadFrame(List<KeyValuePair<int, byte[]>> rtp_payloads, out bool isKeyFrame)
    {
        bool? isKeyFrameNullable = null;
        var nal_units = new List<byte[]>(); // Stores the NAL units for a Video Frame. May be more than one NAL unit in a video frame.

        for (var payload_index = 0; payload_index < rtp_payloads.Count; payload_index++)
        {
            // Examine the first rtp_payload and the first byte (the NAL header)
            var nal_header_f_bit = (rtp_payloads[payload_index].Value[0] >> 7) & 0x01;
            var nal_header_nri = (rtp_payloads[payload_index].Value[0] >> 5) & 0x03;
            var nal_header_type = (rtp_payloads[payload_index].Value[0] >> 0) & 0x1F;

            // If the Nal Header Type is in the range 1..23 this is a normal NAL (not fragmented)
            // So write the NAL to the file
            if (nal_header_type is >= 1 and <= 23)
            {
                norm++;
                //Check if is Key Frame
                CheckKeyFrame(nal_header_type, ref isKeyFrameNullable);

                nal_units.Add(rtp_payloads[payload_index].Value);
            }
            // There are 4 types of Aggregation Packet (split over RTP payloads)
            else if (nal_header_type == 24)
            {
                stap_a++;

                // RTP packet contains multiple NALs, each with a 16 bit header
                //   Read 16 byte size
                //   Read NAL
                try
                {
                    var ptr = 1; // start after the nal_header_type which was '24'
                    // if we have at least 2 more bytes (the 16 bit size) then consume more data
                    while (ptr + 2 < (rtp_payloads[payload_index].Value.Length - 1))
                    {
                        var size = (rtp_payloads[payload_index].Value[ptr] << 8) + (rtp_payloads[payload_index].Value[ptr + 1] << 0);
                        ptr = ptr + 2;
                        var nal = new byte[size];
                        Buffer.BlockCopy(rtp_payloads[payload_index].Value, ptr, nal, 0, size); // copy the NAL

                        var reconstructed_nal_type = (byte)((nal[0] >> 0) & 0x1F);
                        //Check if is Key Frame
                        CheckKeyFrame(reconstructed_nal_type, ref isKeyFrameNullable);

                        nal_units.Add(nal); // Add to list of NALs for this RTP frame. Start Codes like 00 00 00 01 get added later
                        ptr = ptr + size;
                    }
                }
                catch
                {
                }
            }
            else if (nal_header_type == 25)
            {
                stap_b++;
            }
            else if (nal_header_type == 26)
            {
                mtap16++;
            }
            else if (nal_header_type == 27)
            {
                mtap24++;
            }
            else if (nal_header_type == 28)
            {
                fu_a++;

                // Parse Fragmentation Unit Header
                int fu_indicator = rtp_payloads[payload_index].Value[0];
                var fu_header_s = (rtp_payloads[payload_index].Value[1] >> 7) & 0x01;  // start marker
                var fu_header_e = (rtp_payloads[payload_index].Value[1] >> 6) & 0x01;  // end marker
                var fu_header_r = (rtp_payloads[payload_index].Value[1] >> 5) & 0x01;  // reserved. should be 0
                var fu_header_type = (rtp_payloads[payload_index].Value[1] >> 0) & 0x1F; // Original NAL unit header

                // Check Start and End flags
                if (fu_header_s == 1 && fu_header_e == 0)
                {
                    // Start of Fragment.
                    // Initialise the fragmented_nal byte array
                    // Build the NAL header with the original F and NRI flags but use the the Type field from the fu_header_type
                    var reconstructed_nal_type = (byte)((nal_header_f_bit << 7) + (nal_header_nri << 5) + fu_header_type);

                    // Empty the stream
                    fragmented_nal.SetLength(0);

                    // Add reconstructed_nal_type byte to the memory stream
                    fragmented_nal.WriteByte((byte)reconstructed_nal_type);

                    // copy the rest of the RTP payload to the memory stream
                    fragmented_nal.Write(rtp_payloads[payload_index].Value, 2, rtp_payloads[payload_index].Value.Length - 2);
                }

                if (fu_header_s == 0 && fu_header_e == 0)
                {
                    // Middle part of Fragment
                    // Append this payload to the fragmented_nal
                    // Data starts after the NAL Unit Type byte and the FU Header byte
                    fragmented_nal.Write(rtp_payloads[payload_index].Value, 2, rtp_payloads[payload_index].Value.Length - 2);
                }

                if (fu_header_s == 0 && fu_header_e == 1)
                {
                    // End part of Fragment
                    // Append this payload to the fragmented_nal
                    // Data starts after the NAL Unit Type byte and the FU Header byte
                    fragmented_nal.Write(rtp_payloads[payload_index].Value, 2, rtp_payloads[payload_index].Value.Length - 2);

                    var fragmeted_nal_array = fragmented_nal.ToArray();
                    var reconstructed_nal_type = (byte)((fragmeted_nal_array[0] >> 0) & 0x1F);

                    //Check if is Key Frame
                    CheckKeyFrame(reconstructed_nal_type, ref isKeyFrameNullable);

                    // Add the NAL to the array of NAL units
                    nal_units.Add(fragmeted_nal_array);
                    fragmented_nal.SetLength(0);
                }
            }

            else if (nal_header_type == 29)
            {
                fu_b++;
            }
        }

        isKeyFrame = isKeyFrameNullable is { } ? isKeyFrameNullable.Value : false;

        // Output all the NALs that form one RTP Frame (one frame of video)
        return nal_units;
    }

    protected void CheckKeyFrame(int nal_type, ref bool? isKeyFrame)
    {
        if (isKeyFrame is null)
        {
            isKeyFrame = nal_type is SPS or PPS ? new bool?(true) :
                (nal_type == NON_IDR_SLICE ? new bool?(false) : null);
        }
        else
        {
            isKeyFrame = nal_type is SPS or PPS ?
                (isKeyFrame.Value ? isKeyFrame : new bool?(false)) :
                (nal_type == NON_IDR_SLICE ? new bool?(false) : isKeyFrame);
        }
    }
}
