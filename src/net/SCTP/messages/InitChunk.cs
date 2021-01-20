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
 * @author tim
 */

using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    public class InitChunk : Chunk
    {
        /*
		 0                   1                   2                   3
		 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |   Type = 1    |  Chunk Flags  |      Chunk Length             |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |                         Initiate Tag                          |
		 +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
		 |           Advertised Receiver Window Credit (a_rwnd)          |
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

        private static ILogger logger = Log.Logger;

        long _initiateTag;
        uint _adRecWinCredit;
        int _numOutStreams;
        int _numInStreams;
        uint _initialTSN;
        byte[] _farSupportedExtensions;
        byte[] _farRandom;
        public bool _farForwardTSNsupported;
        byte[] _farHmacs;
        byte[] _farChunks;
        public int _outStreams;

        public InitChunk() : base(ChunkType.INIT) { }

        public InitChunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
            : base(type, flags, length, pkt)
        {
            if (_body.remaining() >= 16)
            {
                _initiateTag = _body.GetInt();
                _adRecWinCredit = _body.GetUInt();
                _numOutStreams = _body.GetUShort();
                _numInStreams = _body.GetUShort();
                _initialTSN = _body.GetUInt();
                //logger.LogDebug("Init " + this.ToString());
                while (_body.hasRemaining())
                {
                    VariableParam v = readVariable();
                    _varList.Add(v);
                }
                foreach (VariableParam v in _varList)
                {
                    // now look for variables we are expecting...
                    //logger.LogDebug("variable of type: " + v.getName() + " " + v.ToString());
                    if (typeof(SupportedExtensions).IsAssignableFrom(v.GetType()))
                    {
                        _farSupportedExtensions = ((SupportedExtensions)v).getData();
                    }
                    else if (typeof(RandomParam).IsAssignableFrom(v.GetType()))
                    {
                        _farRandom = ((RandomParam)v).getData();
                    }
                    else if (typeof(ForwardTSNsupported).IsAssignableFrom(v.GetType()))
                    {
                        _farForwardTSNsupported = true;
                    }
                    else if (typeof(RequestedHMACAlgorithmParameter).IsAssignableFrom(v.GetType()))
                    {
                        _farHmacs = ((RequestedHMACAlgorithmParameter)v).getData();
                    }
                    else if (typeof(ChunkListParam).IsAssignableFrom(v.GetType()))
                    {
                        _farChunks = ((ChunkListParam)v).getData();
                    }
                    else
                    {
                        //logger.LogDebug("unexpected variable of type: " + v.getName());
                    }
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
                    + " initialTSN : " + _initialTSN
                    + " farForwardTSNsupported : " + _farForwardTSNsupported;
            //+ ((_farSupportedExtensions == null) ? " no supported extensions" : " supported extensions are: " + chunksToNames(_farSupportedExtensions));
            return ret;
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put((int)_initiateTag);
            ret.Put(_adRecWinCredit);
            ret.Put((ushort)_numOutStreams);
            ret.Put((ushort)_numInStreams);
            ret.Put(_initialTSN);
        }

        public uint getInitiateTag()
        {
            return (uint)_initiateTag;
        }

        public uint getAdRecWinCredit()
        {
            return _adRecWinCredit;
        }

        public int getNumOutStreams()
        {
            return _numOutStreams;
        }

        public int getNumInStreams()
        {
            return _numInStreams;
        }

        public long getInitialTSN()
        {
            return _initialTSN;
        }

        public void setInitialTSN(uint tsn)
        {
            _initialTSN = tsn;
        }

        public void setAdRecWinCredit(uint credit)
        {
            _adRecWinCredit = credit;
        }

        public void setNumOutStreams(int outn)
        {
            _numOutStreams = outn;
        }

        public void setNumInStreams(int inn)
        {
            _numInStreams = inn;
        }

        public byte[] getFarSupportedExtensions()
        {
            return _farSupportedExtensions;
        }

        public void setInitiate(uint tag)
        {
            this._initiateTag = tag;
        }
    }
}
