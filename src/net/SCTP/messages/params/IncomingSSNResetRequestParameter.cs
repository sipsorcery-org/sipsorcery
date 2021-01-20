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
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
*
* @author tim
*/
namespace SIPSorcery.Net.Sctp
{
    public class IncomingSSNResetRequestParameter : KnownParam
    {
        /*
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Parameter Type = 14       |  Parameter Length = 8 + 2 * N |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |          Re-configuration Request Sequence Number             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |  Stream Number 1 (optional)   |    Stream Number 2 (optional) |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 /                            ......                             /
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |  Stream Number N-1 (optional) |    Stream Number N (optional) |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */

        private static ILogger logger = Log.Logger;

        uint reqSeqNo;
        int[] streams;

        public IncomingSSNResetRequestParameter(int t, string n) : base(t, n) { }

        public IncomingSSNResetRequestParameter() : base(14, "IncomingSSNResetRequestParameter") { }

        public IncomingSSNResetRequestParameter(uint reqNo) : this()
        {
            this.reqSeqNo = reqNo;
        }

        public override void readBody(ByteBuffer body, int blen)
        {
            if (blen < 4)
            {
                logger.LogError("Huh ? No body to this " + this.getName());
                return;
            }
            reqSeqNo = body.GetUInt();
            if (blen > 4)
            {
                this.streams = new int[(blen - 4) / 2];
                for (int i = 0; i < streams.Length; i++)
                {
                    streams[i] = body.GetUShort();
                }
            }
            else
            {
                this.streams = new int[0];
                logger.LogWarning("No inbound stream mentioned");
            }
        }

        public override void writeBody(ByteBuffer body)
        {
            body.Put(reqSeqNo);
            if (streams != null)
            {
                for (int i = 0; i < streams.Length; i++)
                {
                    body.Put((ushort)streams[i]);
                }
            }
        }

        public override string ToString()
        {
            StringBuilder ret = new StringBuilder();
            ret.Append(this.GetType().Name).Append(" ");
            ret.Append("seq:" + this.reqSeqNo);
            if (streams != null)
            {
                ret.Append("streams {");
                foreach (int s in streams)
                {
                    ret.Append("" + s);
                }
                ret.Append("}");
            }
            else
            {
                ret.Append("no streams");
            }
            return ret.ToString();
        }

        public bool sameAs(IncomingSSNResetRequestParameter other)
        {
            return this.reqSeqNo == other.reqSeqNo;
        }

        public int[] getStreams()
        {
            return streams;
        }

        public uint getReqNo()
        {
            return this.reqSeqNo;
        }
        public void setStreams(int[] ss)
        {
            this.streams = ss;
        }
    }
}
