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
        private LimitedConcurrentQueue<DataChunk> _freeBlocks;
        static int MAX_INIT_RETRANS = 8;

        private static ILogger logger = Log.Logger;



        /*



		 Stewart                     Standards Track                    [Page 95]

		 RFC 4960          Stream Control Transmission Protocol    September 2007


		 SCTP also requires one additional control variable,
		 partial_bytes_acked, which is used during congestion avoidance phase
		 to facilitate cwnd adjustment.
		 */
        private long _partial_bytes_acked;
        // AC: This variable was never being set. Removed for now.
        private bool _fastRecovery;
        /*
		 10.2.  Probing Method Using SCTP

		 In the Stream Control Transmission Protocol (SCTP) [RFC2960], the
		 application writes messages to SCTP, which divides the data into
		 smaller "chunks" suitable for transmission through the network.  Each
		 chunk is assigned a Transmission Sequence Number (TSN).  Once a TSN
		 has been transmitted, SCTP cannot change the chunk size.  SCTP multi-
		 path support normally requires SCTP to choose a chunk size such that
		 its messages to fit the smallest PMTU of all paths.  Although not
		 required, implementations may bundle multiple data chunks together to
		 make larger IP packets to send on paths with a larger PMTU.  Note
		 that SCTP must independently probe the PMTU on each path to the peer.

		 The RECOMMENDED method for generating probes is to add a chunk
		 consisting only of padding to an SCTP message.  The PAD chunk defined
		 in [RFC4820] SHOULD be attached to a minimum length HEARTBEAT (HB)
		 chunk to build a probe packet.  This method is fully compatible with
		 all current SCTP implementations.

		 SCTP MAY also probe with a method similar to TCP's described above,
		 using inline data.  Using such a method has the advantage that
		 successful probes have no additional overhead; however, failed probes
		 will require retransmission of data, which may impact flow
		 performance.

		 To do .....
		 */

        private Chunk[] _stashCookieEcho;
        private object _congestion = new Object();
        private bool _firstRTT = true;
        private double _srtt;
        private double _rttvar;

        /*
		 RTO.Initial - 3 seconds
		 RTO.Min - 1 second
		 RTO.Max - 60 seconds
		 Max.Burst - 4
		 RTO.Alpha - 1/8
		 RTO.Beta - 1/4
		 */
        private static double _rtoBeta = 0.2500;
        private static double _rtoAlpha = 0.1250;
        private static double _rtoMin = 1.0;
        private static double _rtoMax = 60.0;
        protected DateTime _lastSack = DateTime.Now;

        public ThreadedAssociation(DatagramTransport transport, AssociationListener al, bool isClient, int srcPort, int destPort)
            : base(transport, al, isClient, srcPort, destPort)
        {
            _freeBlocks = new LimitedConcurrentQueue<DataChunk>(MAXBLOCKS);

            // The higher the concurrencyLevel, the higher the theoretical number of operations
            // that could be performed concurrently on the ConcurrentDictionary.  However, global
            // operations like resizing the dictionary take longer as the concurrencyLevel rises.
            // For the purposes of this example, we'll compromise at numCores * 2.
            int numProcs = Environment.ProcessorCount;

            for (int i = 0; i < MAXBLOCKS; i++)
            {
                DataChunk dc = new DataChunk();
                _freeBlocks.Enqueue(dc);
            }
        }

        public override SCTPStream mkStream(int id)
        {
            //logger.LogDebug("Make new Blocking stream " + id);
            return new BlockingSCTPStream(this, id);
        }

        public override void enqueue(DataChunk d)
        {
            d.immediateSack = true;
            pendingQueue.push(d);
        }
        internal override void sendAndBlock(SCTPMessage m)
        {
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
                m.fill(dc);
                enqueue(dc);
            }
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
