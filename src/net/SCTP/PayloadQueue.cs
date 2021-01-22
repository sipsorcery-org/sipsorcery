using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Net.Sctp;
using System.Collections.Concurrent;
using SCTP4CS.Utils;

namespace SIPSorcery.Net.Sctp
{
    public class PayloadQueue
    {
        private object myLock = new object();
        Dictionary<uint, DataChunk> chunkMap = new Dictionary<uint, DataChunk>();
        uint[] sorted;
        List<uint> dupTSN = new List<uint>();
        protected uint nBytes;


        private uint[] updateSortedKeys()
        {
            lock (myLock)
            {
                if (sorted != null)
                {
                    return sorted;
                }

                var s = new List<uint>();
                s.AddRange(chunkMap.Keys);
                s.Sort();// (a, b) => Utils.sna32LT(a, b) ? 1 : 0);
                sorted = s.ToArray();
                return sorted;
            }
        }

        public bool canPush(DataChunk p, uint cumulativeTSN)
        {
            var tsn = p.getTsn();
            if (chunkMap.ContainsKey(tsn) || Utils.sna32LT(tsn, cumulativeTSN))
            {
                return false;
            }
            return true;
        }

        public void pushNoCheck(DataChunk p)
        {
            lock (myLock)
            {
                chunkMap.Add(p.getTsn(), p);
                nBytes += p.getDataSize();
                sorted = null;
            }
        }

        // push pushes a payload data. If the payload data is already in our queue or
        // older than our cumulativeTSN marker, it will be recored as duplications,
        // which can later be retrieved using popDuplicates.
        public bool push(DataChunk p, uint cumulativeTSN)
        {
            var tsn = p.getTsn();
            if (chunkMap.ContainsKey(tsn) || Utils.sna32LTE(tsn, cumulativeTSN))
            {
                if (dupTSN == null)
                {
                    dupTSN = new List<uint>();
                }
                // Found the packet, log in dups
                dupTSN.Add(tsn);
                return false;
            }
            pushNoCheck(p);
            return true;
        }

        // pop pops only if the oldest chunk's TSN matches the given TSN.
        public bool pop(uint tsn, out DataChunk c)
        {
            var s = updateSortedKeys();

            if (chunkMap.Count > 0 && tsn == s[0])
            {
                c = chunkMap[tsn];
                lock (myLock)
                {
                    chunkMap.Remove(tsn);
                    nBytes -= c.getDataSize();
                    sorted = null;
                }
                return true;
            }
            c = null;
            return false;
        }

        // get returns reference to chunkPayloadData with the given TSN value.
        public bool get(uint tsn, out DataChunk c)
        {
            return chunkMap.TryGetValue(tsn, out c);
        }

        // popDuplicates returns an array of TSN values that were found duplicate.
        public uint[] popDuplicates()
        {
            var dups = dupTSN.ToArray();
            this.dupTSN = new List<uint>();
            return dups;
        }

        public SackChunk.GapBlock[] getGapAckBlocks(uint cumulativeTSN)
        {
            if (chunkMap.Count == 0)
            {
                return new SackChunk.GapBlock[0];
            }

            var gapAckBlocks = new List<SackChunk.GapBlock>();
            var b = new SackChunk.GapBlock();
            var s = updateSortedKeys();
            for (int i = 0; i < s.Length; i++)
            {
                var tsn = s[i];
                if (i == 0)
                {
                    b.start = (ushort)(tsn - cumulativeTSN);
                    b.end = b.start;
                    continue;
                }
                var diff = (ushort)(tsn - cumulativeTSN);
                if (b.end + 1 == diff)
                {
                    b.end++;
                }
                else
                {
                    gapAckBlocks.Add(new SackChunk.GapBlock()
                    {
                        start = b.start,
                        end = b.end
                    });
                    b.start = diff;
                    b.end = diff;
                }
            }
            return gapAckBlocks.ToArray();
        }

        public uint markAsAcked(uint tsn)
        {
            uint nBytesAcked = 0;
            if (chunkMap.TryGetValue(tsn, out var c))
            {
                c.acked = true;
                c.retransmit = false;
                nBytesAcked = c.getDataSize();
                lock (myLock)
                {
                    nBytes -= nBytesAcked;
                }
                c.setData(new byte[0]);
            }

            return nBytesAcked;
        }

        public uint markAsAcked(DataChunk dc)
        {
            uint nBytesAcked = 0;
            if (chunkMap.TryGetValue(dc.tsn, out var c))
            {
                c.acked = true;
                c.retransmit = false;
                nBytesAcked = c.getDataSize();
                lock (myLock)
                {
                    nBytes -= nBytesAcked;
                }
                c.setData(new byte[0]);
            }
            else
            {
                dc.acked = true;
                dc.retransmit = false;
                nBytesAcked = dc.getDataSize();
                //nBytes -= nBytesAcked;
                dc.setData(new byte[0]);
            }

            return nBytesAcked;
        }

        public bool getLastTSNReceived(out uint tsn)
        {
            var s = updateSortedKeys();
            if ((s?.Length ?? 0) == 0)
            {
                tsn = 0;
                return false;
            }
            tsn = s[sorted.Length - 1];
            return true;
        }

        public bool getOldestTSNReceived(out uint tsn)
        {
            var s = updateSortedKeys();
            if ((s?.Length ?? 0) == 0)
            {
                tsn = 0;
                return false;
            }
            tsn = s[0];
            return true;
        }

        public void markAllToRetrasmit()
        {
            foreach (var c in chunkMap.Values)
            {
                if (c.acked || c.abandoned())
                {
                    continue;
                }
                c.retransmit = true;
            }
        }

        public uint getNumBytes()
        {
            return nBytes;
        }

        public virtual int size()
        {
            return chunkMap.Count;
        }
    }
}
