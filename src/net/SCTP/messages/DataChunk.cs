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
 * @author Westhawk Ltd<thp@westhawk.co.uk>
 */

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// <![CDATA[
    /*

     0                   1                   2                   3
     0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
     |   Type = 0    | Reserved|U|B|E|    Length                     |
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
     |                              TSN                              |
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
     |      Stream Identifier S      |   Stream Sequence Number n    |
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
     |                  Payload Protocol Identifier                  |
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
     \                                                               \
     /                 User Data (seq n of Stream S)                 /
     \                                                               \
     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    Length:
    This field indicates the length of the DATA chunk in bytes from
    the beginning of the type field to the end of the User Data field
    excluding any padding.  A DATA chunk with one byte of user data
    will have Length set to 17 (indicating 17 bytes).

    A DATA chunk with a User Data field of length L will have the
    Length field set to (16 + L) (indicating 16+L bytes) where L MUST
    be greater than 0.

   Payload Protocol Identifier:
   +-------------------------------+----------+-----------+------------+
   | Value                         | SCTP     | Reference | Date       |
   |                               | PPID     |           |            |
   +-------------------------------+----------+-----------+------------+
   | WebRTC string                 | 51       | [RFCXXXX] | 2013-09-20 |
   | WebRTC Binary Partial         | 52       | [RFCXXXX] | 2013-09-20 |
   | (Deprecated)                  |          |           |            |
   | WebRTC Binary                 | 53       | [RFCXXXX] | 2013-09-20 |
   | WebRTC string Partial         | 54       | [RFCXXXX] | 2013-09-20 |
   | (Deprecated)                  |          |           |            |
   | WebRTC string Empty           | 56       | [RFCXXXX] | 2014-08-22 |
   | WebRTC Binary Empty           | 57       | [RFCXXXX] | 2014-08-22 |
   +-------------------------------+----------+-----------+------------+

     */
    /// ]]>
    /// </remarks>
    public class DataChunk : Chunk, IComparer<DataChunk>, IComparable<DataChunk>
    {
        private static ILogger logger = Log.Logger;

        public const uint WEBRTCCONTROL = 50;
        public const uint WEBRTCstring = 51;
        public const uint WEBRTCBINARY = 53;
        public const uint WEBRTCstringEMPTY = 56;
        public const uint WEBRTCBINARYEMPTY = 57;

        public const int CONTINUEFLAG = 0;
        public const int BEGINFLAG = 2;
        public const int ENDFLAG = 1;
        public const int SINGLEFLAG = 3;
        public const int UNORDERED = 4;

        private uint _tsn;
        private int _streamId;
        private ushort _sSeqNo;
        private uint _ppid;
        private byte[] _data;
        private int _dataOffset;
        private uint _dataLength;

        private DataChannelOpen _open;
        private InvalidDataChunkException _invalid;
        private bool _gapAck;
        private long _retryTime;
        public int _retryCount;
        private long _sentTime;
        public bool retransmit;
        public bool _abandoned;
        public bool allInFlight;
        public bool acked;
        public bool unordered;
        public DataChunk Head;
        public uint tsn
        {
            get
            {
                return _tsn;
            }
        }
        public bool endingFragment
        {
            get
            {
                return _flags == ENDFLAG || _flags == SINGLEFLAG;
            }
        }
        public bool beginningFragment
        {
            get
            {
                return _flags == BEGINFLAG || _flags == SINGLEFLAG;
            }
        }
        public int missIndicator;
        public int nSent;
        public bool immediateSack;

        public uint payloadType
        {
            get
            {
                return _ppid;
            }
        }

        public int streamIdentifier
        {
            get
            {
                return getStreamId();
            }
        }

        public ushort streamSequenceNumber
        {
            get
            {
                return _sSeqNo;
            }
        }

        public void setAbandoned(bool val)
        {
            if (Head != null)
            {
                Head.setAbandoned(val);
                return;
            }
            this._abandoned = val;
        }

        internal bool abandoned()
        {
            if (Head != null)
            {
                return Head.abandoned();
            }
            return _abandoned && allInFlight;
        }

        public DataChunk(byte flags, int length, ByteBuffer pkt) : base(ChunkType.DATA, flags, length, pkt)
        {
            //logger.LogDebug("read in chunk header " + length);
            //logger.LogDebug("body remaining " + _body.remaining());

            if (_body.remaining() >= 12)
            {
                _tsn = _body.GetUInt();
                _streamId = _body.GetUShort();
                _sSeqNo = _body.GetUShort();
                _ppid = _body.GetUInt();

                //logger.LogDebug(" _tsn : " + _tsn
                //        + " _streamId : " + _streamId
                //        + " _sSeqNo : " + _sSeqNo
                //        + " _ppid : " + _ppid);
                //logger.LogDebug("data size remaining " + _body.remaining());

                switch (_ppid)
                {
                    case WEBRTCCONTROL:
                        ByteBuffer bb = _body.slice();
                        try
                        {
                            _open = new DataChannelOpen(bb);
                        }
                        catch (InvalidDataChunkException ex)
                        {
                            _invalid = ex;
                        }
                        //logger.LogDebug("Got an DCEP " + _open);
                        break;
                    case WEBRTCstring:
                        // what format is a string ?
                        _data = new byte[_body.remaining()];
                        _body.GetBytes(_data, _data.Length);
                        _dataOffset = 0;
                        _dataLength = (uint)_data.Length;
                        //logger.LogDebug("string data is " + Encoding.ASCII.GetString(_data));
                        break;
                    case WEBRTCBINARY:
                        _data = new byte[_body.remaining()];
                        _body.GetBytes(_data, _data.Length);
                        _dataOffset = 0;
                        _dataLength = (uint)_data.Length;
                        //logger.LogDebug("data is " + Packet.getHex(_data));
                        break;

                    default:
                        logger.LogWarning($"Invalid payload protocol identifier Id in data chunk, ppid {_ppid}.");
                        _invalid = new InvalidDataChunkException($"Invalid payload protocol identifier in data chunk, ppid {_ppid}.");
                        break;
                }
            }
        }

        public string getDataAsString()
        {
            string ret;
            switch (_ppid)
            {
                case WEBRTCCONTROL:
                    ret = "Got an DCEP " + _open;
                    break;
                case WEBRTCstring:
                    ret = Encoding.UTF8.GetString(_data, _dataOffset, (int)_dataLength);
                    break;
                case WEBRTCstringEMPTY:
                    ret = "Empty string message";
                    break;
                case WEBRTCBINARY:
                    byte[] p = new byte[_dataLength];
                    Array.Copy(_data, _dataOffset, p, 0, _dataLength);
                    ret = Packet.getHex(_data);
                    break;
                case WEBRTCBINARYEMPTY:
                    ret = "Empty binary message";
                    break;
                default:
                    ret = "Invalid Protocol Id in data Chunk " + _ppid;
                    break;
            }
            return ret;
        }

        public override void validate()
        {
            if (_invalid != null)
            {
                throw _invalid;
            }
        }

        public DataChunk() : base(ChunkType.DATA)
        {
            setFlags(0); // default assumption.
        }

        public uint getTsn()
        {
            return _tsn;
        }

        public int getStreamId()
        {
            return this._streamId;
        }

        public int getSSeqNo()
        {
            return this._sSeqNo;
        }

        public uint getPpid()
        {
            return this._ppid;
        }

        public byte[] getData()
        {
            return this._data;
        }

        public DataChannelOpen getDCEP()
        {
            return this._open;
        }

        public void setPpid(uint pp)
        {
            _ppid = pp;
        }

        public uint getDataSize()
        {
            return _dataLength;
        }

        public new int getLength()
        {
            int len = base.getLength();
            if (len == 0)
            {
                // ie outbound chunk.
                len = (int)_dataLength + 12 + 4;
            }
            return len;
        }

        protected override void putFixedParams(ByteBuffer ret)
        {
            ret.Put(_tsn);// = _body.getInt();
            ret.Put((ushort)_streamId);// = _body.getushort();
            ret.Put((ushort)_sSeqNo);// = _body.getushort();
            ret.Put(_ppid);// = _body.getInt();
            if (_dataLength > 0)
            {
                ret.Put(_data, _dataOffset, (int)_dataLength);
            }
        }

        private int pad(int len)
        {
            int mod = len % 4;
            int res = 0;
            //logger.LogDebug("field of " + len + " mod 4 is " + mod);

            if (mod > 0)
            {
                res = (4 - mod);
            }
            //logger.LogDebug("padded by " + res);
            return res;
        }

        /// <param name="tsn">the _tsn to set.</param>
        public void setTsn(uint tsn)
        {
            _tsn = tsn;
        }

        /// <param name="streamId">the _streamId to set.</param>
        public void setStreamId(int streamId)
        {
            _streamId = streamId;
        }

        /// <param name="sSeqNo">the _sSeqNo to set.</param>
        public void setsSeqNo(ushort sSeqNo)
        {
            _sSeqNo = sSeqNo;
        }

        public DataChunk mkAck(DataChannelOpen dcep)
        {
            DataChunk ack = new DataChunk();
            ack.setData(dcep.mkAck());
            ack._ppid = WEBRTCCONTROL;
            ack.setFlags(DataChunk.SINGLEFLAG);

            return ack;
        }

        public static DataChunk mkDataChannelOpen(string label)
        {
            DataChunk open = new DataChunk();
            DataChannelOpen dope = new DataChannelOpen(label);
            open.setData(dope.getBytes());
            open._ppid = WEBRTCCONTROL;
            open.setFlags(DataChunk.SINGLEFLAG);
            return open;
        }

        public override string ToString()
        {
            string ret = base.ToString();
            ret += $" ppid {_ppid}, seqn {_sSeqNo}, streamId {_streamId}, tsn {_tsn}"
                    + $", retry {_retryTime}, gap acked {_gapAck}.";
            return ret;
        }

        public void setFlags(int flag)
        {
            _flags = (byte)flag;
        }

        public int getFlags()
        {
            return _flags;
        }

        public static int GetCapacity()
        {
            return 1024; // shrug - needs to be less than the theoretical MTU or slow start fails.
        }

        public void setData(byte[] data)
        {
            _data = data;
            _dataLength = (uint)data.Length;
            _dataOffset = 0;
        }

        /// <summary
        /// Only use this method if you are certain that data won't be reused until
		/// this chunk is sent and ack'd ie after MessageCompleteHandler has been
		/// called for the surrounding message
        /// </summary>
        public void setData(byte[] data, int offs, int len)
        {
            _data = data;
            _dataLength = (uint)len;
            _dataOffset = offs;
        }

        public void setGapAck(bool b)
        {
            _gapAck = b;
        }

        public bool getGapAck()
        {
            return _gapAck;
        }

        public void setRetryTime(long l)
        {
            _retryTime = l;
            _retryCount++;
        }

        public long getRetryTime()
        {
            return _retryTime;
        }

        public int CompareTo(DataChunk o)
        {
            return Compare(this, o);
        }

        public int Compare(DataChunk o1, DataChunk o2)
        {
            return (int)(o1._tsn - o2._tsn);
        }

        public long getSentTime()
        {
            return _sentTime;
        }

        public void setSentTime(long now)
        {
            _sentTime = now;
        }

        internal void setAllInflight()
        {
            if (Head != null)
            {
                Head.allInFlight = true;
                return;
            }
            allInFlight = true;
        }
    }
}
