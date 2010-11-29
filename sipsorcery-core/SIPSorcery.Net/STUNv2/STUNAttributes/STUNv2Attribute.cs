//-----------------------------------------------------------------------------
// Filename: STUNv2Attribute.cs
//
// Description: Implements STUN message attributes as defined in RFC5389.
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
    }

    public class STUNv2AttributeTypes
    {
        public static STUNv2AttributeTypesEnum GetSTUNAttributeTypeForId(int stunAttributeTypeId)
        {
            return (STUNv2AttributeTypesEnum)Enum.Parse(typeof(STUNv2AttributeTypesEnum), stunAttributeTypeId.ToString(), true);
        }
    }
    
    public class STUNv2Attribute
	{
        public const short STUNATTRIBUTE_HEADER_LENGTH = 4;
        
        public STUNv2AttributeTypesEnum AttributeType = STUNv2AttributeTypesEnum.Unknown;
        public UInt16 Length;
        public byte[] Value;

        public STUNv2Attribute(STUNv2AttributeTypesEnum attributeType, UInt16 length, byte[] value)
        {
            AttributeType = attributeType;
            Length = length;
            Value = value;
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
                        stunAttributeValue = new byte[stunAttributeLength];
                        Buffer.BlockCopy(buffer, startIndex + 4, stunAttributeValue, 0, stunAttributeLength);
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
                    else
                    {
                        attribute = new STUNv2Attribute(attributeType, stunAttributeLength, stunAttributeValue);
                    }
                            
                    attributes.Add(attribute);

                    startAttIndex = startAttIndex + 4 + stunAttributeLength;
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
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(Length)), 0, buffer, startIndex + 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)AttributeType), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, buffer, startIndex + 2, 2);
            }

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(Value, 0, buffer, startIndex + 4, Length);
            }
            else
            {
                Buffer.BlockCopy(Value, 0, buffer, startIndex + 4, Length);
            }

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + Length;
        }

        public new virtual string ToString()
        {
            string attrDescrString = "STUNv2 Attribute: " + AttributeType.ToString() + ", length=" + Length + ".";

            return attrDescrString;
        }
	}
}
