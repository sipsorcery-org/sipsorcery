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
/**
 *
 * @author thp
 */

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    class ReconfigState
    {

        private static ILogger logger = Log.Logger;

        ReConfigChunk recentInbound = null;
        //ReConfigChunk recentOutboundRequest = null;
        ReConfigChunk sentReply = null;
        bool timerRunning = false;
        uint nearSeqno = 0;
        uint farSeqno = 0;
        Association assoc;
        Queue<SCTPStream> listOfStreamsToReset;

        public ReconfigState(Association a, uint farTSN)
        {
            nearSeqno = a.getNearTSN();
            farSeqno = farTSN;
            assoc = a;
            listOfStreamsToReset = new Queue<SCTPStream>();
        }

        private bool haveSeen(ReConfigChunk rconf)
        {
            return rconf.sameAs(recentInbound);
        }

        private ReConfigChunk getPrevious(ReConfigChunk rconf)
        {
            return rconf.sameAs(recentInbound) ? sentReply : null;
        }

        private bool timerIsRunning()
        {
            return timerRunning;
        }

        private void markAsAcked(ReConfigChunk rconf)
        {
            // ooh, what does this button do ??? To Do
        }

        private uint nextNearNo()
        {
            return (uint)nearSeqno++;
        }

        private uint nextFarNo()
        {
            return (uint)farSeqno++;
        }

        public uint nextDue()
        {
            return 1000;
        }

        /*
		 * https://tools.ietf.org/html/rfc6525
		 */
        public Chunk[] deal(ReConfigChunk rconf)
        {
            Chunk[] ret = new Chunk[1];
            ReConfigChunk reply = null;
            //logger.LogDebug("Got a reconfig message to deal with");
            if (haveSeen(rconf))
            {
                // if not - is this a repeat
                reply = getPrevious(rconf); // then send the same reply
            }
            if (reply == null)
            {
                // not a repeat then
                reply = new ReConfigChunk(); // create a new thing
                if (rconf.hasOutgoingReset())
                {
                    OutgoingSSNResetRequestParameter oreset = rconf.getOutgoingReset();
                    int[] streams = oreset.getStreams();
                    if (streams.Length == 0)
                    {
                        streams = assoc.allStreams();
                    }
                    if (timerIsRunning())
                    {
                        markAsAcked(rconf);
                    }
                    // if we are behind, we are supposed to wait until we catch up.
                    if (oreset.getLastAssignedTSN() > assoc.getCumAckPt())
                    {
                        //logger.LogDebug("Last assigned > farTSN " + oreset.getLastAssignedTSN() + " v " + assoc.getCumAckPt());
                        foreach (int s in streams)
                        {
                            SCTPStream defstr = assoc.getStream(s);
                            // AC: All this call did was set an unused local variable. Removed for now.
                            //defstr.setDeferred(true);
                        }
                        ReconfigurationResponseParameter rep = new ReconfigurationResponseParameter();
                        rep.setSeq(oreset.getReqSeqNo());
                        rep.setResult(ReconfigurationResponseParameter.IN_PROGRESS);
                        reply.addParam(rep);
                    }
                    else
                    {
                        // somehow invoke this when TSN catches up ?!?! ToDo
                        //logger.LogDebug("we are up-to-date ");
                        ReconfigurationResponseParameter rep = new ReconfigurationResponseParameter();
                        rep.setSeq(oreset.getReqSeqNo());
                        int result = streams.Length > 0 ? ReconfigurationResponseParameter.SUCCESS_PERFORMED : ReconfigurationResponseParameter.SUCCESS_NOTHING_TO_DO;
                        rep.setResult((uint)result); // assume all good
                        foreach (int s in streams)
                        {
                            SCTPStream cstrm = assoc.delStream(s);
                            if (cstrm == null)
                            {
                                //logger.LogError("Close a non existant stream");
                                rep.setResult(ReconfigurationResponseParameter.ERROR_WRONG_SSN);
                                break;
                                // bidriectional might be a problem here...
                            }
                            else
                            {
                                cstrm.reset();
                            }
                        }
                        reply.addParam(rep);
                    }
                }
                // ponder putting this in a second chunk ?
                if (rconf.hasIncomingReset())
                {
                    IncomingSSNResetRequestParameter ireset = rconf.getIncomingReset();
                    /*The Re-configuration
					Response Sequence Number of the Outgoing SSN Reset Request
					Parameter MUST be the Re-configuration Request Sequence Number
					of the Incoming SSN Reset Request Parameter. */
                    OutgoingSSNResetRequestParameter rep = new OutgoingSSNResetRequestParameter(nextNearNo(), ireset.getReqNo(), assoc.getNearTSN());
                    int[] streams = ireset.getStreams();
                    rep.setStreams(streams);
                    if (streams.Length == 0)
                    {
                        streams = assoc.allStreams();
                    }
                    foreach (int s in streams)
                    {
                        SCTPStream st = assoc.getStream(s);
                        if (st != null)
                        {
                            st.setClosing(true);
                        }
                    }
                    reply.addParam(rep);
                    // set outbound timer running here ???
                    //logger.LogDebug("Ireset " + ireset);
                }
            }
            if (reply.hasParam())
            {
                ret[0] = reply;
                // todo should add sack here
                //logger.LogDebug("about to reply with " + reply.ToString());
            }
            else
            {
                ret = null;
            }
            return ret;
        }

        /* we can only demand they close their outbound streams */
        /* we can request they start to close inbound (ie ask us to shut our outbound */
        /* DCEP treats streams as bi-directional - so this is somewhat of an inpedance mis-match */
        /* resulting in a temporary 'half closed' state */
        /* mull this over.... */
        public ReConfigChunk makeClose(SCTPStream st)
        {
            ReConfigChunk ret = null;
            logger.LogDebug($"SCTP closing stream {st}");
            st.setClosing(true);
            lock (listOfStreamsToReset)
            {
                listOfStreamsToReset.Enqueue(st);
            }
            if (!timerIsRunning())
            {
                ret = makeSSNResets();
            }
            return ret;
        }

        private ReConfigChunk makeSSNResets()
        {
            ReConfigChunk reply = new ReConfigChunk(); // create a new thing
            //logger.LogDebug($"SCTP closing {listOfStreamsToReset.Count} stream.");
            List<int> streamsL = new List<int>();
            lock (listOfStreamsToReset)
            {
                foreach (var s in listOfStreamsToReset) if (s.InboundIsOpen()) streamsL.Add(s.getNum());
            }
            int[] streams = streamsL.ToArray();
            if (streams.Length > 0)
            {
                OutgoingSSNResetRequestParameter rep = new OutgoingSSNResetRequestParameter(nextNearNo(), farSeqno - 1, assoc.getNearTSN());
                rep.setStreams(streams);
                reply.addParam(rep);
            }
            streamsL.Clear();
            lock (listOfStreamsToReset)
            {
                foreach (var s in listOfStreamsToReset) if (s.OutboundIsOpen()) streamsL.Add(s.getNum());
            }
            streams = streamsL.ToArray();
            if (streams.Length > 0)
            {
                IncomingSSNResetRequestParameter rep = new IncomingSSNResetRequestParameter(nextNearNo());
                rep.setStreams(streams);
                reply.addParam(rep);
            }
            //logger.LogDebug("reconfig chunk is " + reply.ToString());
            return reply;
        }
    }
}
