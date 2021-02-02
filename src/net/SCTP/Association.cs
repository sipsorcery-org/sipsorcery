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
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */
namespace SIPSorcery.Net.Sctp
{
    public abstract class Association
    {
        private bool _even;

        public abstract void associate();

        /**
		 * <code>
		 *                     -----          -------- (from any state)
		 *                   /       \      /  rcv ABORT      [ABORT]
		 *  rcv INIT        |         |    |   ----------  or ----------
		 *  --------------- |         v    v   delete TCB     snd ABORT
		 *  generate Cookie  \    +---------+                 delete TCB
		 *  snd INIT ACK       ---|  CLOSED |
		 *                        +---------+
		 *                         /      \      [ASSOCIATE]
		 *                        /        \     ---------------
		 *                       |          |    create TCB
		 *                       |          |    snd INIT
		 *                       |          |    strt init timer
		 *        rcv valid      |          |
		 *      COOKIE  ECHO     |          v
		 *  (1) ---------------- |      +------------+
		 *      create TCB       |      | COOKIE-WAIT| (2)
		 *      snd COOKIE ACK   |      +------------+
		 *                       |          |
		 *                       |          |    rcv INIT ACK
		 *                       |          |    -----------------
		 *                       |          |    snd COOKIE ECHO
		 *                       |          |    stop init timer
		 *                       |          |    strt cookie timer
		 *                       |          v
		 *                       |      +--------------+
		 *                       |      | COOKIE-ECHOED| (3)
		 *                       |      +--------------+
		 *                       |          |
		 *                       |          |    rcv COOKIE ACK
		 *                       |          |    -----------------
		 *                       |          |    stop cookie timer
		 *                       v          v
		 *                     +---------------+
		 *                     |  ESTABLISHED  |
		 *                     +---------------+
		 * </code>
		 */
        public enum State
        {
            COOKIEWAIT, COOKIEECHOED, ESTABLISHED,
            SHUTDOWNPENDING, SHUTDOWNSENT, SHUTDOWNRECEIVED,
            SHUTDOWNACKSENT, CLOSED
        };

        private byte[] _supportedExtensions = { (byte)ChunkType.RE_CONFIG };
        /*
		 For what it is worth, here's the logic as to why we don't have any supported extensions.
		 { 
		 ASCONF, // this is ICE's job so we never send ASCONF or 
		 ASCONF-ACK, // ASCONF-ACK
		 FORWARDTSN, // we may end up wanting this - it supports partial reliability - aka giving up..
		 PKTDROP, // this is an optional performance enhancement especially valuable for middleboxes (we aren't one)
		 RE-CONFIG, // not sure about this - but lets assume for now that the w3c interface doesn't support stream resets.
		 AUTH // Assume DTLS will cover this for us if we never send ASCONF packets.
		 */

        private static ILogger logger = Log.Logger;

        public static int COOKIESIZE = 32;
        private static long VALIDCOOKIELIFE = 60000;
        /*
		 RTO.Initial - 3 seconds
		 RTO.Min - 1 second
		 RTO.Max - 60 seconds
		 Max.Burst - 4
		 RTO.Alpha - 1/8
		 RTO.Beta - 1/4
		 Valid.Cookie.Life - 60 seconds
		 Association.Max.Retrans - 10 attempts
		 Path.Max.Retrans - 5 attempts (per destination address)
		 Max.Init.Retransmits - 8 attempts
		 HB.interval - 30 seconds
		 HB.Max.Burst - 1
		 */
        protected DatagramTransport _transp;
        private Thread _rcv;
        private int _peerVerTag;
        protected int _myVerTag;
        private SecureRandom _random;
        private long _winCredit;
        private uint _farTSN;
        private int MAXSTREAMS = 1000;
        private int _maxOutStreams;
        private int _maxInStreams;
        public static uint MAXBUFF = 20 * 1024;
        public uint _nearTSN;
        private int _srcPort;
        private int _destPort;
        private Dictionary<int, SCTPStream> _streams;
        private AssociationListener _al;
        private Dictionary<long, DataChunk> _outbound;
        protected State _state;
        private Dictionary<uint, DataChunk> _holdingPen;
        private static int TICK = 1000; // loop time in rcv
        static int __assocNo = 1;
        private ReconfigState reconfigState;

