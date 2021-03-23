//-----------------------------------------------------------------------------
// Filename: SctpChunkParameter.cs
//
// Description: Represents the a variable length parameter field for use within
// a Chunk.
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
    public enum SctpChunkParameterType : ushort
    {
        Unknown = 0,
        HeartbeatInfo = 1,
        IPv4Address = 5,
        IPv6Address = 6,
        StateCookie = 7,
        UnrecognizedParameters = 8,
        CookiePreservative = 9,
        HostNameAddress = 11,
        SupportedAddressTypes = 12,
        OutgoingSSNResetRequestParameter = 13,
        IncomingSSNResetRequestParameter = 14,
        SSNTSNResetRequestParameter = 15,
        ReconfigurationResponseParameter = 16,
        AddOutgoingStreamsRequestParameter = 17,
        AddIncomingStreamsRequestParameter = 18,
        ReservedforECNCapable = 32768,
        Random = 32770,
        ChunkList = 32771,
        RequestedHMACAlgorithmParameter = 32772,
        Padding = 32773,
        SupportedExtensions = 32776,
        ForwardTSNsupported = 49152,
        AddIPAddress = 49153,
        DeleteIPAddress = 49154,
        ErrorCauseIndication = 49155,
        SetPrimaryAddress = 49156,
        SuccessIndication = 49157,
        AdaptationLayerIndication = 49158
    }

    /// <summary>
    /// Represents the a variable length parameter field for use within
    /// a Chunk. All chunk parameters use the same underlying format but may 
    /// specialise how the value field is used.
    /// </summary>
    public class SctpChunkParameter
    {
        public const int SCTP_PARAMETER_HEADER_LENGTH = 4;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpChunkParameter>();

        /// <summary>
        /// The type of the chunk parameter.
        /// </summary>
        public ushort ParameterType { get; private set; }

        /// <summary>
        /// The information contained in the parameter.
        /// </summary>
        public byte[] ParameterValue;

        /// <summary>
        /// If recognised returns the known chunk parameter type. If not recognised returns null.
        /// </summary>
        public SctpChunkParameterType? KnownType
        {
            get
            {
                if (Enum.IsDefined(typeof(SctpChunkParameterType), ParameterType))
                {
                    return (SctpChunkParameterType)ParameterType;
                }
                else
                {
                    return null;
                }
            }
        }

        private SctpChunkParameter()
        { }

        /// <summary>
        /// Creates a new chunk parameter instance.
        /// </summary>
        public SctpChunkParameter(SctpChunkParameterType parameterType)
        {
            ParameterType = (ushort)parameterType;
        }

        /// <summary>
        /// Creates a new chunk parameter instance.
        /// </summary>
        public SctpChunkParameter(SctpChunkParameterType parameterType, byte[] parameterValue)
        {
            ParameterType = (ushort)parameterType;
            ParameterValue = parameterValue;
        }

        /// <summary>
        /// Calculates the length for the chunk parameter.
        /// </summary>
        /// <returns>The length of the chunk. This method gets overridden by specialised SCTP parameters 
        /// that each have their own fields that determine the length.</returns>
        public virtual ushort GetParameterLength()
        {
            return (ushort)(SCTP_PARAMETER_HEADER_LENGTH
                + (ParameterValue == null ? 0 : ParameterValue.Length));
        }

        /// <summary>
        /// Calculates the padded length for the chunk parameter.
        /// Parameters are required to be padded out to 4 byte boundaries.
        /// </summary>
        /// <returns>The length of the chunk. This method gets overridden by specialised SCTP parameters 
        /// that each have their own fields that determine the length.</returns>
        public ushort GetParameterPaddedLength()
        {
            return SctpPadding.PadTo4ByteBoundary(GetParameterLength());
        }

        /// <summary>
        /// Writes the parameter header to the buffer. All chunk parameters use the same two
        /// header fields.
        /// </summary>
        /// <param name="buffer">The buffer to write the chunk parameter header to.</param>
        /// <param name="posn">The position in the buffer to write at.</param>
        protected void WriteParameterHeader(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer(ParameterType, buffer, posn);
            NetConvert.ToBuffer(GetParameterLength(), buffer, posn + 2);
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
        public virtual int WriteTo(byte[] buffer, int posn)
        {
            WriteParameterHeader(buffer, posn);

            if (ParameterValue?.Length > 0)
            {
                Buffer.BlockCopy(ParameterValue, 0, buffer, posn + SCTP_PARAMETER_HEADER_LENGTH, ParameterValue.Length);
            }

            return GetParameterPaddedLength();
        }

        /// <summary>
        /// The first 32 bits of all chunk parameters represent the type and length. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk parameter.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk parameter.</param>
        public ushort ParseFirstWord(byte[] buffer, int posn)
        {
            ParameterType = NetConvert.ParseUInt16(buffer, posn);
            ushort paramLen = NetConvert.ParseUInt16(buffer, posn + 2);

            if (paramLen > 0 && buffer.Length < posn + paramLen)
            {
                // The buffer was not big enough to supply the specified chunk parameter.
                int bytesRequired = paramLen;
                int bytesAvailable = buffer.Length - posn;
                throw new ApplicationException($"The SCTP chunk parameter buffer was too short. " +
                    $"Required {bytesRequired} bytes but only {bytesAvailable} available.");
            }

            return paramLen;
        }

        /// <summary>
        /// Parses a simple chunk parameter and does not attempt to process the value.
        /// This method is suitable when:
        ///  - the parameter type is not recognised.
        ///  - the parameter type consists only of the 4 byte header and has 
        ///    no extra fields or chunk value set.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk parameter instance.</returns>
        public static SctpChunkParameter ParseSimpleChunkParameter(byte[] buffer, int posn)
        {
            var simpleParameter = new SctpChunkParameter();
            ushort paramLen = simpleParameter.ParseFirstWord(buffer, posn);
            if (paramLen > SCTP_PARAMETER_HEADER_LENGTH)
            {
                simpleParameter.ParameterValue = new byte[paramLen - SCTP_PARAMETER_HEADER_LENGTH];
                Buffer.BlockCopy(buffer, posn + SCTP_PARAMETER_HEADER_LENGTH, simpleParameter.ParameterValue,
                    0, simpleParameter.ParameterValue.Length);
            }
            return simpleParameter;
        }

        /// <summary>
        /// Parses an SCTP chunk parameter from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk parameter.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk parameter instance.</returns>
        public static SctpChunkParameter Parse(byte[] buffer, int posn)
        {
            if (buffer.Length < posn + SCTP_PARAMETER_HEADER_LENGTH)
            {
                throw new ApplicationException("Buffer did not contain the minimum of bytes for an SCTP chunk parameter.");
            }

            ushort parameterType = NetConvert.ParseUInt16(buffer, posn);

            if (Enum.IsDefined(typeof(SctpChunkParameterType), parameterType))
            {
                switch ((SctpChunkParameterType)parameterType)
                {
                    case SctpChunkParameterType.IPv4Address:
                    case SctpChunkParameterType.IPv6Address:
                        return SctpAddressParameter.ParseParameter(buffer, posn);
                    default:
                        logger.LogDebug($"TODO: Implement parsing logic for well known chunk parameter type {(SctpChunkParameterType)parameterType}.");
                        return ParseSimpleChunkParameter(buffer, posn);
                }
            }

            // Didn't recognised the buffer as a well known chunk.
            // Return a simple chunk and leave it up to the application.
            logger.LogWarning($"SCTP chunk parameter type of {parameterType} was not recognised.");

            return ParseSimpleChunkParameter(buffer, posn);
        }
    }

    /// <summary>
    /// Represents an IPv4 or IPv6 chunk parameter.
    /// </summary>
    public class SctpAddressParameter : SctpChunkParameter
    {
        public IPAddress Address;

        public SctpAddressParameter(IPAddress address) :
            base(address.AddressFamily == AddressFamily.InterNetwork ? 
                SctpChunkParameterType.IPv4Address : SctpChunkParameterType.IPv6Address)
        {
            Address = address;
        }

        public override ushort GetParameterLength() =>
            (ushort)(SCTP_PARAMETER_HEADER_LENGTH +
                (Address.AddressFamily == AddressFamily.InterNetwork ? 4 : 16));

        /// <summary>
        /// Serialises the SCTP address parameter to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk parameter bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override int WriteTo(byte[] buffer, int posn)
        {
            WriteParameterHeader(buffer, posn);

            var addrBytes = Address.GetAddressBytes();
            Buffer.BlockCopy(addrBytes, 0, buffer, posn + SCTP_PARAMETER_HEADER_LENGTH, addrBytes.Length);

            return GetParameterPaddedLength();
        }

        public static SctpAddressParameter ParseParameter(byte[] buffer, int posn)
        {
            var param = ParseSimpleChunkParameter(buffer, posn);
            var address = new IPAddress(param.ParameterValue);
            return new SctpAddressParameter(address);
        }
    }
}
