//-----------------------------------------------------------------------------
// Filename: STUNv2AddressAttribute.cs
//
// Description: Implements STUN address attribute as defined in RFC5389.
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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

namespace SIPSorcery.Net
{
    public class STUNv2AddressAttribute : STUNv2Attribute
    {
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;

        //protected readonly STUNv2AttributeTypesEnum Family = STUNv2AttributeTypesEnum.MappedAddress;
        public int Family = 1;      // Ipv4 = 1, IPv6 = 2.
        public int Port;
        public IPAddress Address;

        public override UInt16 PaddedLength
        {
            get { return ADDRESS_ATTRIBUTE_LENGTH;  }
        }

        public STUNv2AddressAttribute(byte[] attributeValue)
            : base(STUNv2AttributeTypesEnum.MappedAddress, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                Port = Utility.ReverseEndian(BitConverter.ToUInt16(attributeValue, 2));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2);
            }

            Address = new IPAddress(new byte[] { attributeValue[4], attributeValue[5], attributeValue[6], attributeValue[7] });
        }

        public STUNv2AddressAttribute(STUNv2AttributeTypesEnum attributeType, int port, IPAddress address) 
            : base(attributeType, null)
        {
            Port = port;
            Address = address;

            base.AttributeType = attributeType;
            //base.Length = ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((UInt16)base.AttributeType)), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(ADDRESS_ATTRIBUTE_LENGTH)), 0, buffer, startIndex + 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)base.AttributeType), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(ADDRESS_ATTRIBUTE_LENGTH), 0, buffer, startIndex + 2, 2);
            }

            buffer[startIndex + 5] = (byte)Family;

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(Convert.ToUInt16(Port))), 0, buffer, startIndex + 6, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt16(Port)), 0, buffer, startIndex + 6, 2);
            }
            Buffer.BlockCopy(Address.GetAddressBytes(), 0, buffer, startIndex + 8, 4);

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUNv2 Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }
    }
}