        /// <summary>
        /// The next ID to use when creating a new stream. 
        /// Note originally this value as generated randomly between 0 and 65535 but Chrome was rejecting
        /// ID's that were greater than maximum number of streams set on the SCTP association. Hence
        /// changed it to be sequential.
        /// </summary>
        private int _nextStreamID = 0;

        class CookieHolder
        {
            public byte[] cookieData;
            public long cookieTime;
        };
        private List<CookieHolder> _cookies = new List<CookieHolder>();

        // default is server
        public Association(DatagramTransport transport, AssociationListener al, int srcPort, int dstPort) : this(transport, al, false, srcPort, dstPort) { }

        public Association(DatagramTransport transport, AssociationListener al, bool client, int srcPort, int dstPort)
        {
            //logger.LogDebug($"SCTP created an Association of type: {this.GetType().Name}.");
            _al = al;
            _random = new SecureRandom();
            _myVerTag = _random.NextInt();
            _transp = transport;
            _streams = new Dictionary<int, SCTPStream>();
            _outbound = new Dictionary<long, DataChunk>();
            _holdingPen = new Dictionary<uint, DataChunk>();
            var IInt = new FastBit.Int(_random.NextInt());
            _nearTSN = new FastBit.Uint(IInt.b0, IInt.b1, IInt.b2, IInt.b3).Auint;
            _state = State.CLOSED;
            if (_transp != null)
            {
                startRcv();
            }
            else
            {
                logger.LogError("Created an Association with a null transport somehow...");
            }
            __assocNo++;
            /*
			the method used to determine which
			side uses odd or even is based on the underlying DTLS connection
			role: the side acting as the DTLS client MUST use Streams with even
			Stream Identifiers, the side acting as the DTLS server MUST use
			Streams with odd Stream Identifiers. */
            _even = client;
            _nextStreamID = (_even) ? _nextStreamID : _nextStreamID + 1;

            _srcPort = srcPort;
            _destPort = dstPort;
        }

        protected byte[] getSupportedExtensions()
        { // this lets others switch features off.
            return _supportedExtensions;
        }
        public uint getNearTSN()
        {
            return _nearTSN;
        }
        byte[] getUnionSupportedExtensions(byte[] far)
        {
            ByteBuffer unionbb = new ByteBuffer(new byte[far.Length]);
            for (int f = 0; f < far.Length; f++)
            {
                //logger.LogDebug($"offered extension {(ChunkType)far[f]}.");
                for (int n = 0; n < _supportedExtensions.Length; n++)
                {
                    //logger.LogDebug($"supported extension {(ChunkType)_supportedExtensions[n]}.");
                    if (_supportedExtensions[n] == far[f])
                    {
                        //logger.LogDebug($"matching extension {(ChunkType)_supportedExtensions[n]}.");
                        unionbb.Put(far[f]);
                    }
                }
            }
            byte[] res = new byte[unionbb.Position];
            unionbb.Position = 0;
            unionbb.GetBytes(res, res.Length);
            //logger.LogDebug("union of extensions contains :" + Chunk.chunksToNames(res));
            return res;
        }

        public void deal(Packet rec)
        {
            List<Chunk> replies = new List<Chunk>();
            rec.validate(this);
            List<Chunk> cl = rec.getChunkList();
            foreach (Chunk c in cl)
            {
                c.validate();
            }
            if (cl[0].getType() == ChunkType.INIT)
            {
                _srcPort = rec.getDestPort();
                _destPort = rec.getSrcPort();
            }
            foreach (Chunk c in cl)
            {
                if (!deal(c, replies))
                {
                    break; // drop the rest of the packet.
                }
            }
            // find the highest sack.
            Chunk hisack = null;
            foreach (var c in replies)
            {
                if (c.getType() == ChunkType.SACK)
                {
                    if (hisack == null || ((SackChunk)c).getCumuTSNAck() > ((SackChunk)hisack).getCumuTSNAck())
                    {
                        hisack = c;
                    }
                }
            }
            // remove all sacks
            replies.RemoveAll((Chunk c) =>
            {
                return c.getType() == ChunkType.SACK;
            });
            // insert the highest one first.
            if (hisack != null)
            {
                replies.Insert(0, hisack);
            }
            try
            {
                send(replies.ToArray());
            }
            catch (Exception end)
            {
                unexpectedClose(end);
                logger.LogError(end.ToString());
            }
        }

