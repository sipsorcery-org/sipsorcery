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


using System.Text;
using SCTP4CS.Utils;

/**
 *
 * @author tim
 */
namespace SIPSorcery.Net.Sctp
{
    public class HostNameAddress : KnownParam
    {
        string hostname;

        public HostNameAddress(int t, string n) : base(t, n) { }

        public override void readBody(ByteBuffer body, int blen)
        {
            byte[] data = new byte[blen];
            body.GetBytes(data, data.Length);
            int off = blen - 1;
            // trim any 0 bytes off the end.
            while ((off > 0) && (data[off--] == 0))
            {
                blen--;
            }
            hostname = Encoding.ASCII.GetString(data, 0, blen);
        }
        public override void writeBody(ByteBuffer body)
        {
            // gonz up a C style string.
            byte[] b = Encoding.ASCII.GetBytes(hostname);
            _data = new byte[b.Length + 1];
            b.CopyTo(_data, 0);
            body.Put(_data);
        }
    }
}
