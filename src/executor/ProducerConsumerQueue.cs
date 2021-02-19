using System;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

namespace SIPSorcery.Executor
{
    internal class ProducerConsumerQueue<T> : IDisposable
    {
        private object myLock = new object();
        private bool IsDisposed { get; set; }
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();

        public void Produce(T input)
        {
            lock (myLock)
            {
                queue.Enqueue(input);
                Monitor.Pulse(myLock);
            }
        }

        public T Consume(int timeout)
        {
            Consume(out var item, timeout);
            return item;
        }

        public bool Consume(out T item, int timeout)
        {
            while (!queue.Any())
            {
                lock (myLock)
                {
                    if (!queue.Any())
                    {
                        if (!Monitor.Wait(myLock, timeout))
                        {
                            item = default(T);
                            return false;
                        }
                    }
                }
            }

            if (queue.TryDequeue(out item))
            {
                return true;
            }

            item = default(T);
            return false;
        }

        public void Dispose()
        {
            T item;
            while (queue.TryDequeue(out item))
            {
                // do nothing
            }
            IsDisposed = true;
        }

        public struct AsyncTResult<T>
        {
            public bool Success;
            public T Value;
        }
    }
}