        void startRcv()
        {
            Association me = this;
            _rcv = new Thread(() =>
            {
                try
                {
                    byte[] buf = new byte[_transp.GetReceiveLimit()];
                    while (_rcv != null)
                    {
                        try
                        {
                            var length = _transp.Receive(buf, 0, buf.Length, TICK);
                            if (length > 0)
                            {
                                //var b = Packet.getHex(buf, 0, length);
                                //logger.LogInformation($"DTLS message recieved\n{b}");   
                                ByteBuffer pbb = new ByteBuffer(buf);
                                pbb.Limit = length;
                                Packet rec = new Packet(pbb);
                                deal(rec);
                            }
                            else if (length == DtlsSrtpTransport.DTLS_RECEIVE_ERROR_CODE)
                            {
                                // The DTLS transport has been closed or i no longer available.
                                break;
                            }
                            else
                            {
                                logger.LogInformation("Timeout -> short packet " + length);
                            }
                        }
                        catch (SocketException e)
                        {
                            // ignore. it should be a timeout.
                            switch (e.SocketErrorCode)
                            {
                                case SocketError.TimedOut:
                                    logger.LogDebug("tick time out");
                                    break;
                                default:
                                    throw;
                            }
                        }
                    }
                    logger.LogDebug("SCTP message receive was empty, closing association listener.");

                    _transp.Close();
                }
                catch (EndOfStreamException eof)
                {
                    unexpectedClose(eof);
                    logger.LogDebug(eof.ToString());
                }
                catch (Exception ex)
                {
                    logger.LogDebug("Association receive failed " + ex.GetType().Name + " " + ex.ToString());
                }
            })
            { IsBackground = true };
            _rcv.Priority = ThreadPriority.Highest;
            _rcv.Name = "AssocRcv" + __assocNo;
            _rcv.Start();
        }

        /**
		 * override this and return false to disable the bi-directionalinit gamble
		 * that webRTC expects. Only do this in testing. Production should have it
		 * enabled since it also provides glare resolution.
		 *
		 * @return true
		 */
        public bool doBidirectionalInit()
        {
            return true;
        }

        protected void send(Chunk[] c)
        {
            if ((c != null) && (c.Length > 0))
            {
                ByteBuffer obb = mkPkt(c);
                //logger.LogDebug($"SCTP packet send: {Packet.getHex(obb)}");
                lock (this)
                {
                    _transp.Send(obb.Data, obb.offset, obb.Limit);
                }
            }
            //else
            //{
            //    logger.LogDebug("Blocked empty packet send() - probably no response needed.");
            //}
        }

        /**
		 * decide if we want to do the webRTC specified bidirectional init _very_
		 * useful to be able to switch this off for testing
		 *
		 * @return
		 */
        private bool acceptableStateForInboundInit()
        {
            bool ret = false;
            if (doBidirectionalInit())
            {
                ret = ((_state == State.CLOSED) || (_state == State.COOKIEWAIT) || (_state == State.COOKIEECHOED));
            }
            else
            {
                ret = (_state == State.CLOSED);
            }
            return ret;
        }

