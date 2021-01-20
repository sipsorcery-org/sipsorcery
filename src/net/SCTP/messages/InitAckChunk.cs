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

using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */

/*

 The format of the INIT ACK chunk is shown below:

 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |   Type = 2    |  Chunk Flags  |      Chunk Length             |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |                         Initiate Tag                          |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |              Advertised Receiver Window Credit                |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |  Number of Outbound Streams   |  Number of Inbound Streams    |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 |                          Initial TSN                          |
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 \                                                               \
 /              Optional/Variable-Length Parameters              /
 \                                                               \
 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
 */
namespace SIPSorcery.Net.Sctp
{
    public class InitAckChunk : Chunk
    {

        private static ILogger logger = Log.Logger;

        uint _initiateTag;
        uint _adRecWinCredit;
        int _numOutStreams;
        int _numInStreams;
        uint _initialTSN;
        private byte[] _cookie;
        private byte[] _supportedExtensions;

        public InitAckChunk() : base(ChunkType.INITACK) { }

        public uint getInitiateTag()
        {
            return _initiateTag;
        }

        public void setInitiateTag(uint v)
        {
            _initiateTag = v;
        }

        public uint getAdRecWinCredit()
        {
            return _adRecWinCredit;
        }

        public void setAdRecWinCredit(uint v)
        {
            _adRecWinCredit = v;
        }

        public int getNumOutStreams()
        {
            return _numOutStreams;
        }

        public void setNumOutStreams(int v)
        {
            _numOutStreams = v;
        }

        public int getNumInStreams()
        {
            return _numInStreams;
        }

        public void setNumInStreams(int v)
        {
            _numInStreams = v;
        }

        public uint getInitialTSN()
        {
            return _initialTSN;
        }

        public void setInitialTSN(uint v)
        {
            _initialTSN = v;
        }

        public byte[] getCookie()
        {
            return _cookie;
        }

        public void setCookie(byte[] v)
        {
            _cookie = v;
        }

        public InitAckChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            if (_body.remaining() >= 16)
            {
                _initiateTag = _body.GetUInt();
                _adRecWinCredit = _body.GetUInt(); ;
                _numOutStreams = _body.GetUShort();
                _numInStreams = _body.GetUShort();
                _initialTSN = _body.GetUInt();
                logger.LogDebug("Init Ack" + this.ToString());
                while (_body.hasRemaining())
                {
                    VariableParam v = readVariable();
                    _varList.Add(v);
                }

                foreach (VariableParam v in _varList)
                {
                    // now look for variables we are expecting...
                    //logger.LogDebug("variable of type: " + v.getName() + " " + v.ToString());
                    if (typeof(StateCookie).IsAssignableFrom(v.GetType()))
                    {
                        _cookie = ((StateCookie)v).getData();
                    }
                    //else
                    //{
                    //    logger.LogDebug("ignored variable of type: " + v.getName());
                    //}
                }

            }
        }

        public override string ToString()
        {
            string ret = base.ToString();
            ret += " initiateTag : " + _initiateTag
                    + " adRecWinCredit : " + _adRecWinCredit
                    + " numOutStreams : " + _numOutStreams
                    + " numInStreams : " + _numInStreams
                    + " initialTSN : " + _initialTSN;
            //+ ((_supportedExtensions == null) ? " no supported extensions" : " supported extensions are: " + chunksToNames(_supportedExtensions));
            ;
            return ret;
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put(_initiateTag);
            ret.Put(_adRecWinCredit);
            ret.Put((ushort)_numOutStreams);
            ret.Put((ushort)_numInStreams);
            ret.Put(_initialTSN);
            if (_cookie != null)
            {
                StateCookie sc = new StateCookie();
                sc.setData(_cookie);
                _varList.Add(sc);
            }
            if (_supportedExtensions != null)
            {
                SupportedExtensions se = new SupportedExtensions();
                se.setData(_supportedExtensions);
                _varList.Add(se);
            }
        }

        public byte[] getSupportedExtensions(byte[] v)
        {
            return _supportedExtensions;
        }

        public void setSupportedExtensions(byte[] v)
        {
            _supportedExtensions = v;
        }
    }
}
