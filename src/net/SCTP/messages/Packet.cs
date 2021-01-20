/*
 * Copyright 2017 pi.pe gmbh .
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */
// Modified by Andrés Leone Gámez

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
*
* @author Westhawk Ltd<thp@westhawk.co.uk>
*/
namespace SIPSorcery.Net.Sctp
{
    public class Packet
    {
        /*
		 SCTP Common Header Format

		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Source Port Number        |     Destination Port Number   |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                      Verification Tag                         |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                           Checksum                            |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */

        private static ILogger logger = Log.Logger;

        static int MTU = 1500;
        ushort _srcPort;
        ushort _destPort;
        uint _verTag;
        uint _chksum;
        List<Chunk> _chunks;
        private static int SUMOFFSET = 8;

        /**
		 * Constructor used to parse an incoming packet
		 *
		 * @param pkt
		 */
        public Packet(ByteBuffer pkt)
        {
            if (pkt.Length < 12)
            {
                throw new SctpPacketFormatException("SCTP packet too short expected 12 bytes, got " + pkt.Length);
            }
            checkChecksum(pkt); // if this isn't ok, then we dump the packet silently - by throwing an exception.

            _srcPort = pkt.GetUShort();
            _destPort = pkt.GetUShort();
            _verTag = pkt.GetUInt();
            _chksum = pkt.GetUInt();
            _chunks = mkChunks(pkt);

            pkt.Position = 0;
        }

        public Packet(int sp, int dp, uint vertag)
        {
            _srcPort = (ushort)sp;
            _destPort = (ushort)dp;
            _verTag = vertag;
            _chunks = new List<Chunk>();
        }

        public ByteBuffer getByteBuffer()
        {
            ByteBuffer ret = new ByteBuffer(new byte[MTU]);
            ret.Put(_srcPort);
            ret.Put(_destPort);
            ret.Put(_verTag);
            ret.Put(_chksum);
            int pad = 0;
            foreach (Chunk c in _chunks)
            {
                ByteBuffer cs = ret.slice();            // create a zero offset buffer to play in
                c.write(cs); // ask the chunk to write itself into there.
                pad = cs.Position % 4;
                pad = (pad != 0) ? 4 - pad : 0;
                //logger.LogDebug("padding by " + pad);
                ret.Position += pad + cs.Position;// move us along.
            }
            /*Log.logger.verb("un padding by " + pad);
			ret.position(ret.position() - pad);*/
            ret = ret.flip();
            setChecksum(ret);
            return ret;
        }

        public int getSrcPort()
        {
            return _srcPort;
        }

        public int getDestPort()
        {
            return _destPort;
        }

        public uint getVerTag()
        {
            return _verTag;
        }

        public uint getChksum()
        {
            return _chksum;
        }

        public static string getHex(ByteBuffer buf, char? separator = null)
        {
            return getHex(buf.Data, buf.offset, buf.Length, separator);
        }

        public static string getHex(byte[] buf, char? separator = null)
        {
            return buf.HexStr(separator);
        }

        public static string getHex(byte[] buf, int off, int len, char? separator = null)
        {
            return buf.Skip(off).Take(len).ToArray().HexStr(separator);
        }

        private List<Chunk> mkChunks(ByteBuffer pkt)
        {
            List<Chunk> ret = new List<Chunk>();
            Chunk next = null;
            while (null != (next = Chunk.mkChunk(pkt)))
            {
                ret.Add(next);
                //logger.LogDebug("saw chunk: " + next.typeLookup());
            }
            return ret;
        }

        public List<Chunk> getChunkList()
        {
            return _chunks;
        }

        public void Add(Chunk c)
        {
            _chunks.Add(c);
        }

        /*
		 When an SCTP packet is received, the receiver MUST first check the
		 CRC32c checksum as follows:

		 1)  Store the received CRC32c checksum value aside.

		 2)  Replace the 32 bits of the checksum field in the received SCTP
		 packet with all '0's and calculate a CRC32c checksum value of the
		 whole received packet.

		 3)  Verify that the calculated CRC32c checksum is the same as the
		 received CRC32c checksum.  If it is not, the receiver MUST treat
		 the packet as an invalid SCTP packet.

		 The default procedure for handling invalid SCTP packets is to
		 silently discard them.

		 Any hardware implementation SHOULD be done in a way that is
		 verifiable by the software.
		 */
        void setChecksum(ByteBuffer pkt)
        {
            pkt.Put(SUMOFFSET, 0);
            var UUint = new FastBit.Uint(SCTP4CS.Utils.Crc32.CRC32C.Calculate(pkt.Data, pkt.offset, pkt.Limit));
            uint flip = new FastBit.Uint(UUint.b3, UUint.b2, UUint.b1, UUint.b0).Auint;
            pkt.Put(SUMOFFSET, flip);
        }