        /**
		 *
		 * @param c - Chunk to be processed
		 * @return valid - false if the remaining chunks of the packet should be
		 * ignored.
		 * @throws IOException
		 * @throws SctpPacketFormatException
		 */
        private bool deal(Chunk c, List<Chunk> replies)
        {
            ChunkType ty = c.getType();
            bool ret = true;
            State oldState = _state;
            Chunk[] reply = null;
            switch (ty)
            {
                case ChunkType.INIT:
                    if (acceptableStateForInboundInit())
                    {
                        InitChunk init = (InitChunk)c;
                        reply = inboundInit(init);
                    }
                    else
                    {
                        // logger.LogDebug("Got an INIT when state was " + _state.ToString() + " - ignoring it for now ");
                    }
                    break;
                case ChunkType.INITACK:
                    //logger.LogDebug("got initack " + c.ToString());
                    if (_state == State.COOKIEWAIT)
                    {
                        InitAckChunk iack = (InitAckChunk)c;
                        reply = iackDeal(iack);
                    }
                    else
                    {
                        //logger.LogDebug("Got an INITACK when not waiting for it - ignoring it");
                    }
                    break;
                case ChunkType.COOKIE_ECHO:
                    // logger.LogDebug("got cookie echo " + c.ToString());
                    reply = cookieEchoDeal((CookieEchoChunk)c);
                    if (reply.Length > 0)
                    {
                        ret = !typeof(ErrorChunk).IsAssignableFrom(reply[0].GetType()); // ignore any following data chunk. 
                    }
                    break;
                case ChunkType.COOKIE_ACK:
                    //logger.LogDebug("got cookie ack " + c.ToString());
                    if (_state == State.COOKIEECHOED)
                    {
                        _state = State.ESTABLISHED;
                    }
                    break;
                case ChunkType.DATA:
                    //logger.LogDebug("got data " + c.ToString());
                    reply = dataDeal((DataChunk)c);
                    break;
                case ChunkType.ABORT:
                    // no reply we should just bail I think.
                    _rcv = null;
                    _transp.Close();
                    break;
                case ChunkType.HEARTBEAT:
                    reply = ((HeartBeatChunk)c).mkReply();
                    break;
                case ChunkType.SACK:
                    //logger.LogDebug("got tsak for TSN " + ((SackChunk)c).getCumuTSNAck());
                    reply = sackDeal((SackChunk)c);
                    // fix the outbound list here
                    break;
                case ChunkType.RE_CONFIG:
                    reply = reconfigState.deal((ReConfigChunk)c);
                    break;
                case ChunkType.ERROR:
                    logger.LogWarning($"SCTP error chunk received.");
                    foreach (var vparam in c._varList)
                    {
                        if (vparam is KnownError)
                        {
                            var knownErr = vparam as KnownError;
                            logger.LogWarning($"{knownErr.getName()}, {knownErr}");
                        }
                    }
                    break;
            }
            if (reply != null)
            {
                foreach (Chunk r in reply)
                {
                    replies.Add(r);
                }
                // theoretically could be multiple DATA in a single packet - 
                // we'd send multiple SACKs in reply - ToDo fix that

            }
            if ((_state == State.ESTABLISHED) && (oldState != State.ESTABLISHED))
            {
                if (null != _al)
                {
                    _al.onAssociated(this);
                }
                reconfigState = new ReconfigState(this, _farTSN);

            }
            if ((oldState == State.ESTABLISHED) && (_state != State.ESTABLISHED))
            {
                if (null != _al)
                {
                    _al.onDisAssociated(this);
                }
            }
            return ret;
        }

        public ByteBuffer mkPkt(Chunk[] cs)
        {
            Packet ob = new Packet(_srcPort, _destPort, _peerVerTag);
            foreach (Chunk r in cs)
            {
                //logger.LogDebug("adding chunk to outbound packet: " + r.ToString());
                ob.getChunkList().Add(r);
                //todo - this needs to workout if all the chunks will fit...
            }
            ByteBuffer obb = ob.getByteBuffer();
            return obb;
        }

        public int getPeerVerTag()
        {
            return _peerVerTag;
        }

        public int getMyVerTag()
        {
            return _myVerTag;
        }

        /*
		 Ok - confession here - we are not following the RFC. 
		 We don't encode a pile of stuff into the cookie and decode it
		 when we get the cookie back, then use that data to initialize the Association.
		 The rationale in the RFC is to protect the assocaition from resource exhaustion
		 by fake cookies from bad guys - which makes sense if you are a naked SCTP stack on
		 the internet accepting UDP packets (or IP ones).
		 We on the other hand have already been through 2 levels of validation with ICE and DTLS,
		 and have already committed a pile of resource to this connection, so 32 bytes more won't kill us.
    
		 The only downside is that if the far end spams us with a pile of inits at speed, we may erase one that we've
		 replied to and that was about to be a happy camper. Shrug.
		 */
        private CookieHolder checkCookieEcho(byte[] cookieData)
        {
            CookieHolder same = null;
            foreach (CookieHolder cookie in _cookies)
            {
                byte[] cd = cookie.cookieData;
                if (cd.Length == cookieData.Length)
                {
                    int i = 0;
                    while (i < cd.Length)
                    {
                        if (cd[i] != cookieData[i])
                        {
                            break;
                        }
                        i++;
                    }
                    if (i == cd.Length)
                    {
                        same = cookie;
                        break;
                    }
                }
            }
            return same;
        }

        private uint howStaleIsMyCookie(CookieHolder cookie)
        {
            uint ret = 0;
            long now = TimeExtension.CurrentTimeMillis();

            if ((now - cookie.cookieTime) < VALIDCOOKIELIFE)
            {
                ret = 0;
            }
            else
            {
                ret = (uint)((now - cookie.cookieTime) - VALIDCOOKIELIFE);
            }
            return ret;
        }

