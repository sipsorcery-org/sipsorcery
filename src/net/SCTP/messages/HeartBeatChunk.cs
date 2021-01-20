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

using SCTP4CS.Utils;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */
namespace SIPSorcery.Net.Sctp
{
    public class HeartBeatChunk : Chunk
    {
        public HeartBeatChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            if (_body.remaining() >= 4)
            {
                while (_body.hasRemaining())
                {
                    VariableParam v = readVariable();
                    _varList.Add(v);
                }
            }
        }

        public override void validate()
        {
            VariableParam hbd;
            if ((_varList == null) || (_varList.Count != 1))
            {
                throw new SctpPacketFormatException("No (or too much content in this heartbeat packet");
            }
            hbd = _varList[0];
            if (!typeof(HeartbeatInfo).IsAssignableFrom(hbd.GetType()))
            {
                throw new SctpPacketFormatException("Expected a heartbeatinfo in this packet");
            }
        }

        public Chunk[] mkReply()
        {
            Chunk[] rep = new Chunk[1];
            HeartBeatAckChunk dub = new HeartBeatAckChunk();
            dub._varList = this._varList;
            rep[0] = dub;
            return rep;
        }

        protected override void putFixedParams(ByteBuffer ret) { }
    }
}
