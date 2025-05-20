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
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public partial class STUNMessage
{
    private const int FINGERPRINT_XOR = 0x5354554e;
    private const int MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH = 20;
    private const int FINGERPRINT_ATTRIBUTE_CRC32_LENGTH = 4;

    private static ILogger logger = Log.Logger;

    /// <summary>
    /// For parsed STUN messages this indicates whether a valid fingerprint
    /// as attached to the message.
    /// </summary>
    public bool isFingerprintValid { get; private set; }

    /// <summary>
    /// For received STUN messages this is the raw buffer.
    /// </summary>
    private Memory<byte> _receivedBuffer;

    public STUNHeader Header { get; }
    public List<STUNAttribute> Attributes { get; private set; } = new List<STUNAttribute>();

    public ushort PaddedSize
    {
        get
        {
            Debug.Assert(Header is { });
            return (ushort)(STUNHeader.STUN_HEADER_LENGTH + Header.MessageLength);
        }
    }

    public STUNMessage(STUNHeader header)
    {
        Header = header;
    }

    public STUNMessage(STUNMessageTypesEnum stunMessageType)
        : this(new STUNHeader(stunMessageType))
    {
    }

    public void AddUsernameAttribute(string username)
    {
        var usernameBytes = Encoding.UTF8.GetBytes(username);
        Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, usernameBytes.AsMemory()));
    }

    public void AddNonceAttribute(string nonce)
    {
        var nonceBytes = Encoding.UTF8.GetBytes(nonce);
        Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, nonceBytes.AsMemory()));
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
        var xorAddressAttribute = new STUNXORAddressAttribute(addressType, remotePort, remoteAddress, Header.TransactionId);
        Attributes.Add(xorAddressAttribute);
    }

    public static STUNMessage? ParseSTUNMessage(ReadOnlySpan<byte> buffer)
    {
        if (!buffer.IsEmpty)
        {
            var header = STUNHeader.ParseSTUNHeader(buffer);
            Debug.Assert(header is { });
            var stunMessage = new STUNMessage(header);
            stunMessage._receivedBuffer = buffer.ToArray();

            if (stunMessage.Header is { MessageLength: > 0 })
            {
                STUNAttribute.ParseMessageAttributes(buffer.Slice(STUNHeader.STUN_HEADER_LENGTH), stunMessage.Header, stunMessage.Attributes);

                if (stunMessage.Attributes is { Count: > 0 } &&
                    stunMessage.Attributes[stunMessage.Attributes.Count - 1] is { AttributeType: STUNAttributeTypesEnum.FingerPrint } fingerprintAttribute)
                {
                    // Check fingerprint.

                    var input = buffer.Slice(0, buffer.Length - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);

                    var crc = Crc32.Compute(input) ^ FINGERPRINT_XOR;
                    var fingerprint = BinaryPrimitives.ReadUInt32BigEndian(fingerprintAttribute.Value.Span);

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

    public int GetByteBufferSizeStringKey(string? messageIntegrityKey, bool addFingerprint)
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

    public void WriteToBufferStringKey(Span<byte> destination, string? messageIntegrityKey, bool addFingerprint)
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
            attributeIndex += attr.WriteBytes(buffer.Slice(attributeIndex));
        }

        if (!messageIntegrityKey.IsEmpty)
        {
            using var hmacSHA = new HMACSHA1(messageIntegrityKey.ToArray());
            var message = buffer.Slice(0, attributeIndex);
            var hmac = hmacSHA.ComputeHash(message);
            var integrityAttribute = new STUNAttribute(STUNAttributeTypesEnum.MessageIntegrity, hmac.AsMemory());
            attributeIndex += integrityAttribute.WriteBytes(buffer.Slice(attributeIndex));
        }

        if (addFingerprint)
        {
            // The fingerprint attribute length has not been included in the length in the STUN header so adjust it now.
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), attributesLength += STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);

            var input = buffer.Slice(0, buffer.Length - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH);
            var crc = Crc32.Compute(input) ^ FINGERPRINT_XOR;
            var fingerprint = new byte[FINGERPRINT_ATTRIBUTE_CRC32_LENGTH];
            BinaryPrimitives.WriteUInt32BigEndian(fingerprint, crc);

            var fingerprintAttribute = new STUNAttribute(STUNAttributeTypesEnum.FingerPrint, fingerprint.AsMemory());
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
        Debug.Assert(Header is { });
        Debug.Assert(Attributes is { });

        sb.Append("STUN Message: ");
        sb.Append(Header.MessageType.ToStringFast());
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
        // Find the MESSAGE-INTEGRITY attribute. It can be either:
        // - The last attribute (no FINGERPRINT present)
        // - The second-to-last attribute (FINGERPRINT is last)
        STUNAttribute messageIntegrityAttribute;
        var hasFingerprint = false;

        if (Attributes.Count > 1 && Attributes[Attributes.Count - 1].AttributeType == STUNAttributeTypesEnum.MessageIntegrity)
        {
            messageIntegrityAttribute = Attributes[Attributes.Count - 1];
        }
        else if (Attributes.Count >= 2 && Attributes[Attributes.Count - 1].AttributeType == STUNAttributeTypesEnum.FingerPrint
            && Attributes[Attributes.Count - 2].AttributeType == STUNAttributeTypesEnum.MessageIntegrity)
        {
            messageIntegrityAttribute = Attributes[Attributes.Count - 2];
            hasFingerprint = true;
        }
        else
        {
            return false;
        }

        Debug.Assert(messageIntegrityAttribute is not null);

        if (_receivedBuffer.IsEmpty)
        {
            return false;
        }

        // When FINGERPRINT is present and valid, verify it first.
        if (hasFingerprint && !isFingerprintValid)
        {
            return false;
        }

        // Calculate the pre-image length: everything before the MESSAGE-INTEGRITY attribute.
        int fingerprintSize = hasFingerprint
            ? STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + FINGERPRINT_ATTRIBUTE_CRC32_LENGTH
            : 0;

        int preImageLength = _receivedBuffer.Length
            - fingerprintSize
            - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH
            - MESSAGE_INTEGRITY_ATTRIBUTE_HMAC_LENGTH;

        // Per RFC 5389 Section 15.4: the message length field must be adjusted to
        // include MESSAGE-INTEGRITY but exclude FINGERPRINT (if present).
        var length = hasFingerprint
            ? (ushort)(Header.MessageLength - STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH - FINGERPRINT_ATTRIBUTE_CRC32_LENGTH)
            : Header.MessageLength;

        BinaryPrimitives.WriteUInt16BigEndian(_receivedBuffer.Span.Slice(2, 2), length);

        HMACSHA1 hmacSHA = new HMACSHA1(messageIntegrityKey);
        byte[] calculatedHmac = hmacSHA.ComputeHash(_receivedBuffer.Slice(0, preImageLength));

        return messageIntegrityAttribute.Value.Span.SequenceEqual(calculatedHmac);
    }
}