        public void sendInit()
        {
            InitChunk c = new InitChunk();
            c.setInitialTSN(this._nearTSN);
            c.setNumInStreams(this.MAXSTREAMS);
            c.setNumOutStreams(this.MAXSTREAMS);
            c.setAdRecWinCredit(MAXBUFF);
            c.setInitiate(this.getMyVerTag());
            Chunk[] s = new Chunk[1];
            s[0] = c;
            this._state = State.COOKIEWAIT;
            try
            {
                this.send(s);
            }
            catch (EndOfStreamException end)
            {
                unexpectedClose(end);
                logger.LogError(end.ToString());
            } // todo need timer here.....
        }

        protected virtual Chunk[] iackDeal(InitAckChunk iack)
        {
            iack.getAdRecWinCredit();
            iack.getInitialTSN();
            iack.getNumInStreams();
            iack.getNumOutStreams();
            /* 
			 NOTE: TO DO - this is a protocol violation - this should be done with
			 multiple TCBS and set in cookie echo 
			 NOT HERE
			 */

            _peerVerTag = iack.getInitiateTag();
            _winCredit = iack.getAdRecWinCredit();
            _farTSN = iack.getInitialTSN() - 1;
            _maxOutStreams = Math.Min(iack.getNumInStreams(), MAXSTREAMS);
            _maxInStreams = Math.Min(iack.getNumOutStreams(), MAXSTREAMS);

            iack.getSupportedExtensions(_supportedExtensions);
            byte[] data = iack.getCookie();
            CookieEchoChunk ce = new CookieEchoChunk();
            ce.setCookieData(data);
            Chunk[] reply = new Chunk[1] { ce };
            this._state = State.COOKIEECHOED;
            return reply;
        }

        /* <pre>
		 5.2.1.  INIT Received in COOKIE-WAIT or COOKIE-ECHOED State (Item B)

		 This usually indicates an initialization collision, i.e., each
		 endpoint is attempting, at about the same time, to establish an
		 association with the other endpoint.

		 Upon receipt of an INIT in the COOKIE-WAIT state, an endpoint MUST
		 respond with an INIT ACK using the same parameters it sent in its
		 original INIT chunk (including its Initiate Tag, unchanged).  When
		 responding, the endpoint MUST send the INIT ACK back to the same
		 address that the original INIT (sent by this endpoint) was sent.

		 Upon receipt of an INIT in the COOKIE-ECHOED state, an endpoint MUST
		 respond with an INIT ACK using the same parameters it sent in its
		 original INIT chunk (including its Initiate Tag, unchanged), provided
		 that no NEW address has been added to the forming association.  If
		 the INIT message indicates that a new address has been added to the
		 association, then the entire INIT MUST be discarded, and NO changes
		 should be made to the existing association.  An ABORT SHOULD be sent
		 in response that MAY include the error 'Restart of an association
		 with new addresses'.  The error SHOULD list the addresses that were
		 added to the restarting association.

		 When responding in either state (COOKIE-WAIT or COOKIE-ECHOED) with
		 an INIT ACK, the original parameters are combined with those from the
		 newly received INIT chunk.  The endpoint shall also generate a State
		 Cookie with the INIT ACK.  The endpoint uses the parameters sent in
		 its INIT to calculate the State Cookie.

		 After that, the endpoint MUST NOT change its state, the T1-init timer
		 shall be left running, and the corresponding TCB MUST NOT be
		 destroyed.  The normal procedures for handling State Cookies when a
		 TCB exists will resolve the duplicate INITs to a single association.

		 For an endpoint that is in the COOKIE-ECHOED state, it MUST populate
		 its Tie-Tags within both the association TCB and inside the State
		 Cookie (see Section 5.2.2 for a description of the Tie-Tags).
		 </pre>
		 */
        public virtual Chunk[] inboundInit(InitChunk init)
        {
            Chunk[] reply = null;
            _peerVerTag = init.getInitiateTag();
            _winCredit = init.getAdRecWinCredit();
            _farTSN = (uint)(init.getInitialTSN() - 1);

            _maxOutStreams = Math.Min(init.getNumInStreams(), MAXSTREAMS);
            _maxInStreams = Math.Min(init.getNumOutStreams(), MAXSTREAMS);
            InitAckChunk iac = new InitAckChunk();
            iac.setAdRecWinCredit(MAXBUFF);
            iac.setNumInStreams(_maxInStreams);
            iac.setNumOutStreams(_maxOutStreams);
            iac.setInitialTSN(_nearTSN);
            iac.setInitiateTag(_myVerTag);
            CookieHolder cookie = new CookieHolder();
            cookie.cookieData = new byte[Association.COOKIESIZE];
            cookie.cookieTime = TimeExtension.CurrentTimeMillis();
            _random.NextBytes(cookie.cookieData);
            iac.setCookie(cookie.cookieData);
            _cookies.Add(cookie);

            byte[] fse = init.getFarSupportedExtensions();
            if (fse != null)
            {
                iac.setSupportedExtensions(this.getUnionSupportedExtensions(fse));
            }
            reply = new Chunk[1];
            reply[0] = iac;
            //logger.LogDebug("SCTP received INIT:" + init.ToString());
            //logger.LogDebug("Replying with init-ack :" + iac.ToString());
            return reply;
        }

