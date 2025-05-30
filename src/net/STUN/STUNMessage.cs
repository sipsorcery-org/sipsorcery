//-----------------------------------------------------------------------------
// Filename: STUNMessage.cs
//
// Description: Implements STUN Message as defined in RFC5389.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2010	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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

        /// <summary>
        /// For parsed STUN messages this indicates whether a valid fingerprint
        /// as attached to the message.
        /// </summary>
        public bool isFingerprintValid { get; private set; } = false;

        /// <summary>
        /// For received STUN messages this is the raw buffer.
        /// </summary>
        private byte[] _receivedBuffer;

        public STUNHeader? Header = new STUNHeader();
        public List<STUNAttribute>? Attributes = new List<STUNAttribute>();

        public ushort PaddedSize
        {
            get
            {
                return (ushort)(STUNHeader.STUN_HEADER_LENGTH + Header.MessageLength);
            }
        }

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
            AddXORAddressAttribute(STUNAttributeTypesEnum.XORMappedAddress, remoteAddress, remotePort);
        }

        public void AddXORPeerAddressAttribute(IPAddress remoteAddress, int remotePort)
        {
            AddXORAddressAttribute(STUNAttributeTypesEnum.XORPeerAddress, remoteAddress, remotePort);
        }

        public void AddXORAddressAttribute(STUNAttributeTypesEnum addressType, IPAddress remoteAddress, int remotePort)
        {
            STUNXORAddressAttribute xorAddressAttribute = new STUNXORAddressAttribute(addressType, remotePort, remoteAddress, Header.TransactionId);
            Attributes.Add(xorAddressAttribute);
        }

        [Obsolete("Use ParseSTUNMessage(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static STUNMessage? ParseSTUNMessage(byte[] buffer, int bufferLength)
        {
            if (buffer != null && buffer.Length >= bufferLength)
            {
                return ParseSTUNMessage(buffer.AsSpan(0, bufferLength));
            }

            return null;
        }

        public static STUNMessage? ParseSTUNMessage(ReadOnlySpan<byte> buffer)
        {
            if (!buffer.IsEmpty)
            {
                var stunMessage = new STUNMessage();
                stunMessage._receivedBuffer = buffer.ToArray();
                stunMessage.Header = STUNHeader.ParseSTUNHeader(buffer);

                if (stunMessage.Header is { MessageLength: > 0 })
                {
                    stunMessage.Attributes = STUNAttribute.ParseMessageAttributes(buffer.Slice(STUNHeader.STUN_HEADER_LENGTH), stunMessage.Header);

                    if (stunMessage.Attributes is { Count: > 0 } &&
                        stunMessage.Attributes[stunMessage.Attributes.Count - 1] is { AttributeType: STUNAttributeTypesEnum.FingerPrint } fingerprintAttribute)
                    {
                        // Check fingerprint.

                        var input = buffer.Slice(0, buffer.Length - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);

                        var crc = Crc32.Compute(input) ^ FINGERPRINT_XOR;
                        var fingerprint = BinaryPrimitives.ReadUInt32BigEndian(fingerprintAttribute.Value);

                        if (crc == fingerprint)
                        {
                            stunMessage.isFingerprintValid = true;
                        }
                    }
                }

                return stunMessage;
            }

            return null;
        }

        [Obsolete("Use WriteToBufferStringKey(Span<byte>, string, bool) in conjunction with GetByteBufferSizeStringKey(string, bool) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] ToByteBufferStringKey(string messageIntegrityKey, bool addFingerprint)
        {
            var bufferSize = GetByteBufferSizeStringKey(messageIntegrityKey, addFingerprint);
            var buffer = new byte[bufferSize];
            WriteToBufferStringKey(buffer, messageIntegrityKey, addFingerprint);
            return buffer;
        }

        public int GetByteBufferSizeStringKey(string messageIntegrityKey, bool addFingerprint)
        {
            if (string.IsNullOrWhiteSpace(messageIntegrityKey))
            {
                return GetByteBufferSize(ReadOnlySpan<byte>.Empty, addFingerprint);
            }

            var maxByteCount = Encoding.UTF8.GetMaxByteCount(messageIntegrityKey.Length);
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);

            try
            {
                var actualByteCount = Encoding.UTF8.GetBytes(messageIntegrityKey.AsSpan(), rentedBuffer);
                var keySpan = new ReadOnlySpan<byte>(rentedBuffer, 0, actualByteCount);
                return GetByteBufferSize(keySpan, addFingerprint);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        public void WriteToBufferStringKey(Span<byte> destination, string messageIntegrityKey, bool addFingerprint)
        {
            ReadOnlySpan<byte> keySpan;

            if (messageIntegrityKey.NotNullOrBlank())
            {
                var maxByteCount = Encoding.UTF8.GetMaxByteCount(messageIntegrityKey.Length);
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);

                try
                {
                    var actualByteCount = Encoding.UTF8.GetBytes(messageIntegrityKey.AsSpan(), rentedBuffer);
                    keySpan = new ReadOnlySpan<byte>(rentedBuffer, 0, actualByteCount);

                    WriteToBuffer(destination, keySpan, addFingerprint);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
            else
            {
                WriteToBuffer(destination, ReadOnlySpan<byte>.Empty, addFingerprint);
            }
        }

        [Obsolete("Use WriteToBuffer(Span<byte>, ReadOnlySpan<byte>, bool) in conjunction with GetByteBufferSize(ReadOnlySpan<byte>, bool) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] ToByteBuffer(byte[] messageIntegrityKey, bool addFingerprint)
        {
            var messageIntegrityKeySpan = messageIntegrityKey.AsSpan();
            var result = new byte[GetByteBufferSize(messageIntegrityKeySpan, addFingerprint)];
            WriteToBuffer(result.AsSpan(), messageIntegrityKeySpan, addFingerprint);
            return result;
        }

        public int GetByteBufferSize(ReadOnlySpan<byte> messageIntegrityKey, bool addFingerprint)
        {
            var attributesLength = 0;

            foreach (var attribute in Attributes)
            {
                attributesLength += (ushort)(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + attribute.PaddedLength);
            }

            if (!messageIntegrityKey.IsEmpty)
            {
                attributesLength += (ushort)(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH);
            }

            if (addFingerprint)
            {
                attributesLength += (ushort)(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);
            }

            return STUNHeader.STUN_HEADER_LENGTH + attributesLength;
        }

        public void WriteToBuffer(Span<byte> buffer, ReadOnlySpan<byte> messageIntegrityKey, bool addFingerprint)
        {
            var attributesLength = (ushort)(
                GetByteBufferSize(messageIntegrityKey, addFingerprint)
                - STUNHeader.STUN_HEADER_LENGTH
                - (addFingerprint ? STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH : 0)
            );

            // Write STUN header
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0, 2), (ushort)Header.MessageType);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), attributesLength);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4, 4), STUNHeader.MAGIC_COOKIE);
            Header.TransactionId.CopyTo(buffer.Slice(8, STUNHeader.TRANSACTION_ID_LENGTH));

            var attributeIndex = 20;
            foreach (var attr in Attributes)
            {
                attributeIndex += attr.WriteBytes(buffer.Slice(attributeIndex)); // Assuming ToByteBuffer still uses byte[]
            }

            if (!messageIntegrityKey.IsEmpty)
            {
                var integrityAttribute = new STUNAttribute(STUNAttributeTypesEnum.MessageIntegrity, new byte[MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH]);
                using var hmacSHA = new HMACSHA1(messageIntegrityKey.ToArray());
                var hmac = hmacSHA.ComputeHash(buffer.Slice(0, attributeIndex));
                integrityAttribute.Value = hmac;
                attributeIndex += integrityAttribute.WriteBytes(buffer.Slice(attributeIndex));
            }

            if (addFingerprint)
            {
                // The fingerprint attribute length has not been included in the length in the STUN header so adjust it now.
                BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), attributesLength += STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);

                var crc = Crc32.Compute(buffer.Slice(0, buffer.Length - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH)) ^ FINGERPRINT_XOR;
                var fingerprint = new byte[FINGERPRINT_ATTRIBUTE_CRC32_LENGTH];
                BinaryPrimitives.WriteUInt32BigEndian(fingerprint, crc);

                var fingerprintAttribute = new STUNAttribute(STUNAttributeTypesEnum.FingerPrint, fingerprint);
                fingerprintAttribute.WriteBytes(buffer.Slice(attributeIndex));
            }
        }

        public override string ToString()
        {
            var sb = new ValueStringBuilder(stackalloc char[256]);

            try
            {
                ToString(ref sb);

                return sb.ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        internal void ToString(ref ValueStringBuilder sb)
        {
            Debug.Assert(Header is not null);
            Debug.Assert(Attributes is not null);

            sb.Append("STUN Message: ");
            sb.Append(Header.MessageType.ToString());
            sb.Append('[');
            sb.Append((int)Header.MessageType);
            sb.Append("], length=");
            sb.Append(Header.MessageLength);
            sb.Append(", transactionID=");
            sb.Append(Header.TransactionId);

            foreach (var attribute in Attributes)
            {
                sb.Append("\n ");
                attribute.ToString(ref sb);
            }
        }

        /// <summary>
        /// Check that the message integrity attribute is correct.
        /// </summary>
        /// <param name="messageIntegrityKey">The message integrity key that was used to generate
        /// the HMAC for the original message.</param>
        /// <returns>True if the fingerprint and HMAC of the STUN message are valid. False if not.</returns>
        public bool CheckIntegrity(byte[] messageIntegrityKey)
        {
            bool isHmacValid = false;

            if (isFingerprintValid)
            {
                if (Attributes.Count > 2 && Attributes[Attributes.Count - 2].AttributeType == STUNAttributeTypesEnum.MessageIntegrity)
                {
                    var messageIntegrityAttribute = Attributes[Attributes.Count - 2];

                    int preImageLength = _receivedBuffer.Length
                        - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH * 2
                        - MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH
                        - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH;

                    // Need to adjust the STUN message length field for to remove the fingerprint.
                    ushort length = (ushort)(Header.MessageLength - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);
                    BinaryPrimitives.WriteUInt16BigEndian(_receivedBuffer.AsSpan(2, 2), length);

                    HMACSHA1 hmacSHA = new HMACSHA1(messageIntegrityKey);
                    byte[] calculatedHmac = hmacSHA.ComputeHash(_receivedBuffer, 0, preImageLength);

                    //logger.LogDebug($"Received Message integrity HMAC  : {messageIntegrityAttribute.Value.HexStr()}.");
                    //logger.LogDebug($"Calculated Message integrity HMAC: {calculatedHmac.HexStr()}.");

                    isHmacValid = messageIntegrityAttribute.Value.SequenceEqual(calculatedHmac);
                }
            }

            return isHmacValid;
        }
    }
}
