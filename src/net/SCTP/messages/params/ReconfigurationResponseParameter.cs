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
/**
 *
 * @author tim
 */

using System.Text;
using SCTP4CS.Utils;

namespace SIPSorcery.Net.Sctp
{
    public class ReconfigurationResponseParameter : KnownParam
    {

        /*
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |     Parameter Type = 16       |      Parameter Length         |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |         Re-configuration Response Sequence Number             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                            Result                             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                   Sender's Next TSN (optional)                |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                  Receiver's Next TSN (optional)               |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 */
        uint seqNo;
        uint result;
        uint senderNextTSN;
        uint receiverNextTSN;
        bool hasTSNs;
        public const int SUCCESS_NOTHING_TO_DO = 0;
        public const int SUCCESS_PERFORMED = 1;
        public const int DENIED = 2;
        public const int ERROR_WRONG_SSN = 3;
        public const int ERROR_REQUEST_ALREADY_IN_PROGESS = 4;
        public const int ERROR_BAD_SEQUENCE_NUMBER = 5;
        public const int IN_PROGRESS = 6;
        static readonly string[] valuenames = new string[] {
            "Success - Nothing to do",
            "Success - Performed",
            "Denied",
            "Error - Wrong SSN",
            "Error - Request already in progress",
            "Error - Bad Sequence Number",
            "In progress"
        };

        /*
				 +--------+-------------------------------------+
				 | Result | Description                         |
				 +--------+-------------------------------------+
				 | 0      | Success - Nothing to do             |
				 | 1      | Success - Performed                 |
				 | 2      | Denied                              |
				 | 3      | Error - Wrong SSN                   |
				 | 4      | Error - Request already in progress |
				 | 5      | Error - Bad Sequence Number         |
				 | 6      | In progress                         |
				 +--------+-------------------------------------+
		 */
        public ReconfigurationResponseParameter(int t, string n) : base(t, n) { }
        public ReconfigurationResponseParameter(uint t) : base((int)t, "ReconfigurationResponseParameter") { }

        public ReconfigurationResponseParameter() : this(16, "ReconfigurationResponseParameter") { }

        public override void readBody(ByteBuffer body, int blen)
        {
            this.seqNo = body.GetUInt();
            this.result = body.GetUInt();
            if (blen == 16)
            {
                this.senderNextTSN = body.GetUInt();
                this.receiverNextTSN = body.GetUInt();
                hasTSNs = true;
            }
        }

        public override void writeBody(ByteBuffer body)
        {
            body.Put(seqNo);
            body.Put(result);
            if (hasTSNs)
            {
                body.Put(senderNextTSN);
                body.Put(receiverNextTSN);
            }
        }

        private string resultToName()
        {
            return ((result >= 0) && (result < valuenames.Length))
                    ? valuenames[(int)result] : "invalid value";
        }

        public override string ToString()
        {
            StringBuilder ret = new StringBuilder();
            ret.Append(this.GetType().Name).Append(" ");
            ret.Append("seqNo:").Append(this.seqNo).Append(" ");
            ret.Append("result:").Append(resultToName()).Append(" ");
            if (hasTSNs)
            {
                ret.Append("senderNextTSN:").Append(this.senderNextTSN).Append(" ");
                ret.Append("receiverNextTSN:").Append(this.receiverNextTSN).Append(" ");
            }
            return ret.ToString();
        }

        public void setResult(uint res)
        {
            result = res;
        }

        public void setSeq(uint reqSeqNo)
        {
            seqNo = reqSeqNo;
        }
    }
}
