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
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

/**
 *
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */
namespace SIPSorcery.Net.Sctp
{
    internal class SCTPMessage 
    {
        private SCTPStream _stream;
        private byte[] _data;
        private int _offset = 0;
        private uint _pPid = 0;
        private ushort _mseq; // note do we need these ?
        private SCTPStreamListener _li;
        private bool _delivered;

        private static ILogger logger = Log.Logger;

        /**
		 * Outbound message - note that we assume no one will mess with data between
		 * calls to fill()
		 *
		 * @param data
		 * @param s
		 */
        public SCTPMessage(byte[] data, SCTPStream s)
        {
            _data = (data.Length > 0) ? data : new byte[1];
            _stream = s;
            _pPid = (data.Length > 0) ? DataChunk.WEBRTCBINARY : DataChunk.WEBRTCBINARYEMPTY;
        }

        public SCTPMessage(string data, SCTPStream s)
        {
            _data = (data.Length > 0) ? System.Text.Encoding.UTF8.GetBytes(data) : new byte[1];
            _stream = s;
            _pPid = (data.Length > 0) ? DataChunk.WEBRTCstring : DataChunk.WEBRTCstringEMPTY;
        }

        public SCTPMessage(SCTPStream s, SortedArray<DataChunk> chunks)
        {
            _stream = s;
            uint tot = 0;
            if ((chunks.First.getFlags() & DataChunk.BEGINFLAG) == 0)
            {
                throw new Exception("[IllegalArgumentException] must start with 'start' chunk");
            }
            if ((chunks.Last.getFlags() & DataChunk.ENDFLAG) == 0)
            {
                throw new Exception("[IllegalArgumentException] must end with 'end' chunk");
            }
            _pPid = chunks.First.getPpid();
            foreach (DataChunk dc in chunks)
            {
                tot += dc.getDataSize();
                if (_pPid != dc.getPpid())
                {
                    // aaagh 
                    throw new Exception("[IllegalArgumentException] chunk has wrong ppid" + dc.getPpid() + " vs " + _pPid);
                }
            }
            _data = new byte[tot];
            uint offs = 0;
            foreach (DataChunk dc in chunks)
            {
                Array.Copy(dc.getData(), 0, _data, offs, dc.getDataSize());
                offs += dc.getDataSize();
            }
        }

        public SCTPMessage(SCTPStream s, DataChunk singleChunk)
        {
            _stream = s;
            int flags = singleChunk.getFlags();
            if ((flags & DataChunk.SINGLEFLAG) > 0)
            {
                _data = singleChunk.getData();
                _pPid = singleChunk.getPpid();
            }
            else
            {
                throw new Exception("[IllegalArgumentException] must use a 'single' chunk");
            }
        }

        public virtual void setCompleteHandler(MessageCompleteHandler mch)
        {
            throw new Exception("[UnsupportedOperationException] Not supported yet."); //To change body of generated methods, choose Tools | Templates.
        }

        public bool hasMoreData()
        {
            return (_offset < _data.Length);
        }

        /**
		 * available datachunks are put here to be filled with data from this
		 * outbound message
		 *
		 * @param dc
		 */
        public void fill(DataChunk dc, int dsz)
        {
            int remain = _data.Length - _offset;
            if (_offset == 0)
            {
                if (remain <= dsz)
                {
                    // only one chunk
                    dc.setFlags(DataChunk.SINGLEFLAG);
                    dc.setData(_data);
                    _offset = _data.Length;
                }
                else
                {
                    // first chunk of many
                    dc.setFlags(DataChunk.BEGINFLAG);
                    dc.setData(_data, _offset, dsz);
                    _offset += dsz;
                }
            }
            else// not first
            if (remain <= dsz)
            {
                // last chunk, this will all fit.
                dc.setFlags(DataChunk.ENDFLAG);
                dc.setData(_data, _offset, remain);
                _offset += remain; // should be _data_length now
            }
            else
            {
                // middle chunk.
                dc.setFlags(0);
                dc.setData(_data, _offset, dsz);
                _offset += dsz;
            }
            dc.setPpid(_pPid);
            dc.setsSeqNo(_mseq);
            _stream.outbound(dc);
        }

        public bool deliver(SCTPStreamListener li)
        {
            _li = li;
            _delivered = false;
            //logger.LogDebug("delegating message delivery to stream of type " + _stream.GetType().Name);
            _stream.deliverMessage(this);
            return true;
        }

        public void setSeq(ushort mseq)
        {
            _mseq = mseq;
        }

        public uint Count
        {
            get
            {
                return (uint)(_data?.Length ?? 0);
            }
        }

        public void run()
        {
            //logger.LogDebug("delegated message delivery from stream of type " + _stream.GetType().Name);
            byte[] data = _data;
            if (_li != null)
            {
                switch (_pPid)
                {
                    case DataChunk.WEBRTCBINARYEMPTY:
                        data = new byte[0];
                        goto case DataChunk.WEBRTCBINARY;
                    case DataChunk.WEBRTCBINARY:
                        if (typeof(SCTPByteStreamListener).IsAssignableFrom(_li.GetType()))
                        {
                            ((SCTPByteStreamListener)_li).onMessage(_stream, data);
                            _delivered = true;
                        }
                        else
                        {
                            _li.onDataMessage(_stream, _data);
                            _delivered = true;
                        }
                        break;
                    case DataChunk.WEBRTCstringEMPTY:
                        data = new byte[0];
                        goto case DataChunk.WEBRTCstring;
                    case DataChunk.WEBRTCstring:
                        _li.onMessage(_stream, System.Text.Encoding.UTF8.GetString(_data));
                        _delivered = true;
                        break;
                }
            }
            if (!_delivered)
            {
                logger.LogDebug("Undelivered message to " + (_stream == null ? "null stream" : _stream.getLabel()) + " via " + (_li == null ? "null listener" : _li.GetType().Name) + " ppid is " + _pPid);
            }
        }

        public void acked()
        {
        }

    }
}
