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

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SCTP4CS.Utils;
using SIPSorcery.Sys;

namespace SIPSorcery.Net.Sctp
{
    public enum ChunkType : byte
    {
        DATA = 0,
        INIT = 1,
        INITACK = 2,
        SACK = 3,
        HEARTBEAT = 4,
        HEARTBEAT_ACK = 5,
        ABORT = 6,
        SHUTDOWN = 7,
        SHUTDOWN_ACK = 8,
        ERROR = 9,
        COOKIE_ECHO = 10,
        COOKIE_ACK = 11,
        ECNE = 12,
        CWR = 13,
        SHUTDOWN_COMPLETE = 14,
        AUTH = 15,
        PKTDROP = 129,
        RE_CONFIG = 130,
        FORWARDTSN = 192,
        ASCONF = 193,
        ASCONF_ACK = 128,
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /*
	0                   1                   2                   3
	0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
	+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	|   Chunk Type  | Chunk  Flags  |        Chunk Length           |
	+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
	\                                                               \
	/                          Chunk Value                          /
	\                                                               \
	+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

	Chunk Length: 16 bits (unsigned integer)

	This value represents the size of the chunk in bytes, including
	the Chunk Type, Chunk Flags, Chunk Length, and Chunk Value fields.
	Therefore, if the Chunk Value field is zero-length, the Length
	field will be set to 4.  The Chunk Length field does not count any
	chunk padding.


     Chunk Type  Chunk Name
     --------------------------------------------------------------
     0xC1    Address Configuration Change Chunk        (ASCONF)
     0x80    Address Configuration Acknowledgment      (ASCONF-ACK)

     +------------+------------------------------------+
     | Chunk Type | Chunk Name                         |
     +------------+------------------------------------+
     | 130        | Re-configuration Chunk (RE-CONFIG) |
     +------------+------------------------------------+

     The following new chunk type is defined:

     Chunk Type    Chunk Name
     ------------------------------------------------------
     192 (0xC0)    Forward Cumulative TSN (FORWARD TSN)

     Chunk Type  Chunk Name
     --------------------------------------------------------------
     0x81    Packet Drop Chunk        (PKTDROP)

    Chunk Named Variables? :

         1	Heartbeat Info	[RFC4960]
         2-4	Unassigned	
         5	IPv4 Address	[RFC4960]
         6	IPv6 Address	[RFC4960]
         7	State Cookie	[RFC4960]
         8	Unrecognized Parameters	[RFC4960]
         9	Cookie Preservative	[RFC4960]
         10	Unassigned	
         11	Host Name Address	[RFC4960]
         12	Supported Address Types	[RFC4960]
         13	Outgoing SSN Reset Request Parameter	[RFC6525]
         14	Incoming SSN Reset Request Parameter	[RFC6525]
         15	SSN/TSN Reset Request Parameter	[RFC6525]
         16	Re-configuration Response Parameter	[RFC6525]
         17	Add Outgoing Streams Request Parameter	[RFC6525]
         18	Add Incoming Streams Request Parameter	[RFC6525]
         19-32767	Unassigned	
         32768	Reserved for ECN Capable (0x8000)	
         32770	Random (0x8002)	[RFC4805]
         32771	Chunk List (0x8003)	[RFC4895]
         32772	Requested HMAC Algorithm Parameter (0x8004)	[RFC4895]
         32773	Padding (0x8005)	
         32776	Supported Extensions (0x8008)	[RFC5061]
         32777-49151	Unassigned	
         49152	Forward TSN supported (0xC000)	[RFC3758]
         49153	Add IP Address (0xC001)	[RFC5061]
         49154	Delete IP Address (0xC002)	[RFC5061]
         49155	Error Cause Indication (0xC003)	[RFC5061]
         49156	Set Primary Address (0xC004)	[RFC5061]
         49157	Success Indication (0xC005)	[RFC5061]
         49158	Adaptation Layer Indication (0xC006)	[RFC5061]
         */
    /// </remarks>
    public abstract class Chunk
    {
        public const byte TBIT = 1;

