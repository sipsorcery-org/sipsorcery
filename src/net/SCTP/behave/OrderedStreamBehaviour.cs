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

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.net.SCTP.messages;
using SIPSorcery.Sys;

/**
 *
 * @author tim
 */
namespace SIPSorcery.Net.Sctp
{
    internal class OrderedStreamBehaviour : SCTPStreamBehaviour
    {
        private OrderedMessageHandler _orderedMessageHandler = new OrderedMessageHandler();
        private static ILogger logger = Log.Logger;

        protected bool _ordered = true;

        public void deliver(SCTPStream s, SortedArray<DataChunk> stash, SCTPStreamListener l)
        {
            //stash is the list of all DataChunks that have not yet been turned into whole messages
            //we assume that it is sorted by stream sequence number.
            List<DataChunk> delivered = new List<DataChunk>();
            if (stash.Count == 0)
            {
                return; // I'm not fond of these early returns 
            }


            foreach (DataChunk dc in stash)
            {
                int messageNo = s.getNextMessageSeqIn();

                int flags = dc.getFlags() & DataChunk.SINGLEFLAG; // mask to the bits we want
                long tsn = dc.getTsn();
                switch (flags)
                {
                    case DataChunk.SINGLEFLAG:
                        SCTPMessage single = new SCTPMessage(s, dc);
                        if (single.deliver(l))
                        {
                            delivered.Add(dc);
                            messageNo++;
                            s.setNextMessageSeqIn(messageNo);
                        }
                        break;
                    case DataChunk.BEGINFLAG:
                    case DataChunk.ENDFLAG:
                    case DataChunk.CONTINUEFLAG:
                        var orderedMessage = _orderedMessageHandler.GetMessage(dc.getSSeqNo())
                            .Add(dc, flags);

                        if (orderedMessage.IsReady)
                        {
                            SCTPMessage deliverable = new SCTPMessage(s, orderedMessage.ToArray());
                            if (deliverable.deliver(l))
                            {
                                _orderedMessageHandler.RemoveMessage(orderedMessage);
                                orderedMessage.AddToList(delivered);
                                messageNo++;
                                s.setNextMessageSeqIn(messageNo);
                            }
                        }
                        break;
                    default:
                        throw new Exception("[IllegalStateException] Impossible value in stream logic");
                }
            }
            stash.RemoveWhere((dc) => { return delivered.Contains(dc); });
        }

        public Chunk[] respond(SCTPStream a)
        {
            return null;
        }

    }
}
