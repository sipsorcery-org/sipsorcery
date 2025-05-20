//-----------------------------------------------------------------------------
// Filename: STUNErrorCodeAttribute.cs
//
// Description: Implements STUN error attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Feb 2016	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public partial class STUNErrorCodeAttribute : STUNAttribute
{
    public byte ErrorClass;             // The hundreds value of the error code must be between 3 and 6.
    public byte ErrorNumber;            // The units value of the error code must be between 0 and 99.
    public string ReasonPhrase;

    public int ErrorCode
    {
        get
        {
            return ErrorClass * 100 + ErrorNumber;
        }
    }

    public STUNErrorCodeAttribute(byte[] attributeValue)
        : base(STUNAttributeTypesEnum.ErrorCode, attributeValue)
    {
        ErrorClass = attributeValue[2];
        ErrorNumber = attributeValue[3];
        ReasonPhrase = Encoding.UTF8.GetString(attributeValue, 4, attributeValue.Length - 4);
    }

    public STUNErrorCodeAttribute(int errorCode, string reasonPhrase)
            : base(STUNAttributeTypesEnum.ErrorCode, BuildValue(errorCode, reasonPhrase))
    {
        ErrorClass = (byte)(errorCode / 100);
        ErrorNumber = (byte)(errorCode % 100);
        ReasonPhrase = reasonPhrase;
    }

    private static byte[] BuildValue(int errorCode, string reasonPhrase)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(reasonPhrase ?? string.Empty);
        var value = new byte[4 + reasonBytes.Length];
        value[2] = (byte)(errorCode / 100);
        value[3] = (byte)(errorCode % 100);
        Buffer.BlockCopy(reasonBytes, 0, value, 4, reasonBytes.Length);
        return value;
    }

    /// <inheritdoc/>
    public override int GetByteCount()
    {
        var reasonBytesLen = string.IsNullOrEmpty(ReasonPhrase) ? 0 : Encoding.UTF8.GetByteCount(ReasonPhrase);
        var valueLen = 4 + reasonBytesLen; // 2 reserved + class + number + reason
        var paddedValueLen = (valueLen % 4 == 0) ? valueLen : valueLen + (4 - (valueLen % 4));
        return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + paddedValueLen;
    }

    /// <inheritdoc/>
    public override int WriteBytes(Span<byte> buffer)
    {
        buffer[0] = 0x00;
        buffer[1] = 0x00;
        buffer[2] = ErrorClass;
        buffer[3] = ErrorNumber;

        var reasonPhraseBytes = Encoding.UTF8.GetBytes(ReasonPhrase);
        reasonPhraseBytes.CopyTo(buffer.Slice(4, reasonPhraseBytes.Length));

        return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + 4 + reasonPhraseBytes.Length;
    }

    private protected override void ValueToString(ref ValueStringBuilder sb)
    {
        sb.Append("error code=");
        sb.Append(ErrorCode);
        sb.Append(", reason phrase=");
        sb.Append(ReasonPhrase);
    }
}
