using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net.Sctp
{
    public class ControlQueue : PayloadQueue
    {
        Queue<Packet> queue = new Queue<Packet>();
        public void push(Packet c)
        {
            queue.Enqueue(c);
        }

        public Packet[] popAll()
        {
            var all = new List<Packet>();
            while(queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == null)
                {
                    break;
                }
                all.Add(c);
            }
            return all.ToArray();
        }

        public override int size()
        {
            return queue.Count;
        }

        internal void pushAll(Packet[] reply)
        {
            foreach(var c in reply)
            {
                queue.Enqueue(c);
            }
        }
    }
}
