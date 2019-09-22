 //-----------------------------------------------------------------------------
// Filename: RTPFrame.cs
//
// Description: Represents a series of RTP packets that combine together to make a single media frame.
//
// History:
// 29 Jan 2014	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.Net
{
    public enum FrameTypesEnum
    {
        Audio = 0,
        JPEG = 1,
        H264 = 2,
        VP8 = 3
    }

    public class RTPFrame
    {
        public uint Timestamp;
        public bool HasMarker;
        public bool HasBeenProcessed;
        //public int FrameHeaderLength = 0;   // Some media types, such as VP8 video, have a header at the start of each RTP data payload. It needs to be stripped.
        public FrameTypesEnum FrameType;

        private List<RTPPacket> _packets = new List<RTPPacket>();

        public List<RTPPacket> FramePackets
        {
            get { return _packets; }
        }

        public uint StartSequenceNumber
        {
            get
            {
                var startPacket = _packets.OrderBy(x => x.Header.SequenceNumber).FirstOrDefault();

                if (startPacket != null)
                {
                    return startPacket.Header.SequenceNumber;
                }
                else
                {
                    return 0;
                }
            }
        }

        public uint EndSequenceNumber
        {
            get
            {
                var finalPacket = _packets.OrderByDescending(x => x.Header.SequenceNumber).FirstOrDefault();

                if (finalPacket != null)
                {
                    return finalPacket.Header.SequenceNumber;
                }
                else
                {
                    return 0;
                }
            }
        }

        public RTPFrame()
        { }

        /// <summary>
        /// Audio frames are generally contained within a single RTP packet. This method is a shortcut
        /// to construct a frame from a single RTP packet.
        /// </summary>
        public static RTPFrame MakeSinglePacketFrame(RTPPacket rtpPacket)
        {
            RTPFrame frame = new RTPFrame();
            frame.AddRTPPacket(rtpPacket);
            frame.Timestamp = rtpPacket.Header.Timestamp;

            return frame;
        }

        public void AddRTPPacket(RTPPacket rtpPacket)
        {
            _packets.Add(rtpPacket);

            //if (HasMarker && FramePayload == null)
            //{
            //    FramePayload = IsComplete(_packets, payloadHeaderLength);
            //}
        }

        public bool IsComplete()
        {
            if(!HasMarker)
            {
                return false;
            }

            // The frame has the marker bit set. Check that there are no missing sequence numbers.
            uint previousSeqNum = 0;

            foreach (var rtpPacket in _packets.OrderBy(x => x.Header.SequenceNumber))
            {
                if (previousSeqNum == 0)
                {
                    previousSeqNum = rtpPacket.Header.SequenceNumber;
                    //payload.AddRange(rtpPacket.Payload.Skip(payloadHeaderLength));
                    //payloadPackets.Add(rtpPacket);
                }
                else if (previousSeqNum != rtpPacket.Header.SequenceNumber - 1)
                {
                    // Missing packet.
                    return false;
                }
                else
                {
                    previousSeqNum = rtpPacket.Header.SequenceNumber;
                    //payload.AddRange(rtpPacket.Payload.Skip(payloadHeaderLength));
                    //payloadPackets.Add(rtpPacket);
                }
            }

            //return payload.ToArray();

            //return Mjpeg.ProcessMjpegFrame(payloadPackets);
            return true;
        }

        public byte[] GetFramePayload()
        {
            List<byte> payload = new List<byte>();

            foreach (var rtpPacket in _packets.OrderBy(x => x.Header.SequenceNumber))
            {
                if (FrameType == FrameTypesEnum.VP8)
                {
                    var vp8Header = RTPVP8Header.GetVP8Header(rtpPacket.Payload);
                    payload.AddRange(rtpPacket.Payload.Skip(vp8Header.PayloadDescriptorLength));
                }
                else
                {
                    payload.AddRange(rtpPacket.Payload);
                }
            }

            return payload.ToArray();
        }
    }
}
