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
    public class OutgoingSSNResetRequestParameter : KnownParam
    {
        /*
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Parameter Type = 13       | Parameter Length = 16 + 2 * N |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |           Re-configuration Request Sequence Number            |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |           Re-configuration Response Sequence Number           |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                Sender's Last Assigned TSN                     |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |  Stream Number 1 (optional)   |    Stream Number 2 (optional) |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 /                            ......                             /
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |  Stream Number N-1 (optional) |    Stream Number N (optional) |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */
        uint reqSeqNo;
        uint respSeqNo;
        uint lastTsn;
        int[] streams;

        public OutgoingSSNResetRequestParameter(int t, string n) : base(t, n) { }
        public OutgoingSSNResetRequestParameter(uint t) : base((int)t, "OutgoingSSNResetRequestParameter") { }
        public OutgoingSSNResetRequestParameter() : base(13, "OutgoingSSNResetRequestParameter") { }
        public OutgoingSSNResetRequestParameter(uint reqNo, uint respNo, uint lastTsn) : this()
        {
            this.respSeqNo = respNo;
            this.lastTsn = lastTsn;
            this.reqSeqNo = reqNo;
        }

        public uint getRespSeqNo()
        {
            return respSeqNo;
        }

        public uint getReqSeqNo()
        {
            return reqSeqNo;
        }

        public override void readBody(ByteBuffer body, int blen)
        {
            reqSeqNo = body.GetUInt();
            respSeqNo = body.GetUInt();
            lastTsn = body.GetUInt();
            streams = new int[(blen - 12) / 2];
            for (int i = 0; i < streams.Length; i++)
            {
                streams[i] = body.GetUShort();
            }
        }

        public override void writeBody(ByteBuffer body)
        {
            body.Put(reqSeqNo);
            body.Put(respSeqNo);
            body.Put(lastTsn);
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
            ret.Append("reqseq:").Append(this.reqSeqNo).Append(" ");
            ret.Append("respseq:").Append(this.respSeqNo).Append(" ");
            ret.Append("latsTSN:").Append(this.lastTsn).Append(" ");

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

        public uint getLastAssignedTSN()
        {
            return lastTsn;
        }

        public int[] getStreams()
        {
            return streams;
        }
        public void setStreams(int[] ss)
        {
            this.streams = ss;
        }

        public bool sameAs(OutgoingSSNResetRequestParameter other)
        {
            return this.reqSeqNo == other.reqSeqNo;
        }
    }
}