        protected virtual void checkChecksum(ByteBuffer pkt)
        {
            uint farsum = pkt.GetUInt(SUMOFFSET);
            setChecksum(pkt);
            uint calc = pkt.GetUInt(SUMOFFSET);
            if (calc != farsum)
            {
                logger.LogError("Checksums don't match " + calc.ToString("X4") + " vs " + farsum.ToString("X4"));
                byte[] p = pkt.Data;
                logger.LogError("for packet " + getHex(p));
                throw new ChecksumException();
            }
        }
        /*
		 8.5.  Verification Tag

		 The Verification Tag rules defined in this section apply when sending
		 or receiving SCTP packets that do not contain an INIT, SHUTDOWN
		 COMPLETE, COOKIE ECHO (see Section 5.1), ABORT, or SHUTDOWN ACK
		 chunk.  The rules for sending and receiving SCTP packets containing
		 one of these chunk types are discussed separately in Section 8.5.1.

		 When sending an SCTP packet, the endpoint MUST fill in the
		 Verification Tag field of the outbound packet with the tag value in
		 the Initiate Tag parameter of the INIT or INIT ACK received from its
		 peer.

		 When receiving an SCTP packet, the endpoint MUST ensure that the
		 value in the Verification Tag field of the received SCTP packet
		 matches its own tag.  If the received Verification Tag value does not
		 match the receiver's own tag value, the receiver shall silently
		 discard the packet and shall not process it any further except for
		 those cases listed in Section 8.5.1 below.

		 8.5.1.  Exceptions in Verification Tag Rules

		 A) Rules for packet carrying INIT:

		 -   The sender MUST set the Verification Tag of the packet to 0.

		 -   When an endpoint receives an SCTP packet with the Verification
		 Tag set to 0, it should verify that the packet contains only an
		 INIT chunk.  Otherwise, the receiver MUST silently discard the
		 packet.

		 B) Rules for packet carrying ABORT:

		 -   The endpoint MUST always fill in the Verification Tag field of
		 the outbound packet with the destination endpoint's tag value, if
		 it is known.

		 -   If the ABORT is sent in response to an OOTB packet, the endpoint
		 MUST follow the procedure described in Section 8.4.



		 Stewart                     Standards Track                   [Page 105]

		 RFC 4960          Stream Control Transmission Protocol    September 2007


		 -   The receiver of an ABORT MUST accept the packet if the
		 Verification Tag field of the packet matches its own tag and the
		 T bit is not set OR if it is set to its peer's tag and the T bit
		 is set in the Chunk Flags.  Otherwise, the receiver MUST silently
		 discard the packet and take no further action.

		 C) Rules for packet carrying SHUTDOWN COMPLETE:

		 -   When sending a SHUTDOWN COMPLETE, if the receiver of the SHUTDOWN
		 ACK has a TCB, then the destination endpoint's tag MUST be used,
		 and the T bit MUST NOT be set.  Only where no TCB exists should
		 the sender use the Verification Tag from the SHUTDOWN ACK, and
		 MUST set the T bit.

		 -   The receiver of a SHUTDOWN COMPLETE shall accept the packet if
		 the Verification Tag field of the packet matches its own tag and
		 the T bit is not set OR if it is set to its peer's tag and the T
		 bit is set in the Chunk Flags.  Otherwise, the receiver MUST
		 silently discard the packet and take no further action.  An
		 endpoint MUST ignore the SHUTDOWN COMPLETE if it is not in the
		 SHUTDOWN-ACK-SENT state.

		 D) Rules for packet carrying a COOKIE ECHO

		 -   When sending a COOKIE ECHO, the endpoint MUST use the value of
		 the Initiate Tag received in the INIT ACK.

		 -   The receiver of a COOKIE ECHO follows the procedures in Section
		 5.

		 E) Rules for packet carrying a SHUTDOWN ACK

		 -   If the receiver is in COOKIE-ECHOED or COOKIE-WAIT state the
		 procedures in Section 8.4 SHOULD be followed; in other words, it
		 should be treated as an Out Of The Blue packet.

		 */

        //private void reflectedVerify(int cno, Association ass)
        //{
        //    Chunk chunk = _chunks[cno];
        //    bool t = ((Chunk.TBIT & chunk._flags) > 0);
        //    int cverTag = t ? ass.getPeerVerTag() : ass.getMyVerTag();
        //    if (cverTag != _verTag)
        //    {
        //        throw new InvalidSCTPPacketException($"VerTag on an {(ChunkType)chunk._type} doesn't match " + (t ? "their " : "our ") + " vertag " + _verTag + " != " + cverTag);
        //    }
        //}

        //public void validate(Association ass)
        //{
        //    // step 1 - deduce the validation rules:
        //    // validation depends on the types of chunk in the list.
        //    if ((_chunks != null) && (_chunks.Count > 0))
        //    {
        //        int init = findChunk(ChunkType.INIT);
        //        if (init >= 0)
        //        {
        //            if (init != 0)
        //            {
        //                throw new InvalidSCTPPacketException("Init must be only chunk in a packet");
        //            }
        //            if (_verTag != 0)
        //            {
        //                throw new InvalidSCTPPacketException("VerTag on an init packet expected to be Zeros");
        //            }
        //        }
        //        else
        //        {
        //            int abo = findChunk(ChunkType.ABORT);
        //            if (abo >= 0)
        //            {
        //                // we have an abort
        //                _chunks = _chunks.GetRange(0, abo + 1); // remove any subsequent chunks.
        //                reflectedVerify(abo, ass);
        //            }
        //            else
        //            {
        //                int sdc = findChunk(ChunkType.SHUTDOWN_COMPLETE);
        //                if (sdc >= 0)
        //                {
        //                    if (sdc == 0)
        //                    {
        //                        reflectedVerify(sdc, ass);
        //                    }
        //                    else
        //                    {
        //                        throw new InvalidSCTPPacketException("SHUTDOWN_COMPLETE must be only chunk in a packet");
        //                    }
        //                }
        //                else
        //                {
        //                    // somewhat hidden here - but this is the normal case - not init abort or shutdown complete 
        //                    if (_verTag != ass.getMyVerTag())
        //                    {
        //                        throw new InvalidSCTPPacketException("VerTag on plain packet expected to match ours " + _verTag + " != " + ass.getMyVerTag());
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        //private int findChunk(ChunkType bty)
        //{
        //    int ret = 0;
        //    foreach (Chunk c in _chunks)
        //    {
        //        if (c._type == bty)
        //        {
        //            break;
        //        }
        //        else
        //        {
        //            ret++;
        //        }
        //    }
        //    return (ret < _chunks.Count) ? ret : -1;
        //}
    }
}