        private void ingest(DataChunk dc, List<Chunk> rep)
        {
            //logger.LogDebug("SCTP received " + dc.ToString());
            Chunk closer = null;
            int sno = dc.getStreamId();
            uint tsn = dc.getTsn();
            SCTPStream _in;
            if (!_streams.TryGetValue(sno, out _in))
            {
                _in = mkStream(sno);
                _streams.Add(sno, _in);
                _al.onRawStream(_in);
            }
            Chunk[] repa;
            // todo dcep logic belongs in behave - not here.
            if (dc.getDCEP() != null)
            {
                repa = dcepDeal(_in, dc, dc.getDCEP());
                // delay 'till after first packet so we can get the label etc set 
                // _however_ this should be in behave -as mentioned above.
                try
                {
                    _al.onDCEPStream(_in, _in.getLabel(), dc.getPpid());
                    if (_in.OnOpen != null)
                    {
                        _in.OnOpen.Invoke();
                    }
                }
                catch (Exception x)
                {
                    closer = _in.immediateClose();
                    logger.LogError(x.ToString());
                }
            }
            else
            {
                repa = _in.append(dc);
            }

            if (repa != null)
            {
                foreach (Chunk r in repa)
                {
                    rep.Add(r);
                }
            }
            if (closer != null)
            {
                rep.Add(closer);
            }
            _in.inbound(dc);
            _farTSN = tsn;
        }

        private Chunk[] dataDeal(DataChunk dc)
        {
            List<Chunk> rep = new List<Chunk>();
            List<uint> duplicates = new List<uint>();

            uint tsn = dc.getTsn();
            if (tsn > _farTSN)
            {
                // put it in the pen.
                //logger.LogDebug("TSN:::" + tsn);
                DataChunk dup;
                if (_holdingPen.TryGetValue(tsn, out dup))
                {
                    duplicates.Add(tsn);
                }
                else
                {
                    _holdingPen.Add(tsn, dc);
                }
                // now see if we can deliver anything new to the streams
                bool gap = false;
                for (uint t = _farTSN + 1; !gap; t++)
                {
                    if (_holdingPen.TryGetValue(t, out dc))
                    {
                        _holdingPen.Remove(t);
                        ingest(dc, rep);
                    }
                    else
                    {
                        //logger.LogDebug("gap in inbound tsns at " + t);
                        gap = true;
                    }
                }
            }
            else
            {
                // probably wrong now.. 
                logger.LogWarning("Already seen . " + tsn + " expecting " + (_farTSN));
                duplicates.Add(tsn);
            }
            List<uint> l = new List<uint>();
            l.AddRange(_holdingPen.Keys);
            l.Sort();

            SackChunk sack = mkSack(l, duplicates);
            rep.Add(sack);
            return rep.ToArray();
        }
        // todo should be in a behave block
        // then we wouldn't be messing with stream seq numbers.

