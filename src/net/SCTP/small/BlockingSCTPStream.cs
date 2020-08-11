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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */
namespace SIPSorcery.Net.Sctp
{
    public class BlockingSCTPStream : SCTPStream
    {
        private ConcurrentDictionary<int, SCTPMessage> undeliveredOutboundMessages = new ConcurrentDictionary<int, SCTPMessage>();
        private static ILogger logger = Log.Logger;
        private SemaphoreSlim semaphore = new SemaphoreSlim(1);

        public BlockingSCTPStream(Association a, int id) : base(a, id) { }

        public override void send(string message)
        {
            Association a = base.getAssociation();
            SCTPMessage m = a.makeMessage(message, this);
            if (m == null)
            {
                logger.LogError("SCTPMessage cannot be null, but it is");
                return;
            }
            a.sendAndBlock(m);
        }

        public override void send(byte[] message)
        {
            Association a = base.getAssociation();
            SCTPMessage m = a.makeMessage(message, this);
            if (m == null)
            {
                logger.LogError("SCTPMessage cannot be null, but it is");
                return;
            }
            undeliveredOutboundMessages.AddOrUpdate(m.getSeq(), m, (id, b) => m);
            a.sendAndBlock(m);
        }

        internal override void deliverMessage(SCTPMessage message)
        {
            message.run();
        }

        public override void delivered(DataChunk d)
        {
            int f = d.getFlags();
            if ((f & DataChunk.ENDFLAG) > 0)
            {
                int ssn = d.getSSeqNo();
                SCTPMessage st;
                if (undeliveredOutboundMessages.TryRemove(ssn, out st))
                {
                    st.acked();
                }
            }
        }

        public override bool idle()
        {
            return undeliveredOutboundMessages.Count == 0;
        }
    }
}
