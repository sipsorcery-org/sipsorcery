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
    public class RequestedHMACAlgorithmParameter : KnownParam
    {
        /*

		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Parameter Type = 0x8004   |       Parameter Length        |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |          HMAC Identifier 1    |      HMAC Identifier 2        |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 /                                                               /
		 \                              ...                              \
		 /                                                               /
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |        HMAC Identifier n      |           Padding             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */

        int[] hmacs;
        /*
		 +-----------------+--------------------------+
		 | HMAC Identifier | Message Digest Algorithm |
		 +-----------------+--------------------------+
		 | 0               | Reserved                 |
		 | 1               | SHA-1 defined in [8]     |
		 | 2               | Reserved                 |
		 | 3               | SHA-256 defined in [8]   |
		 +-----------------+--------------------------+
		 */

        public RequestedHMACAlgorithmParameter(int t, string n) : base(t, n) { }

        public override void readBody(ByteBuffer body, int blen)
        {
            hmacs = new int[blen / 2];
            for (int i = 0; i < hmacs.Length; i++)
            {
                hmacs[i] = body.GetUShort();
            }
        }

        public override void writeBody(ByteBuffer body)
        {
            if (hmacs != null)
            {
                for (int i = 0; i < hmacs.Length; i++)
                {
                    body.Put((ushort)hmacs[i]);
                }
            }
        }

        public override string ToString()
        {
            string ret = " Hmacs are ";
            for (int i = 0; i < hmacs.Length; i++)
            {
                switch (hmacs[i])
                {
                    case 0:
                    case 2:
                        ret += " Reserved ";
                        break;
                    case 1:
                        ret += " SHA-1 ";
                        break;
                    case 3:
                        ret += " SHA-256 ";
                        break;
                }
            }
            return base.ToString() + ret;
        }

        public bool doesSha256()
        {
            bool ret = false;
            for (int i = 0; i < hmacs.Length; i++)
            {
                if (3 == hmacs[i])
                {
                    ret = true;
                }
            }
            return ret;
        }
    }
}
