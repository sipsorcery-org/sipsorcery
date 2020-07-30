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
		 * @return the cumuTSNAck
		 */
        public long getCumuTSNAck()
        {
            return _cumuTSNAck;
        }

        /**
		 * @param cumuTSNAck the _cumuTSNAck to set
		 */
        public void setCumuTSNAck(uint cumuTSNAck)
        {
            _cumuTSNAck = cumuTSNAck;
        }

        /**
		 * @return the _arWin
		 */
        public long getArWin()
        {
            return _arWin;
        }

        /**
		 * @param _arWin the _arWin to set
		 */
        public void setArWin(uint arWin)
        {
            _arWin = arWin;
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
            public ushort _start;
            public ushort _end;
            public GapBlock(ByteBuffer b)
            {
                _start = (ushort)b.GetUShort();
                _end = (ushort)b.GetUShort();
            }
            public GapBlock(ushort start)
            {
                _start = start;
            }
            public void setEnd(ushort end)
            {
                _end = end;
            }

            public void put(ByteBuffer b)
            {
                b.Put(_start);
                b.Put(_end);
            }
            public ushort getStart()
            {
                return _start;
            }
            public ushort getEnd()
            {
                return _end;
            }
        }

        GapBlock[] _gaps;
        uint[] _duplicateTSNs;
        private uint _cumuTSNAck;
        private uint _arWin;

        public SackChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            _cumuTSNAck = _body.GetUInt();
            _arWin = _body.GetUInt();
            int ngaps = _body.GetUShort();
            int nDTSNs = _body.GetUShort();
            _gaps = new GapBlock[ngaps];
            _duplicateTSNs = new uint[nDTSNs];
            for (int i = 0; i < ngaps; i++)
            {
                _gaps[i] = new GapBlock(_body);
            }
            for (int i = 0; i < nDTSNs; i++)
            {
                _duplicateTSNs[i] = _body.GetUInt();
            }
        }

        public GapBlock[] getGaps()
        {
            return _gaps;
        }
        public uint[] getDupTSNs()
        {
            return _duplicateTSNs;
        }
        public SackChunk() : base(ChunkType.SACK)
        {
            _gaps = new GapBlock[0];
            _duplicateTSNs = new uint[0];
        }

        public void setDuplicates(List<uint> dups)
        {
            _duplicateTSNs = new uint[dups.Count];
            int i = 0;
            foreach (uint du in dups)
            {
                _duplicateTSNs[i++] = du;
            }
        }

        public void setGaps(List<uint> seenTsns)
        {
            long cuTsn = _cumuTSNAck;
            List<GapBlock> gaplist = new List<GapBlock>();
            GapBlock currentGap = null;
            ushort prevoff = (ushort)0;
            foreach (long t in seenTsns)
            {
                ushort offs = (ushort)(t - cuTsn);
                if (currentGap == null)
                {
                    currentGap = new GapBlock(offs);
                    currentGap.setEnd(offs);
                    gaplist.Add(currentGap);
                }
                else
                {
                    if (offs == prevoff + 1)
                    {
                        currentGap.setEnd(offs);
                    }
                    else
                    {
                        currentGap = new GapBlock(offs);
                        currentGap.setEnd(offs);
                        gaplist.Add(currentGap);
                    }
                }
                prevoff = offs;
            }
            _gaps = new GapBlock[gaplist.Count];
            int i = 0;
            foreach (GapBlock g in gaplist)
            {
                _gaps[i++] = g;
            }
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put(_cumuTSNAck);
            ret.Put(_arWin);
            ret.Put((ushort)_gaps.Length);
            ret.Put((ushort)_duplicateTSNs.Length);
            for (int i = 0; i < _gaps.Length; i++)
            {
                _gaps[i].put(ret);
            }
            for (int i = 0; i < _duplicateTSNs.Length; i++)
            {
                ret.Put(_duplicateTSNs[i]);
            }
        }
        public override string ToString()
        {
            StringBuilder ret = new StringBuilder("SACK cumuTSNAck=" + _cumuTSNAck)
                    .Append(" _arWin=" + _arWin)
                    .Append(" _gaps=" + _gaps.Length + " [");
            foreach (GapBlock g in _gaps)
            {
                ret.Append("\n\t{" + (int)g._start + "," + (int)g._end + "}");
            }
            ret.Append("]\n _duplicateTSNs=" + _duplicateTSNs.Length);
            foreach (long t in _duplicateTSNs)
            {
                ret.Append("\n\t" + t);
            }
            return ret.ToString();
        }
    }
}
