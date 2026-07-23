//-----------------------------------------------------------------------------
// Filename: DtlsSrtpTransport.cs
//
// Description: This class represents the DTLS SRTP transport connection to use 
// as Client or Server.
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
// 02 Jul 2020  Aaron Clauson   Switched underlying transport from socket to
//                              piped memory stream.
// 30 Dec 2025  Lukas Volf      New DTLS/SRTP impl
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Tls;
using SIPSorcery.Net.SharpSRTP.DTLS;
using SIPSorcery.Net.SharpSRTP.DTLSSRTP;
using SIPSorcery.Net.SharpSRTP.SRTP;

namespace SIPSorcery.Net;

public delegate void OnDataReadyEvent(byte[] data);
public delegate void OnDtlsAlertEvent(TlsAlertLevelsEnum alertLevel, TlsAlertTypesEnum alertType, string alertDescription);

public class DtlsSrtpTransport : DatagramTransport
{
    public const int MAXIMUM_MTU = 1472; // 1500 - 20 (IP) - 8 (UDP)
    public const int DTLS_RETRANSMISSION_CODE = -1;

    private IDtlsSrtpPeer _connection;

    private ConcurrentQueue<byte[]> _data = new ConcurrentQueue<byte[]>();
    private Certificate? _peerCertificate;

    public DatagramTransport? Transport { get; internal set; }
    public bool IsClient { get { return _connection is DtlsSrtpClient; } }
    public SrtpKeys? Keys { get; private set; }

    public ThreadSafeSrtpSessionContext? Context { get; private set; }

    public int TimeoutMilliseconds { get { return _connection.TimeoutMilliseconds; } set { _connection.TimeoutMilliseconds = value; } }

    public event OnDataReadyEvent? OnDataReady;

    public event OnDtlsAlertEvent? OnAlert;

    public DtlsSrtpTransport(IDtlsSrtpPeer connection)
    {
        this._connection = connection;
        this._connection.OnSessionStarted += DtlsSrtpTransport_OnSessionStarted;
        this._connection.OnAlert += DtlsSrtpTransport_OnAlert;
    }

    private void DtlsSrtpTransport_OnSessionStarted(object? sender, DtlsSessionStartedEventArgs e)
    {
        this._peerCertificate = e.PeerCertificate;
        this.Context = new ThreadSafeSrtpSessionContext(e.Context);
    }

    private void DtlsSrtpTransport_OnAlert(object? sender, DtlsAlertEventArgs args)
    {
        OnAlert?.Invoke(args.Level, args.AlertType, args.Description);
    }

    public bool DoHandshake(out string? handshakeError)
    {
        var transport = _connection.DoHandshake(out handshakeError, this, null);
        Transport = transport;
        return string.IsNullOrEmpty(handshakeError);
    }

    public bool IsHandshakeComplete()
    {
        return Transport is not null;
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
            || outputSegment is not { Offset: 0, Array: { } outputArray })
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
            || outputSegment is not { Offset: 0, Array: { } outputArray })
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
            || outputSegment is not { Offset: 0, Array: { } outputArray })
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
            || outputSegment is not { Offset: 0, Array: { } outputArray })
        {
            throw new ArgumentException("The output memory must be backed by an array with 0 offset.", nameof(output));
        }
        var result = Context.UnprotectRtcp(payloadSegment, outputArray, out outputBufferLength);
#endif

        return result;
    }

    public Certificate? GetRemoteCertificate()
    {
        return _peerCertificate;
    }

    public int GetReceiveLimit() => MAXIMUM_MTU;

    public int GetSendLimit() => MAXIMUM_MTU;

    // TODO: Optimize to avoid array copy.
    public void WriteToRecvStream(byte[] buffer) // remoteEndPoint = "127.0.0.1:80"
    {
        _data.Enqueue(buffer);
    }

    public void Close()
    {
        var transport = Transport;
        if (transport != null)
        {
            Transport = null;
            transport.Close();
        }
    }

    public int Receive(byte[] buf, int off, int len, int waitMillis)
    {
        var t = 0L;
        while (true)
        {
            if (_data.TryDequeue(out var data))
            {
                Buffer.BlockCopy(data, 0, buf, off, data.Length);
                return data.Length;
            }
            else
            {
                System.Threading.Thread.Sleep(25);
                t += 25;
                if (t > waitMillis)
                {
                    return -1;
                }
            }
        }
    }

    public void Send(byte[] buf, int off, int len)
    {
        if (OnDataReady is not { } onDataReady)
        {
            return;
        }

        if (off != 0 || len < buf.Length)
        {
            buf = buf.AsSpan(off, len).ToArray();
        }

        onDataReady(buf);
    }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    public int Receive(Span<byte> buffer, int waitMillis)
    {
        byte[] buff = buffer.ToArray();
        int len = Receive(buff, 0, buff.Length, waitMillis);
        if (len > 0)
        {
            buff.AsSpan(0, len).CopyTo(buffer);
        }
        return len;
    }

    public void Send(ReadOnlySpan<byte> buffer)
    {
        Send(buffer.ToArray(), 0, buffer.Length);
    }
#endif
}
