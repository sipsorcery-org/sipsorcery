using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net.Sctp
{
    public class PendingQueue : PayloadQueue
    {
        private object myLock = new object();
        ConcurrentQueue<DataChunk> unorderedQueue = new ConcurrentQueue<DataChunk>();
        ConcurrentQueue<DataChunk> orderedQueue = new ConcurrentQueue<DataChunk>();
        bool selected;
        bool unorderedIsSelected;

        public void push(DataChunk c)
        {
            if (c.unordered)
            {
                unorderedQueue.Enqueue(c);        
            }
            else
            {
                orderedQueue.Enqueue(c);      
            }
            lock (myLock)
            {
                nBytes += c.getDataSize();
            }
        }

        public DataChunk peek()
        {
            DataChunk c = null;
	        if (selected)
            {
		        if (unorderedIsSelected)
                {
                    if (unorderedQueue.Count == 0)
                    {
                        return null;
                    }
                    unorderedQueue.TryPeek(out c);
                    return c;
		        }
                if (orderedQueue.Count == 0)
                {
                    return null;
                }
                orderedQueue.TryPeek(out c);
            }
            if (unorderedQueue.Count > 0)
            {
                unorderedQueue.TryPeek(out c);
                return c;
                if (c != null)
                {
                    return c;
                }
            }
            if (orderedQueue.Count == 0)
            {
                return null;
            }
            orderedQueue.TryPeek(out c);
            return c;
        }


        public void pop(DataChunk c)
        {
	        if (selected)
            {
                DataChunk popped;
		        if (unorderedIsSelected)
                {
                    unorderedQueue.TryDequeue(out popped);
			        if (popped != c)
                    {
                        throw new Exception("errUnexpectedChuckPoppedUnordered");
                    }
                }
                else
                {
                    orderedQueue.TryDequeue(out popped);
                    if (popped != c)
                    {
                        throw new Exception("errUnexpectedChuckPoppedOrdered");
                    }
                }
                if (popped.endingFragment)
                {
                    selected = false;
                }
	       }
           else
           {
                if (!c.beginningFragment)
                {
                    throw new Exception("errUnexpectedQState");

                }
                if (c.unordered)
                {
                    unorderedQueue.TryDequeue(out var popped);
                    if (popped != c)
                    {
                        throw new Exception("errUnexpectedChuckPoppedUnordered");
                    }
                    if (!popped.endingFragment)
                    {
                        selected = true;
                        unorderedIsSelected = true;
                    }
                }
                else
                {
                    orderedQueue.TryDequeue(out var popped);
                    if (popped != c)
                    {
                        throw new Exception("errUnexpectedChuckPoppedOrdered");
                    }
                    if (!popped.endingFragment)
                    {
                        selected = true;
                        unorderedIsSelected = false;
                    }
                }
            }
            lock (myLock)
            {
                nBytes -= c.getDataSize();
            }
        }

        public override int size()
        {
            return unorderedQueue.Count + orderedQueue.Count;
        }
    }
}
