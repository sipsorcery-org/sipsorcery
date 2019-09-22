//-----------------------------------------------------------------------------
// Filename: STUNv2XORAddressAttribute.cs
//
// Description: Implements STUN XOR mapped address attribute as defined in RFC5389.
// 
// History:
// 15 Oct 2014	Aaron Clauson	Created.
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

namespace SIPSorcery.Net
{
    /// <summary>
    /// This attribue is the same as the mapped address attribute except the address details are XOR'ed with the STUN magic cookie. 
    /// THe reason for this is to stop NAT application layer gateways from doing string replacements of private IP adresses and ports.
    /// </summary>
    public class STUNv2XORAddressAttribute : STUNv2Attribute
    {
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;

        public int Family = 1;      // Ipv4 = 1, IPv6 = 2.
        public int Port;
        public IPAddress Address;

        public override UInt16 PaddedLength
        {
            get { return ADDRESS_ATTRIBUTE_LENGTH; }
        }

        public STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum attributeType, byte[] attributeValue)
            : base(attributeType, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                Port = Utility.ReverseEndian(BitConverter.ToUInt16(attributeValue, 2)) ^ (UInt16)(STUNv2Header.MAGIC_COOKIE >> 16);
                UInt32 address = Utility.ReverseEndian(BitConverter.ToUInt32(attributeValue, 4)) ^ STUNv2Header.MAGIC_COOKIE;
                Address = new IPAddress(Utility.ReverseEndian(address));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2) ^ (UInt16)(STUNv2Header.MAGIC_COOKIE >> 16);
                UInt32 address = BitConverter.ToUInt32(attributeValue, 4) ^ STUNv2Header.MAGIC_COOKIE;
                Address = new IPAddress(address);
            }
        }

        public STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum attributeType, int port, IPAddress address)
            : base(attributeType, null)
        {
            Port = port;
            Address = address;
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

            buffer[startIndex + 4] = 0x00;
            buffer[startIndex + 5] = (byte)Family;

            if (BitConverter.IsLittleEndian)
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNv2Header.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = Utility.ReverseEndian(BitConverter.ToUInt32(Address.GetAddressBytes(), 0)) ^ STUNv2Header.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(xorPort)), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(xorAddress)), 0, buffer, startIndex + 8, 4);
            }
            else
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNv2Header.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = BitConverter.ToUInt32(Address.GetAddressBytes(), 0) ^ STUNv2Header.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(xorPort), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(xorAddress), 0, buffer, startIndex + 8, 4);
            }

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUNv2 XOR_MAPPED_ADDRESS Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }
    }
}
