//-----------------------------------------------------------------------------
// Filename: SctpTlvChunkParameter.cs
//
// Description: Represents the a Type-Length-Value (TLV) chunk parameter. These
// parameters represent optional or variable length parameters for a chunk.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.2.1.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    //public enum SctpChunkParameterType : ushort
    //{
    //    Unknown = 0,
    //    HeartbeatInfo = 1,
    //    IPv4Address = 5,
    //    IPv6Address = 6,
    //    StateCookie = 7,
    //    UnrecognizedParameters = 8,
    //    CookiePreservative = 9,
    //    HostNameAddress = 11,
    //    SupportedAddressTypes = 12,
    //    OutgoingSSNResetRequestParameter = 13,
    //    IncomingSSNResetRequestParameter = 14,
    //    SSNTSNResetRequestParameter = 15,
    //    ReconfigurationResponseParameter = 16,
    //    AddOutgoingStreamsRequestParameter = 17,
    //    AddIncomingStreamsRequestParameter = 18,
    //    ReservedforECNCapable = 32768,
    //    Random = 32770,
    //    ChunkList = 32771,
    //    RequestedHMACAlgorithmParameter = 32772,
    //    Padding = 32773,
    //    SupportedExtensions = 32776,
    //    ForwardTSNsupported = 49152,
    //    AddIPAddress = 49153,
    //    DeleteIPAddress = 49154,
    //    ErrorCauseIndication = 49155,
    //    SetPrimaryAddress = 49156,
    //    SuccessIndication = 49157,
    //    AdaptationLayerIndication = 49158
    //}

    /// <summary>
    /// The actions required for unrecognised parameters. The byte value corresponds to the highest 
    /// order two bits of the parameter type value.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.2.1
    /// </remarks>
    public enum SctpUnrecognisedParameterActions : byte
    {
        /// <summary>
        /// Stop processing this parameter; do not process any further parameters within this chunk.
        /// </summary>
        Stop = 0x00,

        /// <summary>
        /// Stop processing this parameter, do not process any further parameters within this chunk, and report the unrecognized
        /// parameter in an 'Unrecognized Parameter'.
        /// </summary>
        StopAndReport = 0x01,

        /// <summary>
        /// Skip this parameter and continue processing.
        /// </summary>
        Skip = 0x02,

        /// <summary>
        /// Skip this parameter and continue processing but report the unrecognized parameter in an 'Unrecognized Parameter'.
        /// </summary>
        SkipAndReport = 0x03
    }

    /// <summary>
    /// Represents the a variable length parameter field for use within
    /// a Chunk. All chunk parameters use the same underlying Type-Length-Value (TLV)
    /// format but then specialise how the fields are used.
    /// </summary>
    /// <remarks>
    /// From https://tools.ietf.org/html/rfc4960#section-3.2.1 (final section):
    /// Note that a parameter type MUST be unique
    /// across all chunks.For example, the parameter type '5' is used to
    /// represent an IPv4 address. The value '5' then
    /// is reserved across all chunks to represent an IPv4 address and MUST
    /// NOT be reused with a different meaning in any other chunk.
    /// </remarks>
    public class SctpTlvChunkParameter
    {
        public const int SCTP_PARAMETER_HEADER_LENGTH = 4;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpTlvChunkParameter>();

        /// <summary>
        /// The type of the chunk parameter.
        /// </summary>
        public ushort ParameterType { get; protected set; }

        /// <summary>
        /// The information contained in the parameter.
        /// </summary>
        public byte[] ParameterValue;

        /// <summary>
        /// If this parameter is unrecognised by the parent chunk then this field dictates
        /// how it should handle it.
        /// </summary>
        public SctpUnrecognisedParameterActions UnrecognisedAction =>
            (SctpUnrecognisedParameterActions) (ParameterType >> 14 & 0x03);

        protected SctpTlvChunkParameter()
        { }

        /// <summary>
        /// Creates a new chunk parameter instance.
        /// </summary>
        public SctpTlvChunkParameter(ushort parameterType, byte[] parameterValue)
        {
            ParameterType = parameterType;
            ParameterValue = parameterValue;
        }

        /// <summary>
        /// Calculates the length for the chunk parameter.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The length of the chunk. This method gets overridden by specialised SCTP parameters 
        /// that each have their own fields that determine the length.</returns>
        public virtual ushort GetParameterLength(bool padded)
        {
            ushort len = (ushort)(SCTP_PARAMETER_HEADER_LENGTH
                + (ParameterValue == null ? 0 : ParameterValue.Length));

            return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        /// <summary>
        /// Writes the parameter header to the buffer. All chunk parameters use the same two
        /// header fields.
        /// </summary>
        /// <param name="buffer">The buffer to write the chunk parameter header to.</param>
        /// <param name="posn">The position in the buffer to write at.</param>
        protected void WriteParameterHeader(Span<byte> buffer, int posn)
        {
            NetConvert.ToBuffer(ParameterType, buffer, posn);
            NetConvert.ToBuffer(GetParameterLength(false), buffer, posn + 2);
        }

        /// <summary>
        /// Serialises the chunk parameter to a pre-allocated buffer. This method gets overridden 
        /// by specialised SCTP chunk parameters that have their own data and need to be serialised
        /// differently.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk parameter bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public virtual int WriteTo(Span<byte> buffer, int posn)
        {
            WriteParameterHeader(buffer, posn);

            if (ParameterValue?.Length > 0)
            {
                ParameterValue.CopyTo(buffer.Slice(posn + SCTP_PARAMETER_HEADER_LENGTH));
            }

            return GetParameterLength(true);
        }

        /// <summary>
        /// Serialises an SCTP chunk parameter to a byte array.
        /// </summary>
        /// <returns>The byte array containing the serialised chunk parameter.</returns>
        public byte[] GetBytes()
        {
            byte[] buffer = new byte[GetParameterLength(true)];
            WriteTo(buffer, 0);
            return buffer;
        }

        /// <summary>
        /// The first 32 bits of all chunk parameters represent the type and length. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk parameter.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk parameter.</param>
        public ushort ParseFirstWord(ReadOnlySpan<byte> buffer, int posn)
        {
            ushort len = ParseFirstWord(buffer.Slice(posn), out ushort type);
            ParameterType = type;
            return len;
        }

        /// <summary>
        /// The first 32 bits of all chunk parameters represent the type and length. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk parameter.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk parameter.</param>
        public static ushort ParseFirstWord(ReadOnlySpan<byte> buffer, out ushort type)
        {
            type = NetConvert.ParseUInt16(buffer, 0);
            ushort paramLen = NetConvert.ParseUInt16(buffer, 2);

            if (paramLen > 0 && buffer.Length < paramLen)
            {
                // The buffer was not big enough to supply the specified chunk parameter.
                int bytesRequired = paramLen;
                int bytesAvailable = buffer.Length;
                throw new ApplicationException($"The SCTP chunk parameter buffer was too short. " +
                    $"Required {bytesRequired} bytes but only {bytesAvailable} available.");
            }

            return paramLen;
        }

        /// <summary>
        /// Parses an SCTP Type-Length-Value (TLV) chunk parameter from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised TLV chunk parameter.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP TLV chunk parameter instance.</returns>
        public static SctpTlvChunkParameter ParseTlvParameter(ReadOnlySpan<byte> buffer, int posn)
        {
            if (buffer.Length < posn + SCTP_PARAMETER_HEADER_LENGTH)
            {
                throw new ApplicationException("Buffer did not contain the minimum of bytes for an SCTP TLV chunk parameter.");
            }

            var tlvParam = new SctpTlvChunkParameter();
            ushort paramLen = tlvParam.ParseFirstWord(buffer, posn);
            if (paramLen > SCTP_PARAMETER_HEADER_LENGTH)
            {
                tlvParam.ParameterValue = new byte[paramLen - SCTP_PARAMETER_HEADER_LENGTH];
                buffer.Slice(posn + SCTP_PARAMETER_HEADER_LENGTH, tlvParam.ParameterValue.Length).CopyTo(tlvParam.ParameterValue);
            }
            return tlvParam;
        }
    }
}
