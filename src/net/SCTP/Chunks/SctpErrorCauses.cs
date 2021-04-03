//-----------------------------------------------------------------------------
// Filename: SctpErrorCauses.cs
//
// Description: Represents the SCTP error causes and the different representations
// for each error type.
//
// Remarks:
// Defined in section 3.3.10 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.10
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <remarks>
    /// Defined in https://tools.ietf.org/html/rfc4960#section-3.3.10
    /// </remarks>
    public enum SctpErrorCauseCode : ushort
    {
        InvalidStreamIdentifier = 1,
        MissingMandatoryParameter = 2,
        StaleCookieError = 3,
        OutOfResource = 4,
        UnresolvableAddress = 5,
        UnrecognizedChunkType = 6,
        InvalidMandatoryParameter = 7,
        UnrecognizedParameters = 8,
        NoUserData = 9,
        CookieReceivedWhileShuttingDown = 10,
        RestartAssociationWithNewAddress = 11,
        UserInitiatedAbort = 12,
        ProtocolViolation = 13
    }

    public interface ISctpErrorCause
    {
        SctpErrorCauseCode CauseCode { get; }
        ushort GetErrorCauseLength(bool padded);
        int WriteTo(byte[] buffer, int posn);
    }

    /// <summary>
    /// This structure captures all SCTP errors that don't have an additional 
    /// parameter.
    /// </summary>
    /// <remarks>
    /// Out of Resource: https://tools.ietf.org/html/rfc4960#section-3.3.10.4
    /// Invalid Mandatory Parameter: https://tools.ietf.org/html/rfc4960#section-3.3.10.7
    /// Cookie Received While Shutting Down: https://tools.ietf.org/html/rfc4960#section-3.3.10.10
    /// </remarks>
    public struct SctpCauseOnlyError : ISctpErrorCause
    {
        private const ushort ERROR_CAUSE_LENGTH = 4;

        public static readonly List<SctpErrorCauseCode> SupportedErrorCauses =
            new List<SctpErrorCauseCode>
            {
                SctpErrorCauseCode.OutOfResource,
                SctpErrorCauseCode.InvalidMandatoryParameter,
                SctpErrorCauseCode.CookieReceivedWhileShuttingDown
            };

        public SctpErrorCauseCode CauseCode { get; private set; }

        public SctpCauseOnlyError(SctpErrorCauseCode causeCode)
        {
            if (!SupportedErrorCauses.Contains(causeCode))
            {
                throw new ApplicationException($"SCTP error struct should not be used for {causeCode}, use the specific error type.");
            }

            CauseCode = causeCode;
        }

        public ushort GetErrorCauseLength(bool padded) => ERROR_CAUSE_LENGTH;

        public int WriteTo(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(ERROR_CAUSE_LENGTH, buffer, posn + 2);
            return ERROR_CAUSE_LENGTH;
        }
    }

    /// <summary>
    /// Invalid Stream Identifier: Indicates endpoint received a DATA chunk
    /// sent to a nonexistent stream.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.1
    /// </remarks>
    public struct SctpErrorInvalidStreamIdentifier : ISctpErrorCause
    {
        private const ushort ERROR_CAUSE_LENGTH = 8;

        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.InvalidStreamIdentifier;

        /// <summary>
        /// The invalid stream identifier.
        /// </summary>
        public ushort StreamID;

        public ushort GetErrorCauseLength(bool padded) => ERROR_CAUSE_LENGTH;

        public int WriteTo(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(ERROR_CAUSE_LENGTH, buffer, posn + 2);
            NetConvert.ToBuffer(StreamID, buffer, posn + 4);
            return ERROR_CAUSE_LENGTH;
        }
    }

    /// <summary>
    /// Indicates that one or more mandatory Type-Length-Value (TLV) format
    /// parameters are missing in a received INIT or INIT ACK.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.2
    /// </remarks>
    public struct SctpErrorMissingMandatoryParameter : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.MissingMandatoryParameter;

        public List<ushort> MissingParameters;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((MissingParameters != null) ? MissingParameters.Count * 2 : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (MissingParameters != null)
            {
                int valPosn = posn + 4;
                foreach (var missing in MissingParameters)
                {
                    NetConvert.ToBuffer(missing, buffer, valPosn);
                    valPosn += 2;
                }
            }
            return len;
        }
    }

    /// <summary>
    /// Indicates the receipt of a valid State Cookie that has expired.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.3
    /// </remarks>
    public struct SctpErrorStaleCookieError : ISctpErrorCause
    {
        private const ushort ERROR_CAUSE_LENGTH = 8;

        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.StaleCookieError;

        /// <summary>
        /// The difference, in microseconds, between the current time and the time the State Cookie expired.
        /// </summary>
        public uint MeasureOfStaleness;

        public ushort GetErrorCauseLength(bool padded) => ERROR_CAUSE_LENGTH;

        public int WriteTo(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(ERROR_CAUSE_LENGTH, buffer, posn + 2);
            NetConvert.ToBuffer(MeasureOfStaleness, buffer, posn + 4);
            return ERROR_CAUSE_LENGTH;
        }
    }

    /// <summary>
    /// Indicates that the sender is not able to resolve the specified address parameter
    /// (e.g., type of address is not supported by the sender).  This is usually sent in
    /// combination with or within an ABORT.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.5
    /// </remarks>
    public struct SctpErrorUnresolvableAddress : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.UnresolvableAddress;

        /// <summary>
        /// The Unresolvable Address field contains the complete Type, Length,
        /// and Value of the address parameter(or Host Name parameter) that
        /// contains the unresolvable address or host name.
        /// </summary>
        public byte[] UnresolvableAddress;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((UnresolvableAddress != null) ? UnresolvableAddress.Length : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (UnresolvableAddress != null)
            {
                Buffer.BlockCopy(UnresolvableAddress, 0, buffer, posn + 4, UnresolvableAddress.Length);
            }
            return len;
        }
    }

    /// <summary>
    /// Indicates that the sender is out of resource.  This
    /// is usually sent in combination with or within an ABORT.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.6
    /// </remarks>
    public struct SctpErrorUnrecognizedChunkType : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.UnrecognizedChunkType;

        /// <summary>
        /// The Unrecognized Chunk field contains the unrecognized chunk from
        /// the SCTP packet complete with Chunk Type, Chunk Flags, and Chunk
        /// Length.
        /// </summary>
        public byte[] UnrecognizedChunk;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((UnrecognizedChunk != null) ? UnrecognizedChunk.Length : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (UnrecognizedChunk != null)
            {
                Buffer.BlockCopy(UnrecognizedChunk, 0, buffer, posn + 4, UnrecognizedChunk.Length);
            }
            return len;
        }
    }

    /// <summary>
    /// This error cause is returned to the originator of the INIT ACK chunk 
    /// if the receiver does not recognize one or more optional variable parameters in 
    /// the INIT ACK chunk.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.8
    /// </remarks>
    public struct SctpErrorUnrecognizedParameters : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.UnrecognizedParameters;

        /// <summary>
        /// The Unrecognized Parameters field contains the unrecognized
        /// parameters copied from the INIT ACK chunk complete with TLV. This
        /// error cause is normally contained in an ERROR chunk bundled with
        /// the COOKIE ECHO chunk when responding to the INIT ACK, when the
        /// sender of the COOKIE ECHO chunk wishes to report unrecognized
        /// parameters.
        /// </summary>
        public byte[] UnrecognizedParameters;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((UnrecognizedParameters != null) ? UnrecognizedParameters.Length : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (UnrecognizedParameters != null)
            {
                Buffer.BlockCopy(UnrecognizedParameters, 0, buffer, posn + 4, UnrecognizedParameters.Length);
            }
            return len;
        }
    }

    /// <summary>
    /// This error cause is returned to the originator of a
    /// DATA chunk if a received DATA chunk has no user data.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.9
    /// </remarks>
    public struct SctpErrorNoUserData : ISctpErrorCause
    {
        private const ushort ERROR_CAUSE_LENGTH = 8;

        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.NoUserData;

        /// <summary>
        /// The TSN value field contains the TSN of the DATA chunk received
        /// with no user data field.
        /// </summary>
        public uint TSN;

        public ushort GetErrorCauseLength(bool padded) => ERROR_CAUSE_LENGTH;

        public int WriteTo(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(ERROR_CAUSE_LENGTH, buffer, posn + 2);
            NetConvert.ToBuffer(TSN, buffer, posn + 4);
            return ERROR_CAUSE_LENGTH;
        }
    }

    /// <summary>
    /// An INIT was received on an existing association.But the INIT added addresses to the
    /// association that were previously NOT part of the association. The new addresses are 
    /// listed in the error code.This ERROR is normally sent as part of an ABORT refusing the INIT.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.11
    /// </remarks>
    public struct SctpErrorRestartAssociationWithNewAddress : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.RestartAssociationWithNewAddress;

        /// <summary>
        /// Each New Address TLV is an exact copy of the TLV that was found
        /// in the INIT chunk that was new, including the Parameter Type and the
        /// Parameter Length.
        /// </summary>
        public byte[] NewAddressTLVs;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((NewAddressTLVs != null) ? NewAddressTLVs.Length : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (NewAddressTLVs != null)
            {
                Buffer.BlockCopy(NewAddressTLVs, 0, buffer, posn + 4, NewAddressTLVs.Length);
            }
            return len;
        }
    }

    /// <summary>
    /// This error cause MAY be included in ABORT chunks that are sent
    /// because of an upper-layer request.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.12
    /// </remarks>
    public struct SctpErrorUserInitiatedAbort : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.UserInitiatedAbort;

        /// <summary>
        /// Optional descriptive abort reason from Upper Layer Protocol (ULP).
        /// </summary>
        public string AbortReason;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((!string.IsNullOrEmpty(AbortReason)) ? Encoding.UTF8.GetByteCount(AbortReason) : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (!string.IsNullOrEmpty(AbortReason))
            {
                var reasonBuffer = Encoding.UTF8.GetBytes(AbortReason);
                Buffer.BlockCopy(reasonBuffer, 0, buffer, posn + 4, reasonBuffer.Length);
            }
            return len;
        }
    }

    /// <summary>
    /// This error cause MAY be included in ABORT chunks that are sent
    /// because an SCTP endpoint detects a protocol violation of the peer
    /// that is not covered by any of the more specific error causes
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.10.13
    /// </remarks>
    public struct SctpErrorProtocolViolation : ISctpErrorCause
    {
        public SctpErrorCauseCode CauseCode => SctpErrorCauseCode.ProtocolViolation;

        /// <summary>
        /// Optional description of the violation.
        /// </summary>
        public string AdditionalInformation;

        public ushort GetErrorCauseLength(bool padded)
        {
            ushort len = (ushort)(4 + ((!string.IsNullOrEmpty(AdditionalInformation)) ? Encoding.UTF8.GetByteCount(AdditionalInformation) : 0));
            return padded ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        public int WriteTo(byte[] buffer, int posn)
        {
            var len = GetErrorCauseLength(true);
            NetConvert.ToBuffer((ushort)CauseCode, buffer, posn);
            NetConvert.ToBuffer(len, buffer, posn + 2);
            if (!string.IsNullOrEmpty(AdditionalInformation))
            {
                var reasonBuffer = Encoding.UTF8.GetBytes(AdditionalInformation);
                Buffer.BlockCopy(reasonBuffer, 0, buffer, posn + 4, reasonBuffer.Length);
            }
            return len;
        }
    }
}
