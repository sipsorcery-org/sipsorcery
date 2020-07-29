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
    * https://tools.ietf.org/html/draft-ietf-rtcweb-data-protocol-09
    5.1.  DATA_CHANNEL_OPEN Message

    This message is sent initially on the stream used for user messages
    using the channel.

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |  Message Type |  Channel Type |            Priority           |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |                    Reliability Parameter                      |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    |         Label Length          |       Protocol Length         |
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    \                                                               /
    |                             Label                             |
    /                                                               \
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    \                                                               /
    |                            Protocol                           |
    /                                                               \
    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

     Reliability Parameter:
    +------------------------------------------------+------+-----------+
    | Name                                           | Type | Reference |
    +------------------------------------------------+------+-----------+
    | DATA_CHANNEL_RELIABLE                          | 0x00 | [RFCXXXX] |
    | DATA_CHANNEL_RELIABLE_UNORDERED                | 0x80 | [RFCXXXX] |
    | DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT           | 0x01 | [RFCXXXX] |
    | DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT_UNORDERED | 0x81 | [RFCXXXX] |
    | DATA_CHANNEL_PARTIAL_RELIABLE_TIMED            | 0x02 | [RFCXXXX] |
    | DATA_CHANNEL_PARTIAL_RELIABLE_TIMED_UNORDERED  | 0x82 | [RFCXXXX] |
    | Reserved                                       | 0x7f | [RFCXXXX] |
    | Reserved                                       | 0xff | [RFCXXXX] |
    | Unassigned                                     | rest |           |
    +------------------------------------------------+------+-----------+
    */
    /// ]]>
    /// </remarks>
    public class DataChannelOpen
    {
        public const byte RELIABLE = 0x0;
        public const byte PARTIAL_RELIABLE_REXMIT = 0x01;
        public const byte PARTIAL_RELIABLE_REXMIT_UNORDERED = (byte)0x81;
        public const byte PARTIAL_RELIABLE_TIMED = 0x02;
        public const byte PARTIAL_RELIABLE_TIMED_UNORDERED = (byte)0x82;
        public const byte RELIABLE_UNORDERED = (byte)0x80;

        private static ILogger logger = Log.Logger;

        private byte _messType;
        private byte _chanType;
        private int _priority;
        private long _reliablity;
        public int _labLen;
        public int _protLen;
        private byte[] _label;
        private byte[] _protocol;
        const int OPEN = 0x03;
        const int ACK = 0x02;
        bool _isAck = false;

        public DataChannelOpen(string label) : this((byte)RELIABLE, 0, 0, label, "") { }

        public DataChannelOpen(byte chanType, int priority, long reliablity, string label, string protocol)
        {
            _messType = (byte)OPEN;
            _chanType = chanType;
            _priority = priority;
            _reliablity = reliablity;
            _label = Encoding.UTF8.GetBytes(label);
            _protocol = Encoding.UTF8.GetBytes(protocol);
            _labLen = _label.Length;
            _protLen = _protocol.Length;
        }

        public byte[] getBytes()
        {
            //int sz = 12 + _labLen + pad(_labLen) + _protLen + pad(_protLen);
            int sz = 12 + _labLen + _protLen;
            //logger.LogDebug("DataChannelOpen needs " + sz + " bytes ");

            byte[] ret = new byte[sz];
            ByteBuffer buff = new ByteBuffer(ret);
            buff.Put((byte)_messType);
            buff.Put((byte)_chanType);
            buff.Put((ushort)_priority);
            buff.Put((int)_reliablity);
            buff.Put((ushort)_labLen);
            buff.Put((ushort)_protLen);
            buff.Put(_label);
            //buff.Position += pad(_labLen);
            buff.Put(_protocol);
            //buff.Position += pad(_protLen);

            return ret;
        }

        public DataChannelOpen(ByteBuffer bb)
        {
            _messType = bb.GetByte();
            switch (_messType)
            {
                case OPEN:
                    _chanType = bb.GetByte();
                    _priority = bb.GetUShort();
                    _reliablity = bb.GetInt();
                    _labLen = bb.GetUShort();
                    _protLen = bb.GetUShort();
                    _label = new byte[_labLen];
                    bb.GetBytes(_label, _label.Length);
                    _protocol = new byte[_protLen];
                    bb.GetBytes(_protocol, _protocol.Length);
                    break;
                case ACK:
                    _isAck = true;
                    break;
                default:
                    throw new InvalidDataChunkException("Unexpected DCEP message type " + _messType);
            }
        }

        public override string ToString()
        {
            return _isAck ? "Ack " : "Open "
                    + " _chanType =" + (int)_chanType
                    + " _priority = " + _priority
                    + " _reliablity = " + _reliablity
                    + " _label = " + Encoding.UTF8.GetString(_label)
                    + " _protocol = " + Packet.getHex(_protocol);
        }

        public bool isAck()
        {
            return _isAck;
        }

        internal SCTPStreamBehaviour mkStreamBehaviour()
        {
            //logger.LogDebug("Making a behaviour for dcep stream " + _label);
            SCTPStreamBehaviour behave = null;
            switch (_chanType)
            {
                case RELIABLE:
                    behave = new OrderedStreamBehaviour();
                    break;
                case RELIABLE_UNORDERED:
                    behave = new UnorderedStreamBehaviour();
                    break;
                // todo these next 4 are wrong... the odering is atleast correct
                // even if the retry is wrong.
                case PARTIAL_RELIABLE_REXMIT:
                case PARTIAL_RELIABLE_TIMED:
                    behave = new OrderedStreamBehaviour();
                    break;
                case PARTIAL_RELIABLE_REXMIT_UNORDERED:
                case PARTIAL_RELIABLE_TIMED_UNORDERED:
                    behave = new UnorderedStreamBehaviour();
                    break;
            }
            //if (behave != null)
            //{
            //    logger.LogDebug(_label + " behaviour is " + behave.GetType().Name);
            //}

            return behave;
        }

        public string getLabel()
        {
            return Encoding.UTF8.GetString(_label);
        }

        public byte[] mkAck()
        {
            byte[] a = new byte[1];
            a[0] = ACK;
            return a;
        }
    }
}
