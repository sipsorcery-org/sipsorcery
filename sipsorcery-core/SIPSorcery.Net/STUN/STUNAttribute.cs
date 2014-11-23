//-----------------------------------------------------------------------------
// Filename: STUNAttribute.cs
//
// Description: Implements STUN message attributes as defined in RFC3489.
//
//  0                   1                   2                   3
//  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  |         Type                  |            Length             |
//  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  |                             Value                             ....
//  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// History:
// 27 Dec 2006	Aaron Clauson	Created.
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

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
    public enum STUNAttributeTypesEnum
    {
        Unknown = 0,
        MappedAddress = 1,
        ResponseAddress = 2,
        ChangeRequest = 3,
        SourceAddress = 4,
        ChangedAddress = 5,
        Username = 6,
        Password = 7,
        MessageIntegrity = 8,
        ErrorCode = 9,
        UnknownAttributes = 10,
        ReflectedFrom = 11,
        KeepAliveSocket = 12,
    }

    public class STUNAttributeTypes
    {
        public static STUNAttributeTypesEnum GetSTUNAttributeTypeForId(int stunAttributeTypeId)
        {
            return (STUNAttributeTypesEnum)Enum.Parse(typeof(STUNAttributeTypesEnum), stunAttributeTypeId.ToString(), true);
        }
    }
    
    public class STUNAttribute
	{
        public const short STUNATTRIBUTE_HEADER_LENGTH = 4;
        
        public STUNAttributeTypesEnum AttributeType = STUNAttributeTypesEnum.Unknown;
        public UInt16 Length;
        public byte[] Value;

        public STUNAttribute(STUNAttributeTypesEnum attributeType, byte[] value)
        {
            AttributeType = attributeType;
            Value = value;
            Length = Convert.ToUInt16(Value.Length);
        }

        public STUNAttribute(STUNAttributeTypesEnum attributeType, UInt16 length, byte[] value)
        {
            AttributeType = attributeType;
            Length = length;
            Value = value;
        }

        public static List<STUNAttribute> ParseMessageAttributes(byte[] buffer, int startIndex, int endIndex)
        {
            if (buffer != null && buffer.Length > startIndex && buffer.Length >= endIndex)
            {
                List<STUNAttribute> attributes = new List<STUNAttribute>();   
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
                        if (stunAttributeType == (int)STUNAttributeTypesEnum.Username && stunAttributeLength > buffer.Length - startIndex - 4)
                        {
                            // Received some STUN messages where the username is shorter than the claimed length.
                            int realLength = buffer.Length - startIndex - 4;
                            stunAttributeValue = new byte[realLength];
                            Buffer.BlockCopy(buffer, startIndex + 4, stunAttributeValue, 0, realLength);
                        }
                        else
                        {
                            stunAttributeValue = new byte[stunAttributeLength];
                            Buffer.BlockCopy(buffer, startIndex + 4, stunAttributeValue, 0, stunAttributeLength);
                        }
                    }

                    STUNAttributeTypesEnum attributeType = STUNAttributeTypes.GetSTUNAttributeTypeForId(stunAttributeType);

                    STUNAttribute attribute = null;
                    if (attributeType == STUNAttributeTypesEnum.ChangeRequest)
                    {
                        attribute = new STUNChangeRequestAttribute(stunAttributeValue);
                    }
                    else if (attributeType == STUNAttributeTypesEnum.MappedAddress)
                    {
                        attribute = new STUNAddressAttribute(stunAttributeValue);
                    }
                    else
                    {
                        attribute = new STUNAttribute(attributeType, stunAttributeLength, stunAttributeValue);
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

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + Length;
        }

        public new virtual string ToString()
        {
            string attrDescrString = "STUN Attribute: " + AttributeType.ToString() + ", length=" + Length + ".";

            return attrDescrString;
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class STUNMessageAttributeUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			
			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}
		}

		#endif

		#endregion
	}

    /// <summary>
    /// MAPPED-ADDRESS, RESPONSE-ADDRESS, CHANGED-ADDRESS, SOURCE-ADDRESS attributes use this format.
    /// The Send Keep Alive STUN request also uses an attribute with this format to specify the socket to send
    /// the Keep Alive to.
    /// 
    ///     0                   1                   2                   3
    /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |x x x x x x x x|    Family     |           Port                |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |                             Address                           |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///
    /// </summary>
    public class STUNAddressAttribute : STUNAttribute
    {
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;
        public const byte FAMILY = 0x01;

        public int Port;
        public IPAddress Address;

        public STUNAddressAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.MappedAddress, ADDRESS_ATTRIBUTE_LENGTH, attributeValue)
        {
            Port = BitConverter.ToInt16(attributeValue, 2);
            Address = new IPAddress(new byte[] { attributeValue[4], attributeValue[5], attributeValue[6], attributeValue[7] });
        }

        public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address) 
            : base(attributeType, ADDRESS_ATTRIBUTE_LENGTH, null)
        {
            Port = port;
            Address = address;

            base.AttributeType = attributeType;
            base.Length = ADDRESS_ATTRIBUTE_LENGTH;
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

            buffer[startIndex + 5] = FAMILY;

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(Convert.ToUInt16(Port))), 0, buffer, startIndex + 6, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt16(Port)), 0, buffer, startIndex + 6, 2);
            }
            Buffer.BlockCopy(Address.GetAddressBytes(), 0, buffer, startIndex + 8, 4);

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }
    }

    /// <summary>
    ///  0                   1                   2                   3
    /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 A B 0|
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///
    /// The meaning of the flags is:
    ///
    /// A: This is the "change IP" flag.  If true, it requests the server
    ///  to send the Binding Response with a different IP address than the
    ///  one the Binding Request was received on.
    ///
    /// B: This is the "change port" flag.  If true, it requests the
    ///  server to send the Binding Response with a different port than the
    ///  one the Binding Request was received on.
    ///
    /// </summary>
    public class STUNChangeRequestAttribute : STUNAttribute
    {
        public const UInt16 CHANGEREQUEST_ATTRIBUTE_LENGTH = 4;
        
        public bool ChangeAddress = false;
        public bool ChangePort = false;

        private byte m_changeRequestByte;

        public STUNChangeRequestAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.ChangeRequest, CHANGEREQUEST_ATTRIBUTE_LENGTH, attributeValue)
        {
            m_changeRequestByte = attributeValue[3];

            if (m_changeRequestByte == 0x02)
            {
                ChangePort = true;
            }
            else if (m_changeRequestByte == 0x04)
            {
                ChangeAddress = true;
            }
            else if (m_changeRequestByte == 0x06)
            {
                ChangePort = true;
                ChangeAddress = true;
            }
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN Attribute: " + STUNAttributeTypesEnum.ChangeRequest.ToString() + ", key byte=" + m_changeRequestByte.ToString("X") + ", change address=" + ChangeAddress + ", change port=" + ChangePort + ".";

            return attrDescrStr;
        }
    }
}
