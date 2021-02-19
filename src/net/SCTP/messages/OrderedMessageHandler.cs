//-----------------------------------------------------------------------------
// Filename: OrderedMessageHandler.cs
//
// Description: Handler for an SCTP stream using ordered messages.
//
// Author(s):
// @Terricide
//
// History:
// 12 Aug 2020	@Terricide	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace SIPSorcery.Net.Sctp
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
