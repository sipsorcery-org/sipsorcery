//-----------------------------------------------------------------------------
// Filename: STUNv2Message.cs
//
// Description: Implements STUN Message as defined in RFC5389.
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
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class STUNv2Message
	{
        private const int FINGERPRINT_XOR = 0x5354554e;
        private const int MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH = 20;
        private const int FINGERPRINT_ATTRIBUTE_CRC32_LENGTH = 4;

        private static ILog logger = STUNAppState.logger;
        
        public STUNv2Header Header = new STUNv2Header();
        public List<STUNv2Attribute> Attributes = new List<STUNv2Attribute>();

        public STUNv2Message()
        { }

        public STUNv2Message(STUNv2MessageTypesEnum stunMessageType)
        {
            Header = new STUNv2Header(stunMessageType);
        }

        public void AddUsernameAttribute(string username)
        {
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, usernameBytes));
        }

        //public void AddMessageIntegrityAttribute(string key)
        //{
        //    //MD5 md5 = new MD5CryptoServiceProvider();
        //    //byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        //    //HMACSHA1 hmacSHA = new HMACSHA1(hmacKey, true);
        //    //byte[] hmac = hmacSHA.ComputeHash(ToByteBuffer());
        //    //if (BitConverter.IsLittleEndian)
        //    //{
        //    //    Array.Reverse(hmac);
        //    //}
        //    Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.MessageIntegrity, new byte[MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH]));
        //}

        //public void AddFingerPrintAttribute()
        //{
        //    //byte[] messageBytes = ToByteBuffer();
        //    //uint crc = Crc32.Compute(messageBytes) ^ FINGERPRINT_XOR;
        //    //byte[] fingerPrint = (BitConverter.IsLittleEndian) ? BitConverter.GetBytes(NetConvert.DoReverseEndian(crc)) : BitConverter.GetBytes(crc);
        //    Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.FingerPrint, new byte[FINGERPRINT_ATTRIBUTE_CRC32_LENGTH]));
        //}

        public void AddXORMappedAddressAttribute(IPAddress remoteAddress, int remotePort)
        {
            STUNv2XORAddressAttribute xorAddressAttribute = new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORMappedAddress, remotePort, remoteAddress);
            Attributes.Add(xorAddressAttribute);
        }

        public static STUNv2Message ParseSTUNMessage(byte[] buffer, int bufferLength)
        {
            if (buffer != null && buffer.Length > 0 && buffer.Length >= bufferLength)
            {
                STUNv2Message stunMessage = new STUNv2Message();
                stunMessage.Header = STUNv2Header.ParseSTUNHeader(buffer);

                if (stunMessage.Header.MessageLength > 0)
                {
                    stunMessage.Attributes = STUNv2Attribute.ParseMessageAttributes(buffer, STUNv2Header.STUN_HEADER_LENGTH, bufferLength);
                }

                return stunMessage;
            }

            return null;
        }

        public byte[] ToByteBufferStringKey(string messageIntegrityKey, bool addFingerprint)
        {
            return ToByteBuffer(messageIntegrityKey.NotNullOrBlank() ? System.Text.Encoding.UTF8.GetBytes(messageIntegrityKey) : null, addFingerprint);
        }

        public byte[] ToByteBuffer(byte[] messageIntegrityKey, bool addFingerprint)
        {
            UInt16 attributesLength = 0;
            foreach (STUNv2Attribute attribute in Attributes)
            {
                attributesLength += Convert.ToUInt16(STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + attribute.PaddedLength);
            }

            if(messageIntegrityKey != null)
            {
                attributesLength += STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH;
            }

            int messageLength = STUNv2Header.STUN_HEADER_LENGTH + attributesLength;

            byte[] buffer = new byte[messageLength];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((UInt16)Header.MessageType)), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(attributesLength)), 0, buffer, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(STUNv2Header.MAGIC_COOKIE)), 0, buffer, 4, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)Header.MessageType), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(STUNv2Header.MAGIC_COOKIE), 0, buffer, 4, 4);
            }

            Buffer.BlockCopy(Header.TransactionId, 0, buffer, 8, STUNv2Header.TRANSACTION_ID_LENGTH);

            int attributeIndex = 20;
            foreach (STUNv2Attribute attr in Attributes)
            {
                attributeIndex += attr.ToByteBuffer(buffer, attributeIndex);
            }

            if (messageIntegrityKey != null)
            {
                var integrityAttibtue = new STUNv2Attribute(STUNv2AttributeTypesEnum.MessageIntegrity, new byte[MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH]);

                HMACSHA1 hmacSHA = new HMACSHA1(messageIntegrityKey, true);
                byte[] hmac = hmacSHA.ComputeHash(buffer, 0, attributeIndex);
               
                integrityAttibtue.Value = hmac;
                attributeIndex += integrityAttibtue.ToByteBuffer(buffer, attributeIndex);
            }

            if (addFingerprint)
            {
                // The fingerprint attribute length has not been included in the length in the STUN header so adjust it now.
                attributesLength += STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH;
                messageLength += STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH;

                if (BitConverter.IsLittleEndian)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(attributesLength)), 0, buffer, 2, 2);
                }
                else
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
                }

                var fingerprintAttribute = new STUNv2Attribute(STUNv2AttributeTypesEnum.FingerPrint, new byte[FINGERPRINT_ATTRIBUTE_CRC32_LENGTH]);
                uint crc = Crc32.Compute(buffer) ^ FINGERPRINT_XOR;
                byte[] fingerPrint = (BitConverter.IsLittleEndian) ? BitConverter.GetBytes(NetConvert.DoReverseEndian(crc)) : BitConverter.GetBytes(crc);
                fingerprintAttribute.Value = fingerPrint;

                Array.Resize(ref buffer, messageLength);
                fingerprintAttribute.ToByteBuffer(buffer, attributeIndex);
            }

            return buffer;
        }

        public new string ToString()
        {
            string messageDescr = "STUN Message: " + Header.MessageType.ToString() + ", length=" + Header.MessageLength;

            foreach (STUNv2Attribute attribute in Attributes)
            {
                messageDescr += "\n " + attribute.ToString();
            }

            return messageDescr;
        }
	}
}
