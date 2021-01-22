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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;
using SIPSorcery.Sys;

/**
 * An association who's retries etc are managed with plain old threads.
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */
namespace SIPSorcery.Net.Sctp
{
    public class LimitedConcurrentQueue<ELEMENT> : ConcurrentQueue<ELEMENT>
    {
        public readonly int Limit;

        public LimitedConcurrentQueue(int limit)
        {
            Limit = limit;
        }

        public new void Enqueue(ELEMENT element)
        {
            base.Enqueue(element);
            if (Count > Limit)
            {
                TryDequeue(out ELEMENT discard);
            }
        }
    }

    public class ThreadedAssociation : Association
    {
        static int MAXBLOCKS = 1000; // some number....

        public ThreadedAssociation(DatagramTransport transport, AssociationListener al, bool isClient, int srcPort, int destPort)
            : base(transport, al, isClient, srcPort, destPort)
        {
            _freeBlocks = new LimitedConcurrentQueue<DataChunk>(MAXBLOCKS);

            for (int i = 0; i < MAXBLOCKS; i++)
            {
                DataChunk dc = new DataChunk();
                _freeBlocks.Enqueue(dc);
            }
        }

        internal override void sendAndBlock(SCTPMessage m)
        {
            var chunks = new List<DataChunk>();
            DataChunk head = null;
            while (m.hasMoreData())
            {
                _freeBlocks.TryDequeue(out var dc);
                if (dc == null)
                {
                    dc = new DataChunk();
                }
                if (head == null)
                {
                    head = dc;
                }
                else
                {
                    dc.Head = head;
                }
                m.fill(dc, (int)maxPayloadSize);
                chunks.Add(dc);
            }
            sendPayloadData(chunks.ToArray());
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal override SCTPMessage makeMessage(byte[] bytes, BlockingSCTPStream s)
        {
            SCTPMessage m = null;
            if (base.canSend())
            {
                if (bytes.Length < maxMessageSize)
                {
                    m = new SCTPMessage(bytes, s);
                    lock (s)
                    {
                        var mseq = s.getNextMessageSeqOut();
                        s.setNextMessageSeqOut(mseq + 1);
                        m.setSeq(mseq);
                    }
                }
            }
            return m;
        }

        internal override SCTPMessage makeMessage(string bytes, BlockingSCTPStream s)
        {
            SCTPMessage m = null;
            if (base.canSend())
            {
                if (bytes.Length < this.maxMessageSize)
                {
                    m = new SCTPMessage(bytes, s);
                    lock (s)
                    {
                        var mseq = s.getNextMessageSeqOut();
                        s.setNextMessageSeqOut(mseq + 1);
                        m.setSeq(mseq);
                    }
                }
                else
                {
                    logger.LogWarning("Message too long " + bytes.Length + " > " + this.maxMessageSize);
                }
            }
            else
            {
                logger.LogWarning("Can't send a message right now");
            }
            return m;
        }        
    }
}
