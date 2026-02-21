using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.Net
{
    class RTCDataChannelCollection : IReadOnlyCollection<RTCDataChannel>
    {
        readonly ConcurrentBag<RTCDataChannel> pendingChannels = new ConcurrentBag<RTCDataChannel>();
        readonly ConcurrentDictionary<ushort, RTCDataChannel> activeChannels = new ConcurrentDictionary<ushort, RTCDataChannel>();
        readonly Func<bool> useEvenIds;
        
        readonly object idSyncObj = new object();
        ushort lastChannelId = ushort.MaxValue - 1;

        public int Count => pendingChannels.Count + activeChannels.Count;

        public RTCDataChannelCollection(Func<bool> useEvenIds)
            => this.useEvenIds = useEvenIds;

        public void AddPendingChannel(RTCDataChannel channel)
            => pendingChannels.Add(channel);

        public IEnumerable<RTCDataChannel> ActivatePendingChannels()
        {
            while (pendingChannels.TryTake(out var channel))
            {
                AddActiveChannel(channel);
                yield return channel;
            }
        }
        
        public bool TryGetChannel(ushort dataChannelID, out RTCDataChannel result)
            => activeChannels.TryGetValue(dataChannelID, out result);
        
        public bool AddActiveChannel(RTCDataChannel channel)
        {
            if (channel.id.HasValue)
            {
                if (!activeChannels.TryAdd(channel.id.Value, channel))
                    return false;
            }
            else
            {
                while (true)
                {
                    var id = GetNextChannelID();
                    if (activeChannels.TryAdd(id, channel))
                    {
                        channel.id = id;
                        break;
                    }
                }
            }

            channel.onclose += OnClose;
            channel.onerror += OnError;
            return true;
            
            void OnClose()
            {
                channel.onclose -= OnClose;
                channel.onerror -= OnError;
                activeChannels.TryRemove(channel.id.Value, out _);
            }
            void OnError(string error) => OnClose();
        }
        
        ushort GetNextChannelID()
        {
            lock (idSyncObj)
            {
                unchecked
                {
                    //  The SCTP stream identifier 65535 is reserved due to SCTP INIT and
                    // INIT - ACK chunks only allowing a maximum of 65535 streams to be
                    // negotiated(0 - 65534) - https://tools.ietf.org/html/rfc8832
                    if (lastChannelId == ushort.MaxValue - 3)
                        lastChannelId += 4;
                    else
                        lastChannelId += 2;
                }
                return useEvenIds() ? lastChannelId : (ushort) (lastChannelId + 1);
            }
        }

        public IEnumerator<RTCDataChannel> GetEnumerator()
            => pendingChannels.Concat(activeChannels.Select(e => e.Value)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