        private static ILogger logger = Log.Logger;

        public static Chunk mkChunk(ByteBuffer pkt)
        {
            Chunk ret = null;
            if (pkt.remaining() >= 4)
            {
                ChunkType type = (ChunkType)pkt.GetByte();
                byte flags = pkt.GetByte();
                int length = pkt.GetUShort();
                switch (type)
                {
                    case ChunkType.DATA:
                        ret = new DataChunk(flags, length, pkt);
                        break;
                    case ChunkType.INIT:
                        ret = new InitChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.SACK:
                        ret = new SackChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.INITACK:
                        ret = new InitAckChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.COOKIE_ECHO:
                        ret = new CookieEchoChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.COOKIE_ACK:
                        ret = new CookieAckChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.ABORT:
                        ret = new AbortChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.HEARTBEAT:
                        ret = new HeartBeatChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.RE_CONFIG:
                        ret = new ReConfigChunk(type, flags, length, pkt);
                        break;
                    case ChunkType.ERROR:
                        ret = new ErrorChunk(type, flags, length, pkt);
                        break;
                    default:
                        logger.LogWarning($"SCTP unknown chunk type received {type}.");
                        ret = new FailChunk(type, flags, length, pkt);
                        break;
                }
                if (ret != null)
                {
                    if (pkt.hasRemaining())
                    {
                        int mod = ret.getLength() % 4;
                        if (mod != 0)
                        {
                            for (int pad = mod; pad < 4; pad++)
                            {
                                pkt.GetByte();
                            }
                        }
                    }
                }
            }
            return ret;
        }

        public ChunkType _type;
        public byte _flags;
        int _length;
        protected ByteBuffer _body;
        public List<VariableParam> _varList = new List<VariableParam>();


        protected Chunk(ChunkType type)
        {
            _type = type;
        }

        protected Chunk(ChunkType type, byte flags, int length, ByteBuffer pkt)
        {
            _type = type;
            _flags = flags;
            _length = length;
            _body = pkt.slice();
            _body.Limit = length - 4;
            pkt.Position += (length - 4);
        }

        public void write(ByteBuffer ret)
        {
            ret.Put((byte)_type);
            ret.Put((byte)_flags);
            ret.Put((ushort)4); // marker for length;
            putFixedParams(ret);
            int pad = 0;
            if (_varList != null)
            {
                foreach (VariableParam v in this._varList)
                {
                    //logger.LogDebug("var " + v.getName() + " at " + ret.Position);

                    ByteBuffer var = ret.slice();
                    var.Put((ushort)v.getType());
                    var.Put((ushort)4); // length holder.
                    v.writeBody(var);
                    var.Put(2, (ushort)var.Position);
                    //logger.LogDebug("setting var length to " + var.Position);
                    pad = var.Position % 4;
                    pad = (pad != 0) ? 4 - pad : 0;
                    //logger.LogDebug("padding by " + pad);
                    ret.Position += var.Position + pad;
                }
            }
            //Console.WriteLine("un padding by " + pad);
            ret.Position -= pad;
            // and push the new length into place.
            ret.Put(2, (ushort)ret.Position);
            //Console.WriteLine("setting chunk length to " + ret.position());
        }

        public override string ToString()
        {
            return $"Chunk: type {_type}, flags {((0xff) & _flags).ToString("X4")}, length {_length}.";
        }

        public ChunkType getType()
        {
            return _type;
        }

        protected int getLength()
        {
            return _length;
        }

