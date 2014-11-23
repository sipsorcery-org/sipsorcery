//-----------------------------------------------------------------------------
// Filename: STUNMessage.cs
//
// Description: Implements STUN Message as defined in RFC3489.
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
    public class STUNMessage
	{
        private static ILog logger = STUNAppState.logger;
        
        public STUNHeader Header = new STUNHeader();
        public List<STUNAttribute> Attributes = new List<STUNAttribute>();

        public STUNMessage()
        { }

        public STUNMessage(STUNMessageTypesEnum stunMessageType)
        {
            Header = new STUNHeader(stunMessageType);
        }

        public static STUNMessage ParseSTUNMessage(byte[] buffer, int bufferLength)
        {
            if (buffer != null && buffer.Length > 0 && buffer.Length >= bufferLength)
            {
                STUNMessage stunMessage = new STUNMessage();
                stunMessage.Header = STUNHeader.ParseSTUNHeader(buffer);

                if (stunMessage.Header.MessageLength > 0)
                {
                    stunMessage.Attributes = STUNAttribute.ParseMessageAttributes(buffer, STUNHeader.STUN_HEADER_LENGTH, bufferLength);
                }

                return stunMessage;
            }

            return null;
        }

        public byte[] ToByteBuffer()
        {
            UInt16 attributesLength = 0;
            foreach (STUNAttribute attribute in Attributes)
            {
                attributesLength += Convert.ToUInt16(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + attribute.Length);
            }

            int messageLength = STUNHeader.STUN_HEADER_LENGTH + attributesLength;
            byte[] buffer = new byte[messageLength];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((UInt16)Header.MessageType)), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(attributesLength)), 0, buffer, 2, 2); 
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)Header.MessageType), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
            }

            Buffer.BlockCopy(Header.TransactionId, 0, buffer, 4, STUNHeader.TRANSACTION_ID_LENGTH);

            int attributeIndex = 20;
            foreach (STUNAttribute attr in Attributes)
            {
                attributeIndex += attr.ToByteBuffer(buffer, attributeIndex);
            }

            return buffer;
        }

        public new string ToString()
        {
            string messageDescr = "STUN Message: " + Header.MessageType.ToString() + ", length=" + Header.MessageLength;

            foreach (STUNAttribute attribute in Attributes)
            {
                messageDescr += "\n " + attribute.ToString();
            }

            return messageDescr;
        }

        public void AddUsernameAttribute(string username)
        {
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, usernameBytes));
        }
        
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class STUNMessageUnitTest
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
}
