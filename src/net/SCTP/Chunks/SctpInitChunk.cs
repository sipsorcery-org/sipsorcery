//-----------------------------------------------------------------------------
// Filename: SctpInitChunk.cs
//
// Description: Represents the SCTP INIT chunk.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.2
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SIPSorcery.Net;

/// <summary>
/// The optional or variable length Type-Length-Value (TLV) parameters
/// that can be used with INIT and INIT ACK chunks.
/// </summary>
public enum SctpInitChunkParameterType : ushort
{
    IPv4Address = 5,
    IPv6Address = 6,
    StateCookie = 7,                // INIT ACK only.
    UnrecognizedParameter = 8,      // INIT ACK only.
    CookiePreservative = 9,
    HostNameAddress = 11,
    SupportedAddressTypes = 12,
    EcnCapable = 32768
}

/// <summary>
/// This class is used to represent both an INIT and INIT ACK chunk.
/// The only structural difference between them is the INIT ACK requires
/// the Cookie variable parameter to be set.
/// The INIT chunk is used to initiate an SCTP association between two
/// endpoints. The INIT ACK chunk is used to respond to an incoming
/// INIT chunk from a remote peer.
/// </summary>
public partial class SctpInitChunk : SctpChunk
{
    public const int FIXED_PARAMETERS_LENGTH = 16;

    // Lengths for the optional parameter values.
    private const ushort PARAMVAL_LENGTH_IPV4 = 4;
    private const ushort PARAMVAL_LENGTH_IPV6 = 16;
    private const ushort PARAMVAL_LENGTH_COOKIE_PRESERVATIVE = 4;

    /// <summary>
    /// The receiver of the INIT (the responding end) records the value of
    /// the Initiate Tag parameter.This value MUST be placed into the
    /// Verification Tag field of every SCTP packet that the receiver of
    /// the INIT transmits within this association.
    /// </summary>
    public uint InitiateTag;

    /// <summary>
    /// Advertised Receiver Window Credit. This value represents the dedicated 
    /// buffer space, in number of bytes, the sender of the INIT has reserved in 
    /// association with this window.
    /// </summary>
    public uint ARwnd;

    /// <summary>
    /// Defines the number of outbound streams the sender of this INIT
    /// chunk wishes to create in this association.
    /// </summary>
    public ushort NumberOutboundStreams;

    /// <summary>
    /// Defines the maximum number of streams the sender of this INIT
    /// chunk allows the peer end to create in this association.
    /// </summary>
    public ushort NumberInboundStreams;

    /// <summary>
    /// The initial Transmission Sequence Number (TSN) that the sender will use.
    /// </summary>
    public uint InitialTSN;

    /// <summary>
    /// Optional list of IP address parameters that can be included in INIT chunks.
    /// </summary>
    public List<IPAddress> Addresses = new List<IPAddress>();

    /// <summary>
    /// The sender of the INIT shall use this parameter to suggest to the
    /// receiver of the INIT for a longer life-span of the State Cookie.
    /// </summary>
    public uint CookiePreservative;

    /// <summary>
    /// The sender of INIT uses this parameter to pass its Host Name (in
    /// place of its IP addresses) to its peer.The peer is responsible for
    /// resolving the name.Using this parameter might make it more likely
    /// for the association to work across a NAT box.
    /// </summary>
    public string? HostnameAddress;

    /// <summary>
    /// The sender of INIT uses this parameter to list all the address types
    /// it can support. Options are IPv4 (5), IPv6 (6) and Hostname (11).
    /// </summary>
    public List<SctpInitChunkParameterType> SupportedAddressTypes = new List<SctpInitChunkParameterType>();

    /// <summary>
    /// INIT ACK only. Mandatory. This parameter value MUST contain all the necessary state and
    /// parameter information required for the sender of this INIT ACK to create the association, 
    /// along with a Message Authentication Code (MAC). 
    /// </summary>
    public byte[]? StateCookie;

    /// <summary>
    /// INIT ACK only. Optional. This parameter is returned to the originator of the INIT chunk 
    /// if the INIT contains an unrecognized parameter that has a value that indicates it should
    /// be reported to the sender. This parameter value field will contain unrecognized parameters 
    /// copied from the  INIT chunk complete with Parameter Type, Length, and Value fields.
    /// </summary>
    public List<byte[]> UnrecognizedParameters = new List<byte[]>();

    private SctpInitChunk()
    { }

    /// <summary>
    /// Initialises the chunk as either INIT or INIT ACK.
    /// </summary>
    /// <param name="initChunkType">Either INIT or INIT ACK.</param>
    public SctpInitChunk(SctpChunkType initChunkType,
        uint initiateTag,
        uint initialTSN,
        uint arwnd,
        ushort numberOutboundStreams,
        ushort numberInboundStreams) : base(initChunkType)
    {
        InitiateTag = initiateTag;
        NumberOutboundStreams = numberOutboundStreams;
        NumberInboundStreams = numberInboundStreams;
        InitialTSN = initialTSN;
        ARwnd = arwnd;
    }

