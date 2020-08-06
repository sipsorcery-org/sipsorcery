using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Net.Sctp;

namespace SIPSorcery.net.SCTP.messages
{
    class OrderedMessageHandler
    {
        private ConcurrentDictionary<int, OrderedMessage> queue = new ConcurrentDictionary<int, OrderedMessage>();

        public OrderedMessage GetMessage(int num)
        {
            if (!queue.TryGetValue(num, out var message))
            {
                lock (queue)
                {
                    if (!queue.TryGetValue(num, out message))
                    {
                        message = new OrderedMessage(num);
                        queue.AddOrUpdate(num, message, (a, b) => message);
                    }
                }
            }
            return message;
        }

        public void RemoveMessage(OrderedMessage msg)
        {
            queue.TryRemove(msg.Number, out var orderedMessage);
        }
    }
}
