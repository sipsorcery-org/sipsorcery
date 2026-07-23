//-----------------------------------------------------------------------------
// Filename: SrtpHandler.cs
//
// Description: This class represents the SRTP handling for SIP calls
//
// Author(s):
// Kurt Kießling 
//
// History:
// 20 Jul 2021	Kurt Kießling	Created.
// 30 Dec 2025  Lukas Volf      New DTLS/SRTP impl
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SIPSorcery.Net.SharpSRTP.SRTP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Net;

public sealed class SrtpHandler
{
    private List<SDPSecurityDescription>? _localSecurityDescriptions;
    private List<SDPSecurityDescription>? _remoteSecurityDescriptions;

    public ThreadSafeSrtpSessionContext? Context { get; private set; }

    public bool IsNegotiationComplete { get; private set; }
    public SDPSecurityDescription? LocalSecurityDescription { get; private set; }
    public SDPSecurityDescription? RemoteSecurityDescription { get; private set; }

    public SrtpHandler()
    {
    }

    public int ProtectRTP(ReadOnlyMemory<byte> payload, Memory<byte> output, out int outputBufferLength)
    {
        Debug.Assert(Context is not null);

#if NET8_0_OR_GREATER
        var result = Context.ProtectRtp(payload.Span, output.Span, out outputBufferLength);
#else
        if (!MemoryMarshal.TryGetArray(payload, out var payloadSegment))
        {
            throw new ArgumentException("The payload memory must be backed by an array.", nameof(payload));
        }
        if (!MemoryMarshal.TryGetArray(output, out ArraySegment<byte> outputSegment)
            || outputSegment is not {Offset: 0, Array: { } outputArray })
        {
            throw new ArgumentException("The output memory must be backed by an array with 0 offset.", nameof(output));
        }
        var result = Context.ProtectRtp(payloadSegment, outputArray, out outputBufferLength);
#endif

        return result;
    }

    public int UnprotectRTP(ReadOnlyMemory<byte> payload, Memory<byte> output, out int outputBufferLength)
    {
        Debug.Assert(Context is not null);

#if NET8_0_OR_GREATER
        var result = Context.UnprotectRtp(payload.Span, output.Span, out outputBufferLength);
#else
        if (!MemoryMarshal.TryGetArray(payload, out var payloadSegment))
        {
            throw new ArgumentException("The payload memory must be backed by an array.", nameof(payload));
        }
        if (!MemoryMarshal.TryGetArray(output, out ArraySegment<byte> outputSegment)
            || outputSegment is not {Offset: 0, Array: { } outputArray })
        {
            throw new ArgumentException("The output memory must be backed by an array with 0 offset.", nameof(output));
        }
        var result = Context.UnprotectRtp(payloadSegment, outputArray, out outputBufferLength);
#endif

        return result;
    }

    public int ProtectRTCP(ReadOnlyMemory<byte> payload, Memory<byte> output, out int outputBufferLength)
    {
        Debug.Assert(Context is not null);

#if NET8_0_OR_GREATER
        var result = Context.ProtectRtcp(payload.Span, output.Span, out outputBufferLength);
#else
        if (!MemoryMarshal.TryGetArray(payload, out var payloadSegment))
        {
            throw new ArgumentException("The payload memory must be backed by an array.", nameof(payload));
        }
        if (!MemoryMarshal.TryGetArray(output, out ArraySegment<byte> outputSegment)
            || outputSegment is not {Offset: 0, Array: { } outputArray })
        {
            throw new ArgumentException("The output memory must be backed by an array with 0 offset.", nameof(output));
        }
        var result = Context.ProtectRtcp(payloadSegment, outputArray, out outputBufferLength);
#endif

        return result;
    }

    public int UnprotectRTCP(ReadOnlyMemory<byte> payload, Memory<byte> output, out int outputBufferLength)
    {
        Debug.Assert(Context is not null);

#if NET8_0_OR_GREATER
        var result = Context.UnprotectRtcp(payload.Span, output.Span, out outputBufferLength);
#else
        if (!MemoryMarshal.TryGetArray(payload, out var payloadSegment))
        {
            throw new ArgumentException("The payload memory must be backed by an array.", nameof(payload));
        }
        if (!MemoryMarshal.TryGetArray(output, out ArraySegment<byte> outputSegment)
            || outputSegment is not {Offset: 0, Array: { } outputArray })
        {
            throw new ArgumentException("The output memory must be backed by an array with 0 offset.", nameof(output));
        }
        var result = Context.UnprotectRtcp(payloadSegment, outputArray, out outputBufferLength);
#endif

        return result;
    }

