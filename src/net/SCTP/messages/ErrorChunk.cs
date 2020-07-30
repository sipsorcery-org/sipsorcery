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
 3.3.10.  Operation Error (ERROR) (9)

 An endpoint sends this chunk to its peer endpoint to notify it of
 certain error conditions.  It contains one or more error causes.  An
 Operation Error is not considered fatal in and of itself, but may be
 used with an ABORT chunk to report a fatal condition.  It has the
 following parameters:

 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |   Type = 9    | Chunk  Flags  |           Length              |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 \                                                               \
 /                    one or more Error Causes                   /
 \                                                               \
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

 Chunk Flags: 8 bits

 Set to 0 on transmit and ignored on receipt.

 Length: 16 bits (unsigned integer)

 Set to the size of the chunk in bytes, including the chunk header
 and all the Error Cause fields present.
 */
namespace SIPSorcery.Net.Sctp
{
    public class ErrorChunk : Chunk
    {
        private static ILogger logger = Log.Logger;

        public ErrorChunk() : base(ChunkType.ERROR) { }

        public ErrorChunk(KnownError e) : this()
        {
            _varList.Add(e);
        }

        public ErrorChunk(KnownError[] el) : this()
        {
            foreach (KnownError e in el)
            {
                _varList.Add(e);
            }
        }

        public ErrorChunk(ChunkType type, byte flags, int length, ByteBuffer pkt) : base(type, flags, length, pkt)
        {
            if (_body.remaining() >= 4)
            {
                //logger.LogDebug("Error" + this.ToString());
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