        protected VariableParam readVariable()
        {
            int type = _body.GetUShort();
            int len = _body.GetUShort();
            int blen = len - 4;
            //byte[] data;
            Unknown var;
            switch (type)
            {
                case 1:
                    var = new HeartbeatInfo(1, "HeartbeatInfo");
                    break;
                //      2-4	Unassigned	
                case 5:
                    var = new IPv4Address(5, "IPv4Address");
                    break;
                case 6:
                    var = new IPv6Address(6, "IPv6Address");
                    break;
                case 7:
                    var = new StateCookie(7, "StateCookie");
                    break;
                case 8:
                    var = new UnrecognizedParameters(8, "UnrecognizedParameters");
                    break;
                case 9:
                    var = new CookiePreservative(9, "CookiePreservative");
                    break;
                //      10	Unassigned	
                case 11:
                    var = new HostNameAddress(11, "HostNameAddress");
                    break;
                case 12:
                    var = new SupportedAddressTypes(12, "SupportedAddressTypes");
                    break;
                case 13:
                    var = new OutgoingSSNResetRequestParameter(13, "OutgoingSSNResetRequestParameter");
                    break;
                case 14:
                    var = new IncomingSSNResetRequestParameter(14, "IncomingSSNResetRequestParameter");
                    break;
                case 15:
                    var = new SSNTSNResetRequestParameter(15, "SSNTSNResetRequestParameter");
                    break;
                case 16:
                    var = new ReconfigurationResponseParameter(16, "ReconfigurationResponseParameter");
                    break;
                case 17:
                    var = new AddOutgoingStreamsRequestParameter(17, "AddOutgoingStreamsRequestParameter");
                    break;
                case 18:
                    var = new AddIncomingStreamsRequestParameter(18, "AddIncomingStreamsRequestParameter");
                    break;
                //      19-32767	Unassigned	
                case 32768:
                    var = new Unknown(32768, "ReservedforECNCapable");
                    break;
                case 32770:
                    var = new RandomParam(32770, "Random");
                    break;
                case 32771:
                    var = new ChunkListParam(32771, "ChunkList");
                    break;
                case 32772:
                    var = new RequestedHMACAlgorithmParameter(32772, "RequestedHMACAlgorithmParameter");
                    break;
                case 32773:
                    var = new Unknown(32773, "Padding");
                    break;
                case 32776:
                    var = new SupportedExtensions(32776, "SupportedExtensions");
                    break;
                //      32777-49151	Unassigned	
                case 49152:
                    var = new ForwardTSNsupported(49152, "ForwardTSNsupported");
                    break;
                case 49153:
                    var = new Unknown(49153, "AddIPAddress");
                    break;
                case 49154:
                    var = new Unknown(49154, "DeleteIPAddress");
                    break;
                case 49155:
                    var = new Unknown(49155, "ErrorCauseIndication");
                    break;
                case 49156:
                    var = new Unknown(49156, "SetPrimaryAddress");
                    break;
                case 49157:
                    var = new Unknown(49157, "SuccessIndication");
                    break;
                case 49158:
                    var = new Unknown(49158, "AdaptationLayerIndication");
                    break;
                default:
                    var = new Unknown(-1, "Unknown");
                    break;
            }
            try
            {
                var.readBody(_body, blen);
                //logger.LogDebug("variable type " + var.getType() + " name " + var.getName());
            }
            catch (SctpPacketFormatException ex)
            {
                logger.LogError(ex.ToString());
            }
            if (_body.hasRemaining())
            {
                int mod = blen % 4;
                if (mod != 0)
                {
                    for (int pad = mod; pad < 4; pad++)
                    {
                        _body.GetByte();
                    }
                }
            }
            return var;
        }

