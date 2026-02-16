//-----------------------------------------------------------------------------
// Filename: RTCIceCandidate.cs
//
// Description: Represents a candidate used in the Interactive Connectivity 
// Establishment (ICE) negotiation to set up a usable network connection 
// between two peers as per RFC8445 https://tools.ietf.org/html/rfc8445
// (previously implemented for RFC5245 https://tools.ietf.org/html/rfc5245).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Feb 2016	Aaron Clauson	Created, Hobart, Australia.
// 15 Mar 2020  Aaron Clauson   Updated for RFC8445.
// 17 Mar 2020  Aaron Clauson   Renamed from IceCandidate to RTCIceCandidate.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics.CodeAnalysis;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class RTCIceCandidate : IRTCIceCandidate, IEquatable<RTCIceCandidate>
{
    public const string m_CRLF = "\r\n";
    public const string TCP_TYPE_KEY = "tcpType";
    public const string REMOTE_ADDRESS_KEY = "raddr";
    public const string REMOTE_PORT_KEY = "rport";
    public const string CANDIDATE_PREFIX = "candidate:";

    /// <summary>
    /// The ICE server (STUN or TURN) the candidate was generated from.
    /// Will be null for non-ICE server candidates.
    /// </summary>
    public IceServer? IceServer { get; internal set; }

    public string candidate => ToString();

    public string? sdpMid { get; set; }

    public ushort sdpMLineIndex { get; set; }

    /// <summary>
    /// Composed of 1 to 32 chars. It is an
    /// identifier that is equivalent for two candidates that are of the
    /// same type, share the same base, and come from the same STUN
    /// server.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8445#section-5.1.1.3.
    /// </remarks>
    public string? foundation { get; set; }

    /// <summary>
    ///  Is a positive integer between 1 and 256 (inclusive)
    /// that identifies the specific component of the data stream for
    /// which this is a candidate.
    /// </summary>
    public RTCIceComponent component { get; set; } = RTCIceComponent.rtp;

    /// <summary>
    /// A positive integer between 1 and (2**31 - 1) inclusive.
    /// This priority will be used by ICE to determine the order of the
    /// connectivity checks and the relative preference for candidates.
    /// Higher-priority values give more priority over lower values.
    /// </summary>
    /// <remarks>
    /// See specification at https://tools.ietf.org/html/rfc8445#section-5.1.2.
    /// </remarks>
    public uint priority { get; set; }

    /// <summary>
    /// The address or hostname for the candidate.
    /// </summary>
    public string? address { get; set; }

    /// <summary>
    /// The transport protocol for the candidate, supported options are UDP and TCP.
    /// </summary>
    public RTCIceProtocol protocol { get; set; }

    /// <summary>
    /// The local port the candidate is listening on.
    /// </summary>
    public ushort port { get; set; }

    /// <summary>
    /// The type of ICE candidate, host, srflx etc.
    /// </summary>
    public RTCIceCandidateType type { get; set; }

    /// <summary>
    /// For TCP candidates the role they are fulfilling (client, server or both).
    /// </summary>
    public RTCIceTcpCandidateType tcpType { get; set; }

    public string? relatedAddress { get; set; }

    public ushort relatedPort { get; set; }

    public string? usernameFragment { get; set; }

    /// <summary>
    /// This is the end point to use for a remote candidate. The address supplied for an ICE
    /// candidate could be a hostname or IP address. This field will be set before the candidate
    /// is used.
    /// </summary>
    public IPEndPoint? DestinationEndPoint { get; private set; }

    private RTCIceCandidate()
    { }

    public RTCIceCandidate(RTCIceCandidateInit init)
    {
        sdpMid = init.sdpMid;
        sdpMLineIndex = init.sdpMLineIndex;
        usernameFragment = init.usernameFragment;

        if (!string.IsNullOrEmpty(init.candidate))
        {
            var iceCandidate = Parse(init.candidate.AsSpan());
            foundation = iceCandidate.foundation;
            priority = iceCandidate.priority;
            component = iceCandidate.component;
            address = iceCandidate.address;
            port = iceCandidate.port;
            type = iceCandidate.type;
            tcpType = iceCandidate.tcpType;
            relatedAddress = iceCandidate.relatedAddress;
            relatedPort = iceCandidate.relatedPort;
        }
    }

    /// <summary>
    /// Convenience constructor for cases when the application wants
    /// to create an ICE candidate,
    /// </summary>
    public RTCIceCandidate(
        RTCIceProtocol cProtocol,
        IPAddress cAddress,
        ushort cPort,
        RTCIceCandidateType cType)
    {
        SetAddressProperties(cProtocol, cAddress, cPort, cType, null, 0);
    }

    public void SetAddressProperties(
        RTCIceProtocol cProtocol,
        IPAddress cAddress,
        ushort cPort,
        RTCIceCandidateType cType,
        IPAddress? cRelatedAddress,
        ushort cRelatedPort)
    {
        protocol = cProtocol;
        address = cAddress.ToString();
        port = cPort;
        type = cType;
        relatedAddress = cRelatedAddress?.ToString();
        relatedPort = cRelatedPort;

        foundation = GetFoundation();
        priority = GetPriority();
    }

    public static RTCIceCandidate Parse(ReadOnlySpan<char> candidateLine)
    {
        ArgumentOutOfRangeException.ThrowIfEmptyWhiteSpace(candidateLine);

        if (!TryParse(candidateLine, out var candidate))
        {
            throw new FormatException("The ICE candidate line was not in the correct format.");
        }

        return candidate;
    }

    /// <summary>
    /// Attempts to parse an ICE candidate line.
    /// </summary>
    /// <param name="candidateLine">The candidate line to parse.</param>
    /// <param name="candidate">The parsed candidate, or null if parsing failed.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(ReadOnlySpan<char> candidateLine, [NotNullWhen(true)] out RTCIceCandidate? candidate)
    {
        candidate = null;

        if (candidateLine.IsEmptyOrWhiteSpace())
        {
            return false;
        }

        if (candidateLine.StartsWith(CANDIDATE_PREFIX.AsSpan(), StringComparison.Ordinal))
        {
            candidateLine = candidateLine.Slice(CANDIDATE_PREFIX.Length);
        }

        Span<Range> ranges = stackalloc Range[13];
        var rangesCount = candidateLine.Split(ranges, ' ');
        if (rangesCount < 8)
        {
            return false;
        }

        var cand = new RTCIceCandidate();

        cand.foundation = candidateLine[ranges[0]].ToString();

        if (RTCIceComponentExtensions.TryParse(candidateLine[ranges[1]], out var candidateComponent))
        {
            cand.component = candidateComponent;
        }

        if (RTCIceProtocolExtensions.TryParse(candidateLine[ranges[2]], out var candidateProtocol))
        {
            cand.protocol = candidateProtocol;
        }

        if (uint.TryParse(candidateLine[ranges[3]], out var candidatePriority))
        {
            cand.priority = candidatePriority;
        }

        cand.address = candidateLine[ranges[4]].ToString();

        if (!ushort.TryParse(candidateLine[ranges[5]], out var port))
        {
            return false;
        }
        cand.port = port;

        if (RTCIceCandidateTypeExtensions.TryParse(candidateLine[ranges[7]], out var candidateType))
        {
            cand.type = candidateType;
        }

        // TCP Candidates require extra steps to be parsed
        // {"candidate":"candidate:4 1 TCP 2105458943 10.0.1.16 9 typ host tcptype active","sdpMid":"sdparta_0","sdpMLineIndex":0}
        var parseIndex = 8;
        if (cand.protocol == RTCIceProtocol.tcp)
        {
            if (rangesCount > parseIndex + 1 && candidateLine[ranges[parseIndex]].Equals(TCP_TYPE_KEY.AsSpan(), StringComparison.Ordinal))
            {
                cand.relatedAddress = candidateLine[ranges[parseIndex + 1]].ToString();
            }

            parseIndex += 2;
        }

        if (rangesCount > parseIndex && candidateLine[ranges[parseIndex]].Equals(REMOTE_ADDRESS_KEY.AsSpan(), StringComparison.Ordinal))
        {
            cand.relatedAddress = candidateLine[ranges[parseIndex + 1]].ToString();
        }

        if (rangesCount > parseIndex + 3 && candidateLine[ranges[parseIndex + 2]].Equals(REMOTE_PORT_KEY.AsSpan(), StringComparison.Ordinal))
        {
            if (!ushort.TryParse(candidateLine[ranges[parseIndex + 3]], out var relatedPort))
            {
                return false;
            }
            cand.relatedPort = relatedPort;
        }

        candidate = cand;
        return true;
    }

    /// <summary>
    /// Serialises an ICE candidate to a string that's suitable for inclusion in an SDP session
    /// description payload.
    /// </summary>
    /// <remarks>
    /// The specification regarding how an ICE candidate should be serialised in SDP is at
    /// https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-5.1.   
    /// </remarks>
    /// <returns>A string representing the ICE candidate suitable for inclusion in an SDP session
    /// description.</returns>
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

    /// <summary>
    /// Appends the candidate to a ValueStringBuilder.
    /// </summary>
    /// <param name="builder">The ValueStringBuilder to append to.</param>
    internal void ToString(ref ValueStringBuilder builder)
    {
        builder.Append(foundation);
        builder.Append(' ');
        builder.Append((int)component);
        builder.Append(' ');

        if (protocol == RTCIceProtocol.tcp)
        {
            builder.Append("tcp ");
        }
        else
        {
            builder.Append("udp ");
        }

        builder.Append(priority);
        builder.Append(' ');
        builder.Append(address);
        builder.Append(' ');
        builder.Append(port);
        builder.Append(" typ ");
        builder.Append(type.ToStringFast());

        if (protocol == RTCIceProtocol.tcp)
        {
            builder.Append(" tcptype ");
            builder.Append(tcpType.ToStringFast());
        }

        if (type is not RTCIceCandidateType.host and not RTCIceCandidateType.prflx)
        {
            var relAddr = string.IsNullOrWhiteSpace(relatedAddress) ? IPAddress.Any.ToString() : relatedAddress;
            builder.Append(" raddr ");
            builder.Append(relAddr);
            builder.Append(" rport ");
            builder.Append(relatedPort);
        }

        builder.Append(" generation 0");
    }

    /// <summary>
    /// Sets the remote end point for a remote candidate.
    /// </summary>
    /// <param name="destinationEP">The resolved end point for the candidate.</param>
    public void SetDestinationEndPoint(IPEndPoint destinationEP)
    {
        DestinationEndPoint = destinationEP;
    }

    private string GetFoundation()
    {
        var serverProtocol = (IceServer?.Protocol ?? ProtocolType.Udp).ToLowerString();
        var builder = new System.Text.StringBuilder();
        builder = builder.Append(type).Append(address).Append(protocol).Append(serverProtocol);
        var bytes = System.Text.Encoding.ASCII.GetBytes(builder.ToString());
        return UpdateCrc32(0, bytes).ToString();
    }

    private uint GetPriority()
    {
        uint localPreference = 0;

        //Calculate our LocalPreference Priority
        if (IPAddress.TryParse(address, out var addr))
        {
            var addrPref = IPAddressHelper.IPAddressPrecedence(addr);

            // relay_preference in original code was sorted with params:
            // UDP == 2
            // TCP == 1
            // TLS == 0
            var relayPreference = protocol == RTCIceProtocol.udp ? 2u : 1u;

            // TODO: Original implementation consider network adapter preference as strength of wifi
            // We will ignore it as its seems to not be a trivial implementation for use in net-standard 2.0
            uint networkAdapterPreference = 0;

            localPreference = ((networkAdapterPreference << 8) | addrPref) + relayPreference;
        }

        // RTC 5245 Define priority for RTCIceCandidateType
        // https://datatracker.ietf.org/doc/html/rfc5245
        uint typePreference = 0;
        switch (type)
        {
            case RTCIceCandidateType.host:
                typePreference = 126;
                break;
            case RTCIceCandidateType.prflx:
                typePreference = 110;
                break;
            case RTCIceCandidateType.srflx:
                typePreference = 100;
                break;
        }

        //Use formula found in RFC 5245 to define candidate priority
        return (uint)((typePreference << 24) | (localPreference << 8) | (256u - component.GetHashCode()));
    }

    public string toJSON()
    {
        var sb = new ValueStringBuilder(stackalloc char[256]);
        try
        {
            sb.Append(CANDIDATE_PREFIX);
            ToString(ref sb);

            var rtcCandInit = new RTCIceCandidateInit
            {
                sdpMid = sdpMid ?? sdpMLineIndex.ToString(),
                sdpMLineIndex = sdpMLineIndex,
                usernameFragment = usernameFragment,
                candidate = sb.ToString(),
            };

            return rtcCandInit.toJSON();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>
    /// Checks the candidate to identify whether it is equivalent to the specified
    /// protocol and IP end point. Primary use case is to check whether a candidate
    /// is a match for a remote end point that a message has been received from.
    /// </summary>
    /// <param name="epPotocol">The protocol to check equivalence for.</param>
    /// <param name="ep">The IP end point to check equivalence for.</param>
    /// <returns>True if the candidate is deemed equivalent or false if not.</returns>
    public bool IsEquivalentEndPoint(RTCIceProtocol epPotocol, IPEndPoint ep)
    {
        if (protocol == epPotocol && DestinationEndPoint is { } &&
           ep.Address.Equals(DestinationEndPoint.Address) && DestinationEndPoint.Port == ep.Port)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a short description for the candidate that's helpful for log messages.
    /// </summary>
    /// <returns>A short string describing the key properties of the candidate.</returns>
    public string ToShortString()
    {
        string epDescription;
        if (IPAddress.TryParse(address, out var ipAddress))
        {
            var ep = new IPEndPoint(ipAddress, port);
            epDescription = ep.ToString();
        }
        else
        {
            epDescription = $"{address}:{port}";
        }

        return $"{protocol}:{epDescription} ({type.ToStringFast()})";
    }

    //CRC32 implementation from C++ to calculate foundation
    private const uint kCrc32Polynomial = 0xEDB88320;
    private static uint[] LoadCrc32Table()
    {
        var kCrc32Table = new uint[256];
        for (uint i = 0; i < kCrc32Table.Length; ++i)
        {
            var c = i;
            for (var j = 0; j < 8; ++j)
            {
                if ((c & 1) != 0)
                {
                    c = kCrc32Polynomial ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }
            kCrc32Table[i] = c;
        }
        return kCrc32Table;
    }

    private uint UpdateCrc32(uint start, byte[] buf)
    {
        var kCrc32Table = LoadCrc32Table();

        long c = (int)(start ^ 0xFFFFFFFF);
        var u = buf;
        for (var i = 0; i < buf.Length; ++i)
        {
            c = kCrc32Table[(c ^ u[i]) & 0xFF] ^ (c >> 8);
        }
        return (uint)(c ^ 0xFFFFFFFF);
    }

    /// <summary>
    /// Determines whether the specified RTCIceCandidate is equal to the current RTCIceCandidate.
    /// Equality is based on all fields used in the SDP string representation.
    /// </summary>
    /// <param name="other">The RTCIceCandidate to compare with the current candidate.</param>
    /// <returns>true if the specified candidate is equal to the current candidate; otherwise, false.</returns>
    public bool Equals(RTCIceCandidate? other)
    {
        if (other is null)
        {
            return false;
        }
        return string.Equals(foundation, other.foundation, StringComparison.Ordinal)
            && component == other.component
            && protocol == other.protocol
            && priority == other.priority
            && string.Equals(address, other.address, StringComparison.Ordinal)
            && port == other.port
            && type == other.type
            && tcpType == other.tcpType
            && string.Equals(relatedAddress, other.relatedAddress, StringComparison.Ordinal)
            && relatedPort == other.relatedPort;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current RTCIceCandidate.
    /// </summary>
    /// <param name="obj">The object to compare with the current candidate.</param>
    /// <returns>true if the specified object is equal to the current candidate; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is RTCIceCandidate candidate && Equals(candidate);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current candidate.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(foundation);
        hash.Add(component);
        hash.Add(protocol);
        hash.Add(priority);
        hash.Add(address);
        hash.Add(port);
        hash.Add(type);
        hash.Add(tcpType);
        hash.Add(relatedAddress);
        hash.Add(relatedPort);
        return hash.ToHashCode();
    }
}