        private Chunk[] dcepDeal(SCTPStream s, DataChunk dc, DataChannelOpen dcep)
        {
            Chunk[] rep = null;
            //logger.LogDebug("dealing with a decp for stream " + dc.getDataAsString());
            if (!dcep.isAck())
            {
                //logger.LogDebug("decp is not an ack... ");

                SCTPStreamBehaviour behave = dcep.mkStreamBehaviour();
                s.setBehave(behave);
                s.setLabel(dcep.getLabel());
                lock (s)
                {
                    int seqIn = s.getNextMessageSeqIn();
                    s.setNextMessageSeqIn(seqIn + 1);
                    int seqOut = s.getNextMessageSeqOut();
                    s.setNextMessageSeqOut(seqOut + 1);
                }
                rep = new Chunk[1];
                DataChunk ack = dc.mkAck(dcep);
                s.outbound(ack);
                ack.setTsn(_nearTSN++);
                // check rollover - will break at maxint.
                rep[0] = ack;

            }
            else
            {
                //logger.LogDebug("got a dcep ack for " + s.getLabel());
                SCTPStreamBehaviour behave = dcep.mkStreamBehaviour();
                s.setBehave(behave);
                lock (s)
                {
                    int seqIn = s.getNextMessageSeqIn();
                    s.setNextMessageSeqIn(seqIn + 1);
                    int seqOut = s.getNextMessageSeqOut();
                    s.setNextMessageSeqOut(seqOut + 1);
                }
            }
            return rep;
        }

        /**
		 * <code>
		 * 2)  Authenticate the State Cookie as one that it previously generated
		 * by comparing the computed MAC against the one carried in the
		 * State Cookie.  If this comparison fails, the SCTP packet,
		 * including the COOKIE ECHO and any DATA chunks, should be silently
		 * discarded,
		 *
		 * 3)  Compare the port numbers and the Verification Tag contained
		 * within the COOKIE ECHO chunk to the actual port numbers and the
		 * Verification Tag within the SCTP common header of the received
		 * packet.  If these values do not match, the packet MUST be
		 * silently discarded.
		 *
		 * 4)  Compare the creation timestamp in the State Cookie to the current
		 * local time.  If the elapsed time is longer than the lifespan
		 * carried in the State Cookie, then the packet, including the
		 * COOKIE ECHO and any attached DATA chunks, SHOULD be discarded,
		 * and the endpoint MUST transmit an ERROR chunk with a "Stale
		 * Cookie" error cause to the peer endpoint.
		 *
		 * 5)  If the State Cookie is valid, create an association to the sender
		 * of the COOKIE ECHO chunk with the information in the TCB data
		 * carried in the COOKIE ECHO and enter the ESTABLISHED state.
		 *
		 * 6)  Send a COOKIE ACK chunk to the peer acknowledging receipt of the
		 * COOKIE ECHO.  The COOKIE ACK MAY be bundled with an outbound DATA
		 * chunk or SACK chunk; however, the COOKIE ACK MUST be the first
		 * chunk in the SCTP packet.
		 *
		 * 7)  Immediately acknowledge any DATA chunk bundled with the COOKIE
		 * ECHO with a SACK (subsequent DATA chunk acknowledgement should
		 * follow the rules defined in Section 6.2).  As mentioned in step
		 * 6, if the SACK is bundled with the COOKIE ACK, the COOKIE ACK
		 * MUST appear first in the SCTP packet.
		 * </code>
		 */
        private Chunk[] cookieEchoDeal(CookieEchoChunk echo)
        {
            Chunk[] reply = new Chunk[0];
            if (_state == State.CLOSED || _state == State.COOKIEWAIT || _state == State.COOKIEECHOED)
            {
                // Authenticate the State Cookie
                CookieHolder cookie;
                if (null != (cookie = checkCookieEcho(echo.getCookieData())))
                {
                    // Compare the creation timestamp in the State Cookie to the current local time.
                    uint howStale = howStaleIsMyCookie(cookie);
                    if (howStale == 0)
                    {
                        //enter the ESTABLISHED state
                        _state = State.ESTABLISHED;
                        /*
						 Send a COOKIE ACK chunk to the peer acknowledging receipt of the
						 COOKIE ECHO.  The COOKIE ACK MAY be bundled with an outbound DATA
						 chunk or SACK chunk; however, the COOKIE ACK MUST be the first
						 chunk in the SCTP packet.
						 */
                        reply = new Chunk[1];
                        reply[0] = new CookieAckChunk();
                    }
                    else
                    {
                        reply = new Chunk[1];
                        /* If the elapsed time is longer than the lifespan
						 * carried in the State Cookie, then the packet, including the
						 * COOKIE ECHO and any attached DATA chunks, SHOULD be discarded,
						 * and the endpoint MUST transmit an ERROR chunk with a "Stale
						 * Cookie" error cause to the peer endpoint.*/
                        StaleCookieError sce = new StaleCookieError();
                        sce.setMeasure(howStale * 1000);
                        ErrorChunk ec = new ErrorChunk(sce);
                        reply[0] = ec;
                    }
                }
                else
                {
                    logger.LogError("Got a COOKIE_ECHO that doesn't match any we sent. ?!?");
                }
            }
            else
            {
                logger.LogDebug("Got an COOKIE_ECHO when not closed - ignoring it");
            }
            return reply;
        }