    /// <summary>
    /// Gets the length of the optional and variable length parameters for this
    /// INIT or INIT ACK chunk.
    /// </summary>
    /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
    /// <returns>The length of the optional and variable length parameters.</returns>
    private ushort GetVariableParametersLength(bool padded)
    {
        int len = 0;

        var ipv4Count = 0;
        var ipv6Count = 0;
        foreach (var x in Addresses)
        {
            if (x.AddressFamily == AddressFamily.InterNetwork)
            {
                ipv4Count++;
            }
            else if (x.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ipv6Count++;
            }
        }

        len += ipv4Count * (SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH + PARAMVAL_LENGTH_IPV4);

        len += ipv6Count * (SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH + PARAMVAL_LENGTH_IPV6);

        if (CookiePreservative > 0)
        {
            len += SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH +
                PARAMVAL_LENGTH_COOKIE_PRESERVATIVE;
        }

        if (!string.IsNullOrEmpty(HostnameAddress))
        {
            len += SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH +
                SctpPadding.PadTo4ByteBoundary(Encoding.UTF8.GetByteCount(HostnameAddress));
        }

        if (SupportedAddressTypes.Count > 0)
        {
            len += SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH +
                SctpPadding.PadTo4ByteBoundary(SupportedAddressTypes.Count * 2);
        }

        if (StateCookie is { })
        {
            len += SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH +
                SctpPadding.PadTo4ByteBoundary(StateCookie.Length);
        }

        foreach (var unrecognised in UnrecognizedPeerParameters)
        {
            len += SctpTlvChunkParameter.SCTP_PARAMETER_HEADER_LENGTH +
                unrecognised.GetParameterLength(true);
        }

        return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : (ushort)len;
    }

    /// <summary>
    /// Writes the optional and variable length parameters to a Type-Length-Value (TLV)
    /// parameter list.
    /// </summary>
    /// <returns>A TLV parameter list holding the optional and variable length parameters.</returns>
    private List<SctpTlvChunkParameter> GetVariableParameters()
    {
        List<SctpTlvChunkParameter> varParams = new List<SctpTlvChunkParameter>();

        // Add the optional and variable length parameters as Type-Length-Value (TLV) formatted.
        foreach (var address in Addresses)
        {
            ushort addrParamType = (ushort)(address.AddressFamily == AddressFamily.InterNetwork ?
                SctpInitChunkParameterType.IPv4Address : SctpInitChunkParameterType.IPv6Address);
            var addrParam = new SctpTlvChunkParameter(addrParamType, address.GetAddressBytes());
            varParams.Add(addrParam);
        }

        if (CookiePreservative > 0)
        {
            var buffer = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, CookiePreservative);

            varParams.Add(
                new SctpTlvChunkParameter((ushort)SctpInitChunkParameterType.CookiePreservative,
                buffer
                ));
        }

        if (!string.IsNullOrEmpty(HostnameAddress))
        {
            varParams.Add(
                new SctpTlvChunkParameter((ushort)SctpInitChunkParameterType.HostNameAddress,
                Encoding.UTF8.GetBytes(HostnameAddress)
                ));
        }

        if (SupportedAddressTypes.Count > 0)
        {
            var paramVal = new byte[SupportedAddressTypes.Count * 2];
            var paramValPosn = 0;
            foreach (var supAddr in SupportedAddressTypes)
            {
                BinaryPrimitives.WriteUInt16BigEndian(paramVal.AsSpan(paramValPosn), (ushort)supAddr);
                paramValPosn += 2;
            }
            varParams.Add(
                new SctpTlvChunkParameter((ushort)SctpInitChunkParameterType.SupportedAddressTypes, paramVal));
        }

        if (StateCookie is { })
        {
            varParams.Add(
                new SctpTlvChunkParameter((ushort)SctpInitChunkParameterType.StateCookie, StateCookie));
        }

        foreach (var unrecognised in UnrecognizedPeerParameters)
        {
            var paramBuffer = new byte[unrecognised.GetParameterLength(true)];
            _ = unrecognised.WriteBytes(paramBuffer);
            varParams.Add(
               new SctpTlvChunkParameter((ushort)SctpInitChunkParameterType.UnrecognizedParameter, paramBuffer));
        }

