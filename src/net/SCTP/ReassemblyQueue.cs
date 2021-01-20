using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using SCTP4CS.Utils;

namespace SIPSorcery.Net.Sctp
{
    public class ReassemblyQueue
    {
        public UInt64 nBytes;
        private object myLock = new object();
        public int si;
        public ushort nextSSN; // expected SSN for next ordered chunk
        public List<DataChunk> unorderedChunks = new List<DataChunk>();
        public SortedArray<ChunkSet> ordered = new SortedArray<ChunkSet>();
        public List<ChunkSet> unordered = new List<ChunkSet>();

        public int Count
        {
            get
            {
                lock (myLock)
                {
                    int count = 0;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        count += ordered[i].Count;
                    }
                    return count + unorderedChunks.Count;
                }
            }
        }

        public bool push(DataChunk chunk, out SortedArray<DataChunk> chunksReady)
        {
            lock (myLock)
            {
                chunksReady = new SortedArray<DataChunk>();
                ChunkSet? cset = null;

                if (chunk.streamIdentifier != si)
                {
                    return false;
                }

                if (chunk.unordered)
                {
                    // First, insert into unorderedChunks array
                    unorderedChunks.Add(chunk);

                    nBytes += chunk.getDataSize();

                    // Scan unorderedChunks that are contiguous (in TSN)
                    cset = findCompleteUnorderedChunkSet();

                    // If found, append the complete set to the unordered array
                    if (cset != null)
                    {
                        unordered.Add(cset.Value);
                        return true;
                    }

                    return false;
                }

                // This is an ordered chunk

                if (Utils.sna16LT(chunk.streamSequenceNumber, nextSSN))
                {
                    return false;
                }

                // Check if a chunkSet with the SSN already exists
                foreach (var set in ordered)
                {
                    if (set.ssn == chunk.streamSequenceNumber)
                    {
                        cset = set;
                        break;
                    }
                }

                // If not found, create a new chunkSet
                if (cset == null)
                {
                    if (ordered.Count == 0)
                    {
                        nextSSN = chunk.streamSequenceNumber;
                    }
                    cset = new ChunkSet(chunk.streamSequenceNumber, chunk.payloadType);

                    ordered.Add(cset.Value);
                }

                nBytes += chunk.getDataSize();

                if (cset.Value.push(chunk))
                {
                    chunksReady = read();
                    return true;
                }
                return false;
            }
        }

        private SortedArray<DataChunk> read()
        {
            var list = new SortedArray<DataChunk>();
            ChunkSet? cset = null;
            // Check unordered first
            if (unordered.Count > 0)
            {
                cset = unordered[0];
                unordered.RemoveAt(0);
            }
            else if (ordered.Count > 0)
            {
                // Now, check ordered
                cset = ordered[0];
                if (!cset?.isComplete() ?? false)
                {
                    return list;
                }
                if (Utils.sna16GT(cset.Value.ssn, nextSSN))
                {
                    return list;
                }
                ordered.Remove(cset.Value);
                if (cset.Value.ssn == nextSSN)
                {
                    nextSSN++;
                }
            }
            else
            {
                return list;
            }

            foreach (var c in cset.Value.chunks)
            {
                var toCopy = (int)c.getDataSize();
                subtractNumBytes(toCopy);
                list.Add(c);
            }

            return list;
        }

        void subtractNumBytes(int nBytes) 
        {
            var cur = this.nBytes;

            if ((int)cur >= nBytes)
            {
                this.nBytes -= (ulong)nBytes;
            }
            else
            {
                this.nBytes = 0;
            }
        }

        ChunkSet? findCompleteUnorderedChunkSet() 
        {
            var startIdx = -1;
            var nChunks = 0;
            uint lastTSN = 0;
            bool found = false;

	        for (int i = 0; i < unorderedChunks.Count; i++)
            {
                var c = unorderedChunks[i];
		        // seek beigining
		        if (c.beginningFragment)
                {
                    startIdx = i;

                    nChunks = 1;

                    lastTSN = c.tsn;


                    if (c.endingFragment)
                    {
                        found = true;
                        break;
                    }
                    continue;

                }

		        if (startIdx < 0)
                {
                    continue;
                }

		        // Check if contiguous in TSN
		        if (c.tsn != lastTSN+1)
                {
                    startIdx = -1;
                    continue;
                }

                lastTSN = c.tsn;
                nChunks++;

		        if (c.endingFragment)
                {
                    found = true;

                    break;
                }
            }

	        if (!found)
            {
                return null;
            }

            // Extract the range of chunks
            List<DataChunk> chunks = new List<DataChunk>();

            for (int x=startIdx; x < nChunks; x++)
            {
                chunks.Add(unorderedChunks[x]);
            }
            foreach (var c in chunks)
            {
                unorderedChunks.Remove(c);
            }

            var chunkSet = new ChunkSet(0, chunks[0].payloadType);
            chunkSet.chunks.AddToList(chunks);

            return chunkSet;
        }
    }

    public struct ChunkSet : IComparable
    {
        public int Count;
        public ushort ssn;
        public uint ppi;
        public SortedArray<DataChunk> chunks;
        public ChunkSet(ushort ssn, uint ppi)
        {
            Count = 0;
            this.ssn = ssn;
            this.ppi = ppi;
            chunks = new SortedArray<DataChunk>();
        }
        public bool push(DataChunk chunk)
        {
            // check if dup
            foreach (var c in chunks)
            {
                if (c.tsn == chunk.tsn)
                {
                    return false;
                }
            }

            // append and sort
            chunks.Add(chunk);
            Count++;

            // Check if we now have a complete set
            return isComplete();
        }

        public bool isComplete() 
        {
	        // Condition for complete set
	        //   0. Has at least one chunk.
	        //   1. Begins with beginningFragment set to true
	        //   2. Ends with endingFragment set to true
	        //   3. TSN monotinically increase by 1 from beginning to end

	        // 0.
	        var nChunks = chunks.Count;
	        if (nChunks == 0)
            {
                return false;
	        }

	        // 1.
	        if (!chunks[0].beginningFragment)
            {
                return false;
	        }

	        // 2.
	        if (!chunks[nChunks-1].endingFragment)
            {
                return false;
	        }

            // 3.
            uint lastTSN = 0;
	        for (int i=0; i < chunks.Count; i++)
            {
                var c = chunks[i];
		        if (i > 0)
                {
			        // Fragments must have contiguous TSN
			        // From RFC 4960 Section 3.3.1:
			        //   When a user message is fragmented into multiple chunks, the TSNs are
			        //   used by the receiver to reassemble the message.  This means that the
			        //   TSNs for each fragment of a fragmented user message MUST be strictly
			        //   sequential.
			        if (c.tsn != lastTSN +1)
                    {
                        // mid or end fragment is missing
                        return false;
			        }
		        }

                lastTSN = c.tsn;
	        }

            return true;
        }

        public int CompareTo(object obj)
        {
            var c = (ChunkSet)obj;
            if (c.ssn == this.ssn)
            {
                return 0;
            }
            if (c.ssn < this.ssn)
            {
                return 1;
            }
            return -1;
        }
    }
}
