//-----------------------------------------------------------------------------
// Filename: STUNv2Attribute.cs
//
// Description: Implements STUN message attributes as defined in RFC5389.
// 
// 15.  STUN Attributes
//
//   After the STUN header are zero or more attributes.  Each attribute
//   MUST be TLV encoded, with a 16-bit type, 16-bit length, and value.
//   Each STUN attribute MUST end on a 32-bit boundary.  As mentioned
//   above, all fields in an attribute are transmitted most significant
//   bit first.
//
//       0                   1                   2                   3
//       0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//      |         Type                  |            Length             |
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//      |                         Value (variable)                ....
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
//                    Figure 4: Format of STUN Attributes
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
    public enum STUNv2AttributeTypesEnum : ushort
    {
        Unknown = 0,
        MappedAddress = 0x0001,
        ResponseAddress = 0x0002,       // Not used in RFC5389.
        ChangeRequest = 0x0003,         // Not used in RFC5389.
        SourceAddress = 0x0004,         // Not used in RFC5389.
        ChangedAddress = 0x0005,        // Not used in RFC5389.
        Username = 0x0006,
        Password = 0x0007,              // Not used in RFC5389.
        MessageIntegrity = 0x0008,
        ErrorCode = 0x0009,
        UnknownAttributes = 0x000A,
        ReflectedFrom = 0x000B,         // Not used in RFC5389.
        Realm = 0x0014,
        Nonce = 0x0015,
        XORMappedAddress = 0x0020,

        Software = 0x8022,              // Added in RFC5389.
        AlternateServer = 0x8023,       // Added in RFC5389.
        FingerPrint = 0x8028,           // Added in RFC5389.

        IceControlling = 0x802a,
        Priority = 0x0024,

        UseCandidate = 0x0025,          // Added in RFC5245.

        // New attributes defined in TURN (RFC5766).
        ChannelNumber = 0x000C,
        Lifetime = 0x000D,
        XORPeerAddress = 0x0012,
        Data = 0x0013,
        XORRelayedAddress = 0x0016,
        EvenPort = 0x0018,
        RequestedTransport = 0x0019,
        DontFragment = 0x001A,
        ReservationToken = 0x0022,
    }

    public class STUNv2AttributeTypes
    {
        public static STUNv2AttributeTypesEnum GetSTUNAttributeTypeForId(int stunAttributeTypeId)
        {
            return (STUNv2AttributeTypesEnum)Enum.Parse(typeof(STUNv2AttributeTypesEnum), stunAttributeTypeId.ToString(), true);
        }
    }

    public class STUNv2AttributeConstants
    {
        public static readonly byte[] UdpTransportType = new byte[] { 0x17, 0x00, 0x00, 0x00 };     // The payload type for UDP in a RequestedTransport type attribute.
    }


    public class STUNv2Attribute
    {
        public const short STUNATTRIBUTE_HEADER_LENGTH = 4;

        private static ILog logger = STUNAppState.logger;

        public STUNv2AttributeTypesEnum AttributeType = STUNv2AttributeTypesEnum.Unknown;
        public byte[] Value;

        public virtual UInt16 PaddedLength
        {
            get
            {
                if (Value != null)
                {
                    return Convert.ToUInt16((Value.Length % 4 == 0) ? Value.Length : Value.Length + (4 - (Value.Length % 4)));
                }
                else
                {
                    return 0;
                }
            }
        }

        public STUNv2Attribute(STUNv2AttributeTypesEnum attributeType, byte value)
        {
            AttributeType = attributeType;
            Value = new byte[] { value };
        }

        public STUNv2Attribute(STUNv2AttributeTypesEnum attributeType, byte[] value)
        {
            AttributeType = attributeType;
            Value = value;
        }

        public STUNv2Attribute(STUNv2AttributeTypesEnum attributeType, ushort value)
        {
            AttributeType = attributeType;

            if (BitConverter.IsLittleEndian)
            {
                Value = BitConverter.GetBytes(Utility.ReverseEndian(value));
            }
            else
            {
                Value = BitConverter.GetBytes(value);
            }
        }

        public STUNv2Attribute(STUNv2AttributeTypesEnum attributeType, int value)
        {
            AttributeType = attributeType;

            if (BitConverter.IsLittleEndian)
            {
                Value = BitConverter.GetBytes(Utility.ReverseEndian(Convert.ToUInt32(value)));
            }
            else
            {
                Value = BitConverter.GetBytes(value);
            }
        }

        public static List<STUNv2Attribute> ParseMessageAttributes(byte[] buffer, int startIndex, int endIndex)
        {
            if (buffer != null && buffer.Length > startIndex && buffer.Length >= endIndex)
            {
                List<STUNv2Attribute> attributes = new List<STUNv2Attribute>();
                int startAttIndex = startIndex;

                while (startAttIndex < endIndex)
                {
                    UInt16 stunAttributeType = BitConverter.ToUInt16(buffer, startAttIndex);
                    UInt16 stunAttributeLength = BitConverter.ToUInt16(buffer, startAttIndex + 2);
                    byte[] stunAttributeValue = null;

                    if (BitConverter.IsLittleEndian)
                    {
                        stunAttributeType = Utility.ReverseEndian(stunAttributeType);
                        stunAttributeLength = Utility.ReverseEndian(stunAttributeLength);
                    }

                    if (stunAttributeLength > 0)
                    {
                        if (stunAttributeLength + startIndex + 4 > endIndex)
                        {
                            logger.Warn("The attribute length on a STUN parameter was greater than the available number of bytes.");
                        }
                        else
                        {
                            stunAttributeValue = new byte[stunAttributeLength];
                            Buffer.BlockCopy(buffer, startAttIndex + 4, stunAttributeValue, 0, stunAttributeLength);
                        }
                    }

                    STUNv2AttributeTypesEnum attributeType = STUNv2AttributeTypes.GetSTUNAttributeTypeForId(stunAttributeType);

                    STUNv2Attribute attribute = null;
                    if (attributeType == STUNv2AttributeTypesEnum.ChangeRequest)
                    {
                        attribute = new STUNv2ChangeRequestAttribute(stunAttributeValue);
                    }
                    else if (attributeType == STUNv2AttributeTypesEnum.MappedAddress)
                    {
                        attribute = new STUNv2AddressAttribute(stunAttributeValue);
                    }
                    else if (attributeType == STUNv2AttributeTypesEnum.ErrorCode)
                    {
                        attribute = new STUNv2ErrorCodeAttribute(stunAttributeValue);
                    }
                    else if (attributeType == STUNv2AttributeTypesEnum.XORMappedAddress || attributeType == STUNv2AttributeTypesEnum.XORPeerAddress || attributeType == STUNv2AttributeTypesEnum.XORRelayedAddress)
                    {
                        attribute = new STUNv2XORAddressAttribute(attributeType, stunAttributeValue);
                    }
                    else
                    {
                        attribute = new STUNv2Attribute(attributeType, stunAttributeValue);
                    }

                    attributes.Add(attribute);

                    // Attributes start on 32 bit word boundaries so where an attribute length is not a multiple of 4 it gets padded. 
                    int padding = (stunAttributeLength % 4 != 0) ? 4 - (stunAttributeLength % 4) : 0;

                    startAttIndex = startAttIndex + 4 + stunAttributeLength + padding;
                }

                return attributes;
            }
            else
            {
                return null;
            }
        }

        public virtual int ToByteBuffer(byte[] buffer, int startIndex)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((ushort)AttributeType)), 0, buffer, startIndex, 2);

                if (Value != null && Value.Length > 0)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(Convert.ToUInt16(Value.Length))), 0, buffer, startIndex + 2, 2);
                }
                else
                {
                    buffer[startIndex + 2] = 0x00;
                    buffer[startIndex + 3] = 0x00;
                }
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)AttributeType), 0, buffer, startIndex, 2);

                if (Value != null && Value.Length > 0)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt16(Value.Length)), 0, buffer, startIndex + 2, 2);
                }
                else
                {
                    buffer[startIndex + 2] = 0x00;
                    buffer[startIndex + 3] = 0x00;
                }
            }

            if (Value != null && Value.Length > 0)
            {
                Buffer.BlockCopy(Value, 0, buffer, startIndex + 4, Value.Length);
            }

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + PaddedLength;
        }

        public new virtual string ToString()
        {
            string attrDescrString = "STUNv2 Attribute: " + AttributeType.ToString() + ", length=" + PaddedLength + ".";

            return attrDescrString;
        }
    }
}
