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
using System.Text;
using SCTP4CS.Utils;
/**
*
* @author Westhawk Ltd<thp@westhawk.co.uk>
*/
namespace SIPSorcery.Net.Sctp
{
    public class SackChunk : Chunk
    {
        /**
		 * @param cumuTSNAck the _cumuTSNAck to set
		 */
        public void setCumuTSNAck(uint cumuTSNAck)
        {
            cumulativeTSNAck = cumuTSNAck;
        }

        /*
    
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |   Type = 3    |Chunk  Flags   |      Chunk Length             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                      Cumulative TSN Ack                       |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |          Advertised Receiver Window Credit (a_rwnd)           |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 | Number of Gap Ack Blocks = N  |  Number of Duplicate TSNs = X |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |  Gap Ack Block #1 Start       |   Gap Ack Block #1 End        |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 /                                                               /
		 \                              ...                              \
		 /                                                               /
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |   Gap Ack Block #N Start      |  Gap Ack Block #N End         |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                       Duplicate TSN 1                         |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 /                                                               /
		 \                              ...                              \
		 /                                                               /
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                       Duplicate TSN X                         |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */



        public class GapBlock
        {
            public ushort start;
            public ushort end;
            public GapBlock()
            {

            }
            public GapBlock(ByteBuffer b)
            {
                start = (ushort)b.GetUShort();
                end = (ushort)b.GetUShort();
            }
            public GapBlock(ushort start)
            {
                this.start = start;
            }
            public void setEnd(ushort end)
            {
                this.end = end;
            }

            public void put(ByteBuffer b)
            {
                b.Put(start);
                b.Put(end);
            }
        }

        public GapBlock[] gapAckBlocks;
        public uint[] duplicateTSN;
        public uint cumulativeTSNAck;
        public uint advertisedReceiverWindowCredit;

        public SackChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            cumulativeTSNAck = _body.GetUInt();
            advertisedReceiverWindowCredit = _body.GetUInt();
            int ngaps = _body.GetUShort();
            int nDTSNs = _body.GetUShort();
            gapAckBlocks = new GapBlock[ngaps];
            duplicateTSN = new uint[nDTSNs];
            for (int i = 0; i < ngaps; i++)
            {
                gapAckBlocks[i] = new GapBlock(_body);
            }
            for (int i = 0; i < nDTSNs; i++)
            {
                duplicateTSN[i] = _body.GetUInt();
            }
        }

        public uint[] getDupTSNs()
        {
            return duplicateTSN;
        }
        public SackChunk() : base(ChunkType.SACK)
        {
            gapAckBlocks = new GapBlock[0];
            duplicateTSN = new uint[0];
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put(cumulativeTSNAck);
            ret.Put(advertisedReceiverWindowCredit);
            ret.Put((ushort)gapAckBlocks.Length);
            ret.Put((ushort)duplicateTSN.Length);
            for (int i = 0; i < gapAckBlocks.Length; i++)
            {
                gapAckBlocks[i].put(ret);
            }
            for (int i = 0; i < duplicateTSN.Length; i++)
            {
                ret.Put(duplicateTSN[i]);
            }
        }
        public override string ToString()
        {
            StringBuilder ret = new StringBuilder("SACK cumuTSNAck=" + cumulativeTSNAck)
                    .Append(" _arWin=" + advertisedReceiverWindowCredit)
                    .Append(" _gaps=" + gapAckBlocks.Length + " [");
            foreach (GapBlock g in gapAckBlocks)
            {
                ret.Append("\n\t{" + (int)g.start + "," + (int)g.end + "}");
            }
            ret.Append("]\n _duplicateTSNs=" + duplicateTSN.Length);
            foreach (long t in duplicateTSN)
            {
                ret.Append("\n\t" + t);
            }
            return ret.ToString();
        }
    }
}