    public bool RemoteSecurityDescriptionUnchanged(List<SDPSecurityDescription> securityDescriptions)
    {
        if (LocalSecurityDescription is null || RemoteSecurityDescription is null)
        {
            return false;
        }

        var remoteCryptoSuite = FindSecurityDescriptionByCryptoSuite(securityDescriptions, LocalSecurityDescription.CryptoSuite);

        if (remoteCryptoSuite is null)
        {
            return false;
        }

        return remoteCryptoSuite.Equals(RemoteSecurityDescription);

        // Local method for finding security description by crypto suite
        static SDPSecurityDescription? FindSecurityDescriptionByCryptoSuite(List<SDPSecurityDescription> descriptions, SDPSecurityDescription.CryptoSuites cryptoSuite)
        {
            foreach (var description in descriptions)
            {
                if (description.CryptoSuite == cryptoSuite)
                {
                    return description;
                }
            }
            return null;
        }
    }

    public bool SetupLocal(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
    {
        _localSecurityDescriptions = securityDescriptions;

        if (sdpType == SdpType.offer)
        {
            IsNegotiationComplete = false;
            return true;
        }

        Debug.Assert(_remoteSecurityDescriptions is { });

        if (_remoteSecurityDescriptions.Count == 0)
        {
            throw new SipSorceryException("Setup local crypto failed. No crypto attribute in offer.");
        }

        if (_localSecurityDescriptions.Count == 0)
        {
            throw new SipSorceryException("Setup local crypto failed. No crypto attribute in answer.");
        }

        var localSecurityDescription = LocalSecurityDescription = _localSecurityDescriptions[0];
        var remoteSecurityDescription = RemoteSecurityDescription = GetFirstMatchingSecurityDescription(localSecurityDescription);

        if (remoteSecurityDescription is { } && remoteSecurityDescription.Tag == localSecurityDescription.Tag)
        {
            IsNegotiationComplete = true;

            Context = new ThreadSafeSrtpSessionContext(CreateSessionContext(localSecurityDescription, remoteSecurityDescription));

            return true;
        }

        return false;
    }

    public bool SetupRemote(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
    {
        _remoteSecurityDescriptions = securityDescriptions;

        if (sdpType == SdpType.offer)
        {
            IsNegotiationComplete = false;
            return true;
        }

        Debug.Assert(_localSecurityDescriptions is { });

        if (_localSecurityDescriptions.Count == 0)
        {
            throw new SipSorceryException("Setup remote crypto failed. No crypto attribute in offer.");
        }

        if (_remoteSecurityDescriptions.Count == 0)
        {
            throw new SipSorceryException("Setup remote crypto failed. No crypto attribute in answer.");
        }

        var remoteSecurityDescription = RemoteSecurityDescription = _remoteSecurityDescriptions[0];
        var localSecurityDescription = LocalSecurityDescription = GetFirstMatchingSecurityDescription(remoteSecurityDescription);

        if (localSecurityDescription is { } && localSecurityDescription.Tag == remoteSecurityDescription.Tag)
        {
            IsNegotiationComplete = true;

            Context = new ThreadSafeSrtpSessionContext(CreateSessionContext(localSecurityDescription, remoteSecurityDescription));

            return true;
        }

        return false;
    }

    private SrtpSessionContext CreateSessionContext(SDPSecurityDescription localSecurityDescription, SDPSecurityDescription remoteSecurityDescription, byte[]? mki = null)
    {
        // TODO: not tested
        var localProtectionProfile = SrtpProtocol.SrtpCryptoSuites[localSecurityDescription.CryptoSuite.ToStringFast()];
        var remoteProtectionProfile = SrtpProtocol.SrtpCryptoSuites[remoteSecurityDescription.CryptoSuite.ToStringFast()];

        var encodeRtpContext = new SrtpContext(SrtpContextType.RTP, localProtectionProfile, localSecurityDescription.KeyParams[0].Key, localSecurityDescription.KeyParams[0].Salt, mki);
        var encodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, localProtectionProfile, localSecurityDescription.KeyParams[0].Key, localSecurityDescription.KeyParams[0].Salt, mki);
        var decodeRtpContext = new SrtpContext(SrtpContextType.RTP, remoteProtectionProfile, remoteSecurityDescription.KeyParams[0].Key, remoteSecurityDescription.KeyParams[0].Salt, mki);
        var decodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, remoteProtectionProfile, remoteSecurityDescription.KeyParams[0].Key, remoteSecurityDescription.KeyParams[0].Salt, mki);

        return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
    }

    private SDPSecurityDescription? GetFirstMatchingSecurityDescription(SDPSecurityDescription other)
    {
        Debug.Assert(_remoteSecurityDescriptions is { });
        foreach (var desc in _remoteSecurityDescriptions)
        {
            if (desc.CryptoSuite == other.CryptoSuite)
            {
                return desc;
            }
        }

        return null;
    }
}
