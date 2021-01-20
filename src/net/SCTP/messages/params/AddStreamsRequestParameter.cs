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
 * @author tim
 */
namespace SIPSorcery.Net.Sctp
{
    public class AddStreamsRequestParameter : Unknown
    {
        public AddStreamsRequestParameter(int t, string n) : base(t, n) { }

        /*
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Parameter Type = 17       |      Parameter Length = 12    |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |          Re-configuration Request Sequence Number             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |      Number of new streams    |         Reserved              |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */

        uint reconfReqSeqNo;
        int numNewStreams;
        int reserved;

        public override void readBody(ByteBuffer body, int blen)
        {
            reconfReqSeqNo = body.GetUInt();
            numNewStreams = body.GetUShort();
            reserved = body.GetUShort();
        }

        public override void writeBody(ByteBuffer body)
        {
            body.Put((uint)reconfReqSeqNo);
            body.Put((ushort)numNewStreams);
            body.Put((ushort)reserved);
        }
    }
}
