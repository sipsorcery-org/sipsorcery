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

using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */

/*
      0                   1                   2                   3
        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       |   Type = 6    |Reserved     |T|           Length              |
       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       \                                                               \
       /                   zero or more Error Causes                   /
       \                                                               \
       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

 
 */
namespace SIPSorcery.Net.Sctp
{
    public class AbortChunk : Chunk
    {

        private static ILogger logger = Log.Logger;

        public AbortChunk() : base(ChunkType.ABORT) { }

        public AbortChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            if (_body.remaining() >= 4)
            {
                logger.LogDebug("Abort" + this.ToString());
                while (_body.hasRemaining())
                {
                    VariableParam v = readErrorParam();
                    _varList.Add(v);
                }
            }
        }

        protected override void putFixedParams(ByteBuffer ret) { }
    }
}