        private SackChunk mkSack(List<uint> pen, List<uint> dups)
        {
            SackChunk ret = new SackChunk();
            ret.setCumuTSNAck(_farTSN);
            int stashcap = calcStashCap();
            ret.setArWin((uint)(MAXBUFF - stashcap));
            ret.setGaps(pen);
            ret.setDuplicates(dups);
            //logger.LogDebug("made SACK " + ret.ToString());
            return ret;
        }

        private int calcStashCap()
        {
            int ret = 0;
            foreach (SCTPStream s in this._streams.Values)
            {
                ret += s.stashCap();
            }
            return ret;
        }

        public abstract void enqueue(DataChunk d);

        public abstract SCTPStream mkStream(int id);


        public uint getCumAckPt()
        {
            return _farTSN;
        }
        public ReConfigChunk addToCloseList(SCTPStream st)
        {
            return reconfigState.makeClose(st);
        }

        public void closeStream(SCTPStream st)
        {
            Chunk[] cs = new Chunk[1];
            if (canSend())
            {
                //logger.LogDebug("due to reconfig stream " + st);
                cs[0] = reconfigState.makeClose(st);
                this.send(cs);
            }
        }

        public SCTPStream mkStream(string label)
        {
            //int n = 1;
            //int tries = this._maxOutStreams;
            //do
            //{
            //    n = 2 * _random.Next(this._maxOutStreams);
            //    if (!_even) n += 1;
            //    if (--tries < 0)
            //    {
            //        logger.LogError("StreamNumberInUseException");
            //        return null;
            //    }
            //} while (_streams.ContainsKey(n));
            int n = _nextStreamID;
            _nextStreamID += 2;
            return mkStream(n, label);
        }

        public int[] allStreams()
        {
            var ks = _streams.Keys;
            int[] ret = new int[ks.Count];
            int i = 0;
            foreach (int k in ks)
            {
                ret[i++] = k;
            }
            return ret;
        }
        public SCTPStream getStream(int s)
        {
            SCTPStream stream;
            return _streams.TryGetValue(s, out stream) ? stream : null;
        }

        public SCTPStream delStream(int s)
        {
            if (!_streams.ContainsKey(s))
            {
                return null;
            }
            var st = _streams[s];
            _streams.Remove(s);
            return st;
        }

        public SCTPStream mkStream(int sno, string label)
        {
            SCTPStream sout;
            if (canSend())
            {
                lock (_streams)
                {
                    if (_streams.ContainsKey(sno))
                    {
                        logger.LogError("StreamNumberInUseException");
                        return null;
                    }
                    sout = mkStream(sno);
                    sout.setLabel(label);
                    _streams.Add(sno, sout);
                }// todo - move this to behave
                DataChunk DataChannelOpen = DataChunk.mkDataChannelOpen(label);
                sout.outbound(DataChannelOpen);
                DataChannelOpen.setTsn(_nearTSN++);
                logger.LogDebug($"SCTP data channel open chunk {DataChannelOpen}.");
                Chunk[] hack = { DataChannelOpen };
                try
                {
                    send(hack);
                }
                catch (Exception end)
                {
                    unexpectedClose(end);
                    logger.LogError(end.ToString());
                }
            }
            else
            {
                throw new UnreadyAssociationException();
            }
            return sout;
        }

        public int maxMessageSize()
        {
            return 1 << 20; // shrug - I don't know 
        }

        public bool canSend()
        {
            bool ok;
            switch (_state)
            {
                case State.ESTABLISHED:
                case State.SHUTDOWNPENDING:
                case State.SHUTDOWNRECEIVED:
                    ok = true;
                    break;
                default:
                    ok = false;
                    break;
            }
            return ok;
        }

        protected void unexpectedClose(Exception end)
        {
            _rcv = null;
            _al.onDisAssociated(this);
            _state = State.CLOSED;
        }

        abstract internal void sendAndBlock(SCTPMessage m);

        abstract internal SCTPMessage makeMessage(byte[] bytes, BlockingSCTPStream aThis);

        abstract internal SCTPMessage makeMessage(string s, BlockingSCTPStream aThis);

        abstract protected Chunk[] sackDeal(SackChunk sackChunk);
    }
}
