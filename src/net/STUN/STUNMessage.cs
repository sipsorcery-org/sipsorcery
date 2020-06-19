//-----------------------------------------------------------------------------
// Filename: STUNMessage.cs
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
    public class STUNMessage
    {
        private const int FINGERPRINT_XOR = 0x5354554e;
        private const int MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH = 20;
        private const int FINGERPRINT_ATTRIBUTE_CRC32_LENGTH = 4;

        private static ILogger logger = Log.Logger;

        public STUNHeader Header = new STUNHeader();
        public List<STUNAttribute> Attributes = new List<STUNAttribute>();

        public STUNMessage()
        { }

        public STUNMessage(STUNMessageTypesEnum stunMessageType)
        {
            Header = new STUNHeader(stunMessageType);
        }

        public void AddUsernameAttribute(string username)
        {
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, usernameBytes));
        }

        public void AddNonceAttribute(string nonce)
        {
            byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);
            Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, nonceBytes));
        }

        public void AddXORMappedAddressAttribute(IPAddress remoteAddress, int remotePort)
        {
            STUNXORAddressAttribute xorAddressAttribute = new STUNXORAddressAttribute(STUNAttributeTypesEnum.XORMappedAddress, remotePort, remoteAddress);
            Attributes.Add(xorAddressAttribute);
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

        public byte[] ToByteBufferStringKey(string messageIntegrityKey, bool addFingerprint)
        {
            return ToByteBuffer(messageIntegrityKey.NotNullOrBlank() ? System.Text.Encoding.UTF8.GetBytes(messageIntegrityKey) : null, addFingerprint);
        }

        public byte[] ToByteBuffer(byte[] messageIntegrityKey, bool addFingerprint)
        {
            UInt16 attributesLength = 0;
            foreach (STUNAttribute attribute in Attributes)
            {
                attributesLength += Convert.ToUInt16(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + attribute.PaddedLength);
            }

            if (messageIntegrityKey != null)
            {
                attributesLength += STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH;
            }

            int messageLength = STUNHeader.STUN_HEADER_LENGTH + attributesLength;

            byte[] buffer = new byte[messageLength];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((UInt16)Header.MessageType)), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(attributesLength)), 0, buffer, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(STUNHeader.MAGIC_COOKIE)), 0, buffer, 4, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)Header.MessageType), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(STUNHeader.MAGIC_COOKIE), 0, buffer, 4, 4);
            }

            Buffer.BlockCopy(Header.TransactionId, 0, buffer, 8, STUNHeader.TRANSACTION_ID_LENGTH);

            int attributeIndex = 20;
            foreach (STUNAttribute attr in Attributes)
            {
                attributeIndex += attr.ToByteBuffer(buffer, attributeIndex);
            }

            //logger.LogDebug($"Pre HMAC STUN message: {ByteBufferInfo.HexStr(buffer, attributeIndex)}");

            if (messageIntegrityKey != null)
            {
                var integrityAttibtue = new STUNAttribute(STUNAttributeTypesEnum.MessageIntegrity, new byte[MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH]);

                HMACSHA1 hmacSHA = new HMACSHA1(messageIntegrityKey, true);
                byte[] hmac = hmacSHA.ComputeHash(buffer, 0, attributeIndex);

                integrityAttibtue.Value = hmac;
                attributeIndex += integrityAttibtue.ToByteBuffer(buffer, attributeIndex);
            }

            if (addFingerprint)
            {
                // The fingerprint attribute length has not been included in the length in the STUN header so adjust it now.
                attributesLength += STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH;
                messageLength += STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH;

                if (BitConverter.IsLittleEndian)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(attributesLength)), 0, buffer, 2, 2);
                }
                else
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
                }

                var fingerprintAttribute = new STUNAttribute(STUNAttributeTypesEnum.FingerPrint, new byte[FINGERPRINT_ATTRIBUTE_CRC32_LENGTH]);
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

            foreach (STUNAttribute attribute in Attributes)
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
