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
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    public enum ReliabilityType : byte
    {
        // ReliabilityTypeReliable is used for reliable transmission
        Reliable = 0,
        // ReliabilityTypeRexmit is used for partial reliability by retransmission count
        TypeRexmit = 1,
        // ReliabilityTypeTimed is used for partial reliability by retransmission duration
        TypeTimed = 2
    }
    public abstract class SCTPStream
    {
        /* unfortunately a webRTC SCTP stream can change it's reliability rules etc post creation
		 so we can't encapsulate the streams into multiple implementations of the same interface/abstract
		 So what we do is put the bulk of the stream code here, then delegate the variant rules off to the
		 behave class - which has to be stateless since it can be swapped out - it is ugly 
		 - and I wonder if closures would do it better.
		 */

        private static ILogger logger = Log.Logger;

        private SCTPStreamBehaviour _behave;
        protected Association _ass;
        private int _sno;
        private string name;
        protected ReassemblyQueue reassemblyQueue;
        private bool unordered;
        public ReliabilityType reliabilityType;
        public uint reliabilityValue = 10;
        private SCTPStreamListener _sl;
        private int _nextMessageSeqIn;
        private ushort _nextMessageSeqOut;
        private bool closing;
        private State state = State.OPEN;
        private long bufferedAmount;
        private long bufferedAmountLow;
        public object rwLock = new object();
        public Action onBufferedAmountLow;
        private ManualResetEvent readNotifier = new ManualResetEvent(false);

        public Action OnOpen;

        public bool InboundIsOpen()
        {
            return ((state == State.OPEN) || (state == State.INBOUNDONLY));
        }

        public bool OutboundIsOpen()
        {
            return ((state == State.OPEN) || (state == State.OUTBOUNDONLY));
        }

        public Chunk immediateClose()
        {
            Chunk ret = null;
            try
            {
                ret = _ass.addToCloseList(this);
            }
            catch (Exception ex)
            {
                logger.LogError("Can't make immediate close for " + this._sno + " because " + ex.ToString());
            }
            return ret;
        }

        enum State
        {
            CLOSED, INBOUNDONLY, OUTBOUNDONLY, OPEN
        }

        public SCTPStream(Association a, int id)
        {
            _ass = a;
            _sno = id;
            reassemblyQueue = new ReassemblyQueue(); // sort bt tsn
            reassemblyQueue.si = id;
            _behave = new OrderedStreamBehaviour(); // default 'till we know different
        }

        public void setLabel(string l)
        {
            name = l;
        }

        public int getNum()
        {
            return _sno;
        }

        public override string ToString()
        {
            return $"Stream id {_sno}, label {name}, state {state}";// behaviour { _behave.GetType().Name}.";
        }

        /**
		 * note that behaviours must be stateless - since they can be swapped out
		 * when we finally get the 'open'
		 *
		 * @param behave
		 */
        internal void setBehave(SCTPStreamBehaviour behave)
        {
            _behave = behave;
        }

        // seqno management.
        /**
		 * annotate the outgoing chunk with stuff this stream knows.
		 *
		 * @param chunk
		 */
        public void outbound(DataChunk chunk)
        {
            chunk.setStreamId(_sno);
            // roll seqno here.... hopefully....
        }

        public void handleData(DataChunk pd)
        {
            if (reassemblyQueue.push(pd, out var blocks))
            {
                SCTPMessage m = new SCTPMessage(this, blocks);
                m.deliver(_sl);
            }
        }

        public string getLabel()
        {
            return name;
        }

        public void setSCTPStreamListener(SCTPStreamListener sl)
        {
            _sl = sl;
        }

        abstract public void send(string message);

        abstract public void send(byte[] message);

        abstract public uint getNumBytesInReassemblyQueue();

        public Association getAssociation()
        {
            return _ass;
        }

        public virtual void close()
        {
            //logger.LogDebug($"SCTP closing stream id {_sno}, label {_label}.");
            _ass.closeStream(this);
        }

        public void setNextMessageSeqIn(int expectedSeq)
        {
            _nextMessageSeqIn = (expectedSeq == 1 + ushort.MaxValue) ? 0 : expectedSeq;
        }

        public int getNextMessageSeqIn()
        {
            return _nextMessageSeqIn;
        }

        public void setNextMessageSeqOut(int expectedSeq)
        {
            _nextMessageSeqOut = (ushort)((expectedSeq == 1 + ushort.MaxValue) ? 0 : expectedSeq);
        }

        public ushort getNextMessageSeqOut()
        {
            return _nextMessageSeqOut;
        }

        abstract internal void deliverMessage(SCTPMessage message);

        public void reset()
        {
            logger.LogDebug("Resetting stream " + this._sno);
            if (this._sl != null)
            {
                _sl.close(this);
            }
        }

        public virtual void setClosing(bool b)
        {
            closing = b;
        }

        public virtual bool idle()
        {
            return true;
        }

        public void AddBytesToBuffer(uint nBytesAdded)
        {
            if (nBytesAdded <= 0)
            {
                return;
            }

            lock (rwLock)
            {
                bufferedAmount += nBytesAdded;
            }
        }


        internal void onBufferReleased(uint nBytesReleased)
        {
            if (nBytesReleased <= 0)
            {
                return;
            }

            Action f = null;
            lock (rwLock)
            {
                var fromAmount = bufferedAmount;


                if (bufferedAmount < nBytesReleased)
                {
                    bufferedAmount = 0;

                    logger.LogError($"{name} released buffer size {nBytesReleased} should be <= {bufferedAmount}");
                }
                else
                {
                    bufferedAmount -= nBytesReleased;
                }

                logger.LogTrace($"{name} bufferedAmount = {bufferedAmount}");


                if (onBufferedAmountLow != null && fromAmount > bufferedAmountLow && bufferedAmount <= bufferedAmountLow)
                {
                    f = onBufferedAmountLow;
                }
            }

            if (f != null)
            {
                f();
            }
        }
    }
}
