using System;
using System.Collections.Generic;
using System.Text;
using SCTP4CS.Utils;

namespace SIPSorcery.Net.Sctp
{
    public class ForwardTsnChunk : Chunk
    {
        public uint newCumulativeTSN { get; set; }
        public ForwardTsnChunk() : this(ChunkType.FORWARDTSN)
        {

        }
        public ForwardTsnChunk(ChunkType type) : base(type)
        {
        }

        public ForwardTsnChunk(ChunkType type, byte flags, int length, ByteBuffer pkt) : base(type, flags, length, pkt)
        {
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put(newCumulativeTSN);
        }
    }
}