        protected VariableParam readErrorParam()
        {
            int type = _body.GetUShort();
            int len = _body.GetUShort();
            int blen = len - 4;
            //byte[] data;
            KnownError var = null;
            switch (type)
            {
                case 1:
                    var = new KnownError(1, "InvalidStreamIdentifier");
                    break;//[RFC4960]
                case 2:
                    var = new KnownError(2, "MissingMandatoryParameter");
                    break;//[RFC4960]
                case 3:
                    var = new StaleCookieError();
                    break;//[RFC4960]
                case 4:
                    var = new KnownError(4, "OutofResource");
                    break;//[RFC4960]
                case 5:
                    var = new KnownError(5, "UnresolvableAddress");
                    break;//[RFC4960]
                case 6:
                    var = new KnownError(6, "UnrecognizedChunkType");
                    break;//[RFC4960]
                case 7:
                    var = new KnownError(7, "InvalidMandatoryParameter");
                    break;//[RFC4960]
                case 8:
                    var = new KnownError(8, "UnrecognizedParameters");
                    break;//[RFC4960]
                case 9:
                    var = new KnownError(9, "NoUserData");
                    break;//[RFC4960]
                case 10:
                    var = new KnownError(10, "CookieReceivedWhileShuttingDown");
                    break;//[RFC4960]
                case 11:
                    var = new KnownError(11, "RestartofanAssociationwithNewAddresses");
                    break;//[RFC4960]
                case 12:
                    var = new KnownError(12, "UserInitiatedAbort");
                    break;//[RFC4460]
                case 13:
                    var = new ProtocolViolationError(13, "ProtocolViolation");
                    break;//[RFC4460]
                          // 14-159,Unassigned,
                case 160:
                    var = new KnownError(160, "RequesttoDeleteLastRemainingIPAddress");
                    break;//[RFC5061]
                case 161:
                    var = new KnownError(161, "OperationRefusedDuetoResourceShortage");
                    break;//[RFC5061]
                case 162:
                    var = new KnownError(162, "RequesttoDeleteSourceIPAddress");
                    break;//[RFC5061]
                case 163:
                    var = new KnownError(163, "AssociationAbortedduetoillegalASCONF-ACK");
                    break;//[RFC5061]
                case 164:
                    var = new KnownError(164, "Requestrefused-noauthorization");
                    break;//[RFC5061]
                          // 165-260,Unassigned,
                case 261:
                    var = new KnownError(261, "UnsupportedHMACIdentifier");
                    break;//[RFC4895]
                          // 262-65535,Unassigned,
            }
            try
            {
                var.readBody(_body, blen);
                //logger.LogDebug("variable type " + var.getType() + " name " + var.getName());
                //logger.LogDebug("additional info " + var.ToString());
            }
            catch (SctpPacketFormatException ex)
            {
                logger.LogError(ex.ToString());
            }
            if (_body.hasRemaining())
            {
                int mod = blen % 4;
                if (mod != 0)
                {
                    for (int pad = mod; pad < 4; pad++)
                    {
                        _body.GetByte();
                    }
                }
            }
            return var;
        }

        protected abstract void putFixedParams(ByteBuffer ret);

        public virtual void validate()
        { // todo be more specific in the Exception tree
          // throw new Exception("Not supported yet."); //To change body of generated methods, choose Tools | Templates.
        }

        protected class HeartbeatInfo : KnownParam
        {
            public HeartbeatInfo(int t, string n) : base(t, n) { }
        }

        protected class ForwardTSNsupported : KnownParam
        {
            public ForwardTSNsupported(int t, string n) : base(t, n) { }
        }

        protected class RandomParam : KnownParam
        {
            public RandomParam(int t, string n) : base(t, n) { }

            public override string ToString()
            {
                string ret = " random value ";
                ret += Packet.getHex(this.getData());
                return base.ToString() + ret;
            }
        }

        protected class ChunkListParam : KnownParam
        {
            public ChunkListParam(int t, string n) : base(t, n) { }

            public override string ToString()
            {
                string ret = " ChunksTypes ";
                byte[] data = this.getData();
                for (int i = 0; i < data.Length; i++)
                {
                    ret += $" {(ChunkType)data[i]}";
                }
                return base.ToString() + ret;
            }
        }

        protected class SupportedExtensions : KnownParam
        {
            public SupportedExtensions() : base(32776, "SupportedExtensions") { }

            public SupportedExtensions(int t, string n) : base(t, n) { }

            public override string ToString()
            {
                string ret = " ChunksTypes ";
                byte[] data = this.getData();
                for (int i = 0; i < data.Length; i++)
                {
                    ret += $" ${(ChunkType)data[i]}";
                }
                return base.ToString() + ret;
            }
        }
    }
}
