//-----------------------------------------------------------------------------
// Filename: STUNv2Message.cs
//
// Description: Implements STUN Message as defined in RFC5389.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Nov 2010	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNv2Message
    {
        private const int FINGERPRINT_XOR = 0x5354554e;
        private const int MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH = 20;
        private const int FINGERPRINT_ATTRIBUTE_CRC32_LENGTH = 4;

        private static ILogger logger = Log.Logger;

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

            if (messageIntegrityKey != null)
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

            //logger.LogDebug($"Pre HMAC STUN message: {ByteBufferInfo.HexStr(buffer, attributeIndex)}");

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

        /// <summary>
        /// Check that the message integrity attribute is correct.
        /// </summary>
        /// <param name="messageIntegrityKey"></param>
        /// <param name="localUser"></param>
        /// <param name="remoteUser"></param>
        /// <returns></returns>
        public bool CheckIntegrity(byte[] messageIntegrityKey, string localUser, string remoteUser)
        {
            // TODO.

            return true;
        }
    }
}