        return varParams;
    }

    /// <summary>
    /// Calculates the length for INIT and INIT ACK chunks.
    /// </summary>
    /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
    /// <returns>The length of the chunk.</returns>
    public override ushort GetByteCount(bool padded)
    {
        var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH +
            FIXED_PARAMETERS_LENGTH +
            GetVariableParametersLength(false));

        return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
    }

    /// <summary>
    /// Serialises an INIT or INIT ACK chunk to a pre-allocated buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
    /// must have the required space already allocated.</param>
    /// <returns>The number of bytes, including padding, written to the buffer.</returns>
    public override ushort WriteBytes(Span<byte> buffer)
    {
        WriteBytesCore(buffer);

        return GetByteCount(true);
    }

    private void WriteBytesCore(Span<byte> buffer)
    {
        var bytesWritten = WriteChunkHeader(buffer);

        // Write fixed parameters.

        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH), InitiateTag);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 4), ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 8), NumberOutboundStreams);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 10), NumberInboundStreams);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 12), InitialTSN);

        // Write optional parameters.
        if (GetVariableParameters() is { Count: > 0 } varParameters)
        {
            buffer = buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH);
            foreach (var optParam in varParameters)
            {
                var bytesWriten = optParam.WriteBytes(buffer);
                buffer = buffer.Slice(bytesWriten);
            }
        }
    }

    /// <summary>
    /// Parses the INIT or INIT ACK chunk fields
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised chunk.</param>
    public static SctpInitChunk ParseChunk(ReadOnlySpan<byte> buffer)
    {
        var initChunk = new SctpInitChunk();
        var chunkLen = initChunk.ParseFirstWord(buffer);

        initChunk.InitiateTag = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH));
        initChunk.ARwnd = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 4));
        initChunk.NumberOutboundStreams = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 8));
        initChunk.NumberInboundStreams = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 10));
        initChunk.InitialTSN = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 12));

        var paramPosn = SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;
        var paramsBufferLength = chunkLen - SCTP_CHUNK_HEADER_LENGTH - FIXED_PARAMETERS_LENGTH;

        if (paramPosn < paramsBufferLength)
        {
            var stopProcessing = false;

            foreach (var varParam in GetParameters(buffer.Slice(paramPosn, paramsBufferLength)))
            {
                Debug.Assert(varParam is { });

                switch (varParam.ParameterType)
                {
                    case (ushort)SctpInitChunkParameterType.IPv4Address:
                    case (ushort)SctpInitChunkParameterType.IPv6Address:
                        Debug.Assert(varParam.ParameterValue is { });
                        var address = new IPAddress(varParam.ParameterValue);
                        initChunk.Addresses.Add(address);
                        break;

                    case (ushort)SctpInitChunkParameterType.CookiePreservative:
                        Debug.Assert(varParam.ParameterValue is { Length: >= sizeof(uint) });
                        initChunk.CookiePreservative = BinaryPrimitives.ReadUInt32BigEndian(varParam.ParameterValue.AsSpan());
                        break;

                    case (ushort)SctpInitChunkParameterType.HostNameAddress:
                        Debug.Assert(varParam.ParameterValue is { });
                        initChunk.HostnameAddress = Encoding.UTF8.GetString(varParam.ParameterValue);
                        break;

                    case (ushort)SctpInitChunkParameterType.SupportedAddressTypes:
                        Debug.Assert(varParam.ParameterValue is { });
                        for (int valPosn = 0; valPosn < varParam.ParameterValue.Length; valPosn += 2)
                        {
                            switch (BinaryPrimitives.ReadUInt16BigEndian(varParam.ParameterValue.AsSpan(valPosn)))
                            {
                                case (ushort)SctpInitChunkParameterType.IPv4Address:
                                    initChunk.SupportedAddressTypes.Add(SctpInitChunkParameterType.IPv4Address);
                                    break;
                                case (ushort)SctpInitChunkParameterType.IPv6Address:
                                    initChunk.SupportedAddressTypes.Add(SctpInitChunkParameterType.IPv6Address);
                                    break;
                                case (ushort)SctpInitChunkParameterType.HostNameAddress:
                                    initChunk.SupportedAddressTypes.Add(SctpInitChunkParameterType.HostNameAddress);
                                    break;
                            }
                        }
                        break;

                    case (ushort)SctpInitChunkParameterType.EcnCapable:
                        break;

                    case (ushort)SctpInitChunkParameterType.StateCookie:
                        // Used with INIT ACK chunks only.
                        initChunk.StateCookie = varParam.ParameterValue;
                        break;

                    case (ushort)SctpInitChunkParameterType.UnrecognizedParameter:
                        // Used with INIT ACK chunks only. This parameter is the remote peer returning
                        // a list of parameters it did not understand in the INIT chunk.
                        Debug.Assert(varParam.ParameterValue is { });
                        initChunk.UnrecognizedParameters.Add(varParam.ParameterValue);
                        break;

                    default:
                        // Parameters are not recognised in an INIT or INIT ACK.
                        initChunk.GotUnrecognisedParameter(varParam);
                        break;
                }

                if (stopProcessing)
                {
                    logger.LogSctpUnrecognisedParameter(varParam.ParameterType, initChunk.KnownType);
                    break;
                }
            }
        }

        return initChunk;
    }
}
