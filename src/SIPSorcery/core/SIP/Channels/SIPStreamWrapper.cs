//-----------------------------------------------------------------------------
// Filename: SIPStreamWrapper.cs
//
// Description: Helper class for Stream with a thread-safe function WriteAsync.
//
// Author(s):
// Steffen Liersch (https://www.steffen-liersch.de/)
//
// History:
// 20 Feb 2023	Steffen Liersch           Created
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SIPSorcery.SIP
{
    public sealed class SIPStreamWrapper : IDisposable
    {
        private readonly object m_syncRoot = new object();
        private readonly Queue<Request> m_pendingRequests = new Queue<Request>();
        private readonly Stream m_stream;
        private bool m_isInProgress;

        public SIPStreamWrapper(Stream stream) => m_stream = stream ?? throw new ArgumentNullException(nameof(stream));

        public void Dispose() => m_stream.Dispose();

        public IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
          => m_stream.BeginRead(buffer, offset, count, callback, state);

        public int EndRead(IAsyncResult asyncResult) => m_stream.EndRead(asyncResult);

        public Task WriteAsync(byte[] buffer, int offset, int count)
        {
            lock (m_syncRoot)
            {
                if (m_isInProgress)
                {
                    var req = new Request(buffer, offset, count);
                    m_pendingRequests.Enqueue(req);
                    return req.Task;
                }

                m_isInProgress = true;
            }

            var task = m_stream.WriteAsync(buffer, offset, count);
            task.ContinueWith(x => TrySendNextFromQueue(), TaskContinuationOptions.RunContinuationsAsynchronously);
            return task;
        }

        private void TrySendNextFromQueue()
        {
            Request request;
            lock (m_syncRoot)
            {
                if (m_pendingRequests.Count <= 0)
                {
                    m_isInProgress = false;
                    return;
                }

                request = m_pendingRequests.Dequeue();
            }

            Task task = m_stream.WriteAsync(request.Buffer, request.Offset, request.Count);
            task.ContinueWith(x => HandleEndOfQueuedRequest(request, task));
            // TaskContinuationOptions.RunContinuationsAsynchronously is not necessary here!
        }

        private void HandleEndOfQueuedRequest(Request request, Task task)
        {
            request.SetStatus(task);
            TrySendNextFromQueue();
        }

        private class Request
        {
            public readonly byte[] Buffer;
            public readonly int Offset;
            public readonly int Count;

            public Task Task => m_tcs.Task;
            private readonly TaskCompletionSource<bool> m_tcs;

            public Request(byte[] buffer, int offset, int count)
            {
                Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
                Offset = offset;
                Count = count;
                m_tcs = new TaskCompletionSource<bool>();
            }

            public void SetStatus(Task task)
            {
                AggregateException e = task.Exception;
                if (e != null)
                {
                    m_tcs.SetException(e);
                }
                else
                {
                    m_tcs.SetResult(true);
                }
            }
        }
    }
}