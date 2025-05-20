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
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class DtlsSrtpTransport : DatagramTransport, IDisposable
{
    public const int DEFAULT_RETRANSMISSION_WAIT_MILLIS = 100;
    public const int DEFAULT_MTU = 1500;
    public const int MIN_IP_OVERHEAD = 20;
    public const int MAX_IP_OVERHEAD = MIN_IP_OVERHEAD + 64;
    public const int UDP_OVERHEAD = 8;
    public const int DEFAULT_TIMEOUT_MILLISECONDS = 20000;
    public const int DTLS_RETRANSMISSION_CODE = -1;
    public const int DTLS_RECEIVE_ERROR_CODE = -2;

    private static readonly ILogger logger = Log.Logger;

    private static readonly Random random = new Random();

    private IPacketTransformer? srtpEncoder;
    private IPacketTransformer? srtpDecoder;
    private IPacketTransformer? srtcpEncoder;
    private IPacketTransformer? srtcpDecoder;
    private IDtlsSrtpPeer connection;

    /// <summary>The collection of chunks to be written.</summary>
    private BlockingCollection<byte[]> _chunks = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

    public DtlsTransport? Transport { get; private set; }

    /// <summary>
    /// Sets the period in milliseconds that the handshake attempt will timeout
    /// after.
    /// </summary>
    public int TimeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS;

    /// <summary>
    /// Sets the period in milliseconds that receive will wait before try retransmission
    /// </summary>
    public int RetransmissionMilliseconds = DEFAULT_RETRANSMISSION_WAIT_MILLIS;

    public ReadOnlyMemoryAction<byte>? OnDataReady;

    /// <summary>
    /// Parameters:
    ///  - alert level,
    ///  - alert type,
    ///  - alert description.
    /// </summary>
    public event Action<AlertLevelsEnum, AlertTypesEnum, string>? OnAlert;

    private System.DateTime _startTime = System.DateTime.MinValue;
    private bool _isClosed;

    // Network properties
    private int _waitMillis = DEFAULT_RETRANSMISSION_WAIT_MILLIS;
    private int _mtu;
    private int _receiveLimit;
    private int _sendLimit;

    private volatile bool _handshakeComplete;
    private volatile bool _handshakeFailed;
    private volatile bool _handshaking;

    public DtlsSrtpTransport(IDtlsSrtpPeer connection, int mtu = DEFAULT_MTU)
    {
        // Network properties
        this._mtu = mtu;
        this._receiveLimit = System.Math.Max(0, mtu - MIN_IP_OVERHEAD - UDP_OVERHEAD);
        this._sendLimit = System.Math.Max(0, mtu - MAX_IP_OVERHEAD - UDP_OVERHEAD);
        this.connection = connection;

        connection.OnAlert += (level, type, description) => OnAlert?.Invoke(level, type, description);
    }

    public IPacketTransformer? SrtpDecoder => srtpDecoder;

    public IPacketTransformer? SrtpEncoder => srtpEncoder;

    public IPacketTransformer? SrtcpDecoder => srtcpDecoder;

    public IPacketTransformer? SrtcpEncoder => srtcpEncoder;

    public bool IsHandshakeComplete()
    {
        return _handshakeComplete;
    }

    public bool IsHandshakeFailed()
    {
        return _handshakeFailed;
    }

    public bool IsHandshaking()
    {
        return _handshaking;
    }

    public bool DoHandshake(out string? handshakeError)
    {
        if (connection.IsClient())
        {
            return DoHandshakeAsClient(out handshakeError);
        }
        else
        {
            return DoHandshakeAsServer(out handshakeError);
        }
    }

    public bool IsClient
    {
        get { return connection.IsClient(); }
    }

    private bool DoHandshakeAsClient(out string? handshakeError)
    {
        handshakeError = null;

        logger.LogDtlsHandshakeStartUnchecked("client");

        if (!_handshaking && !_handshakeComplete)
        {
            this._waitMillis = RetransmissionMilliseconds;
            this._startTime = System.DateTime.Now;
            this._handshaking = true;
            var clientProtocol = new DtlsClientProtocol();
            try
            {
                var client = (DtlsSrtpClient)connection;
                // Perform the handshake in a non-blocking fashion
                Transport = clientProtocol.Connect(client, this);

                // Prepare the shared key to be used in RTP streaming
                //client.PrepareSrtpSharedSecret();
                // Generate encoders for DTLS traffic
                if (client.GetSrtpPolicy() is { })
                {
                    srtpDecoder = GenerateRtpDecoder();
                    srtpEncoder = GenerateRtpEncoder();
                    srtcpDecoder = GenerateRtcpDecoder();
                    srtcpEncoder = GenerateRtcpEncoder();
                }
                // Declare handshake as complete
                _handshakeComplete = true;
                _handshakeFailed = false;
                _handshaking = false;
                // Warn listeners handshake completed
                //UnityEngine.Debug.Log("DTLS Handshake Completed");

                return true;
            }
            catch (System.Exception excp)
            {
                if (excp.InnerException is TimeoutException)
                {
                    logger.LogDtlsHandshakeTimeout("client", excp);
                    handshakeError = "timeout";
                }
                else
                {
                    handshakeError = "unknown";
                    if (excp is Org.BouncyCastle.Tls.TlsFatalAlert tlsFatalAlert)
                    {
                        handshakeError = tlsFatalAlert.Message;
                    }

                    logger.LogDtlsHandshakeFailed("client", excp.Message, excp);
                }

                // Declare handshake as failed
                _handshakeComplete = false;
                _handshakeFailed = true;
                _handshaking = false;
                // Warn listeners handshake completed
                //UnityEngine.Debug.Log("DTLS Handshake failed\n" + e);
            }
        }
        return false;
    }

    private bool DoHandshakeAsServer(out string? handshakeError)
    {
        handshakeError = null;

        logger.LogDtlsHandshakeStartUnchecked("server");

        if (!_handshaking && !_handshakeComplete)
        {
            this._waitMillis = RetransmissionMilliseconds;
            this._startTime = System.DateTime.Now;
            this._handshaking = true;
            var serverProtocol = new DtlsServerProtocol();
            try
            {
                var server = (DtlsSrtpServer)connection;

                // Perform the handshake in a non-blocking fashion
                Transport = serverProtocol.Accept(server, this);
                // Prepare the shared key to be used in RTP streaming
                //server.PrepareSrtpSharedSecret();
                // Generate encoders for DTLS traffic
                if (server.GetSrtpPolicy() is { })
                {
                    srtpDecoder = GenerateRtpDecoder();
                    srtpEncoder = GenerateRtpEncoder();
                    srtcpDecoder = GenerateRtcpDecoder();
                    srtcpEncoder = GenerateRtcpEncoder();
                }
                // Declare handshake as complete
                _handshakeComplete = true;
                _handshakeFailed = false;
                _handshaking = false;
                // Warn listeners handshake completed
                //UnityEngine.Debug.Log("DTLS Handshake Completed");
                return true;
            }
            catch (System.Exception excp)
            {
                if (excp.InnerException is TimeoutException)
                {
                    logger.LogDtlsHandshakeTimeout("server", excp);
                    handshakeError = "timeout";
                }
                else
                {
                    handshakeError = "unknown";
                    if (excp is Org.BouncyCastle.Tls.TlsFatalAlert tlsFatalAlert)
                    {
                        handshakeError = tlsFatalAlert.Message;
                    }

                    logger.LogDtlsHandshakeFailed("server", excp.Message, excp);
                }

                // Declare handshake as failed
                _handshakeComplete = false;
                _handshakeFailed = true;
                _handshaking = false;
                // Warn listeners handshake completed
                //UnityEngine.Debug.Log("DTLS Handshake failed\n"+ e);
            }
        }
        return false;
    }

    public Certificate? GetRemoteCertificate() => connection.GetRemoteCertificate();

    protected byte[]? GetMasterServerKey() => connection.GetSrtpMasterServerKey();

    protected byte[]? GetMasterServerSalt() => connection.GetSrtpMasterServerSalt();

    protected byte[]? GetMasterClientKey() => connection.GetSrtpMasterClientKey();

    protected byte[]? GetMasterClientSalt() => connection.GetSrtpMasterClientSalt();

    protected SrtpPolicy? GetSrtpPolicy() => connection.GetSrtpPolicy();

    protected SrtpPolicy? GetSrtcpPolicy() => connection.GetSrtcpPolicy();

    protected IPacketTransformer GenerateRtpEncoder() => GenerateTransformer(connection.IsClient(), true);

    protected IPacketTransformer GenerateRtpDecoder() =>
        //Generate the reverse result of "GenerateRtpEncoder"
        GenerateTransformer(!connection.IsClient(), true);

    protected IPacketTransformer GenerateRtcpEncoder()
    {
        var isClient = connection is DtlsSrtpClient;
        return GenerateTransformer(connection.IsClient(), false);
    }

    protected IPacketTransformer GenerateRtcpDecoder() =>
        //Generate the reverse result of "GenerateRctpEncoder"
        GenerateTransformer(!connection.IsClient(), false);

    protected IPacketTransformer GenerateTransformer(bool isClient, bool isRtp)
    {
        var srtpPolicy = GetSrtpPolicy();
        var srtcpPolicy = GetSrtcpPolicy();
        Debug.Assert(srtpPolicy is { });
        Debug.Assert(srtcpPolicy is { });

        SrtpTransformEngine engine;
        if (!isClient)
        {
            var masterKey = GetMasterServerKey();
            var masterSalt = GetMasterServerSalt();
            Debug.Assert(masterKey is { });
            Debug.Assert(masterSalt is { });
            engine = new SrtpTransformEngine(masterKey, masterSalt, srtpPolicy, srtcpPolicy);
        }
        else
        {
            var masterKey = GetMasterClientKey();
            var masterSalt = GetMasterClientSalt();
            Debug.Assert(masterKey is { });
            Debug.Assert(masterSalt is { });
            engine = new SrtpTransformEngine(masterKey, masterSalt, srtpPolicy, srtcpPolicy);
        }

        if (isRtp)
        {
            return engine.GetRTPTransformer();
        }
        else
        {
            return engine.GetRTCPTransformer();
        }
    }

    public byte[]? UnprotectRTP(byte[] packet, int offset, int length)
    {
        Debug.Assert(this.srtpDecoder is { });

        lock (this.srtpDecoder)
        {
            return this.srtpDecoder.ReverseTransform(packet, offset, length);
        }
    }

    public int UnprotectRTP(byte[] payload, int length, out int outLength)
    {
        var result = UnprotectRTP(payload, 0, length);

        if (result is null)
        {
            outLength = 0;
            return -1;
        }

        System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
        outLength = result.Length;

        return 0; //No Errors
    }

    public byte[]? ProtectRTP(byte[] packet, int offset, int length)
    {
        Debug.Assert(this.srtpEncoder is { });

        lock (this.srtpEncoder)
        {
            return this.srtpEncoder.Transform(packet, offset, length);
        }
    }

    public int ProtectRTP(byte[] payload, int length, out int outLength)
    {
        var result = ProtectRTP(payload, 0, length);

        if (result is null)
        {
            outLength = 0;
            return -1;
        }

        System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
        outLength = result.Length;

        return 0; //No Errors
    }

    public byte[]? UnprotectRTCP(byte[] packet, int offset, int length)
    {
        Debug.Assert(this.srtcpDecoder is { });

        lock (this.srtcpDecoder)
        {
            return this.srtcpDecoder.ReverseTransform(packet, offset, length);
        }
    }

    public int UnprotectRTCP(byte[] payload, int length, out int outLength)
    {
        var result = UnprotectRTCP(payload, 0, length);
        if (result is null)
        {
            outLength = 0;
            return -1;
        }

        System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
        outLength = result.Length;

        return 0; //No Errors
    }

    public byte[]? ProtectRTCP(byte[] packet, int offset, int length)
    {
        Debug.Assert(this.srtcpEncoder is { });

        lock (this.srtcpEncoder)
        {
            return this.srtcpEncoder.Transform(packet, offset, length);
        }
    }

    public int ProtectRTCP(byte[] payload, int length, out int outLength)
    {
        var result = ProtectRTCP(payload, 0, length);
        if (result is null)
        {
            outLength = 0;
            return -1;
        }

        System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
        outLength = result.Length;

        return 0; //No Errors
    }

    /// <summary>
    /// Returns the number of milliseconds remaining until a timeout occurs.
    /// </summary>
    private int GetMillisecondsRemaining()
    {
        return TimeoutMilliseconds - (int)(System.DateTime.Now - this._startTime).TotalMilliseconds;
    }

    public int GetReceiveLimit()
    {
        return this._receiveLimit;
    }

    public int GetSendLimit()
    {
        return this._sendLimit;
    }

    public void WriteToRecvStream(ReadOnlySpan<byte> buf)
    {
        if (!_isClosed)
        {
            _chunks.Add(buf.ToArray());
        }
    }

    /// <summary>
    /// Reads a chunk from the internal buffer into the provided span.
    /// </summary>
    /// <param name="buffer">The span to copy data into.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <returns>Number of bytes read, or DTLS_RETRANSMISSION_CODE on timeout/error.</returns>
    private int Read(Span<byte> buffer, int timeout)
    {
        try
        {
            if (_isClosed)
            {
                throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
                //return DTLS_RECEIVE_ERROR_CODE;
            }
            else if (_chunks.TryTake(out var item, timeout))
            {
                var copyLen = Math.Min(item.Length, buffer.Length);
                item.AsSpan(0, copyLen).CopyTo(buffer);
                return copyLen;
            }
        }
        catch (ObjectDisposedException) { }
        catch (ArgumentNullException) { }

        return DTLS_RETRANSMISSION_CODE;
    }

    public int Receive(Span<byte> buf, int waitMillis)
    {
        if (!_handshakeComplete)
        {
            if (_isClosed)
            {
                throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
            }
            // The timeout for the handshake applies from when it started rather than
            // for each individual receive..
            var millisecondsRemaining = GetMillisecondsRemaining();

            //Handle DTLS 1.3 Retransmission time (100 to 6000 ms)
            //https://tools.ietf.org/id/draft-ietf-tls-dtls13-31.html#rfc.section.5.7
            //As HandshakeReliable class contains too long hardcoded initial waitMillis (1000 ms) we must control this internally
            //PS: Random extra delta time guarantee that work in local networks.
            waitMillis = _waitMillis + random.Next(5, 25);

            if (millisecondsRemaining <= 0)
            {
                logger.LogDtlsHandshakeTimedOut(TimeoutMilliseconds, connection.IsClient() ? "server" : "client");
                throw new TimeoutException();
            }
            else
            {
                waitMillis = Math.Min(waitMillis, millisecondsRemaining);
                var receiveLen = Read(buf, waitMillis);

                //Handle DTLS 1.3 Retransmission time (100 to 6000 ms)
                //https://tools.ietf.org/id/draft-ietf-tls-dtls13-31.html#rfc.section.5.7
                if (receiveLen == DTLS_RETRANSMISSION_CODE)
                {
                    _waitMillis = BackOff(_waitMillis);
                }
                else
                {
                    _waitMillis = RetransmissionMilliseconds;
                }

                return receiveLen;
            }
        }
        else if (!_isClosed)
        {
            return Read(buf, waitMillis);
        }
        else
        {
            //throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
            return DTLS_RECEIVE_ERROR_CODE;
        }
    }

    public int Receive(byte[] buf, int off, int len, int waitMillis)
        => Receive(new Span<byte>(buf, off, len), waitMillis);

    public void Send(byte[] buf, int off, int len) => Send(new ReadOnlySpan<byte>(buf, off, len));

    /// <summary>
    /// Sends data from the provided buffer span using ArrayPool for efficient allocation.
    /// Only sends if the buffer is not empty.
    /// </summary>
    /// <param name="buf">The buffer span containing the data to send.</param>
    public void Send(ReadOnlySpan<byte> buf)
    {
        if (buf.IsEmpty)
        {
            return;
        }

        var pool = System.Buffers.ArrayPool<byte>.Shared;
        var rented = pool.Rent(buf.Length);
        try
        {
            buf.CopyTo(rented);
            OnDataReady?.Invoke(rented.AsMemory(0, buf.Length));
        }
        finally
        {
            pool.Return(rented);
        }
    }

    public virtual void Close()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            this._startTime = System.DateTime.MinValue;
            this._chunks?.Dispose();
            Transport?.Close();
        }
    }

    /// <summary>
    /// Close the transport if the instance is out of scope.
    /// </summary>
    protected void Dispose(bool disposing)
    {
        if (!_isClosed)
        {
            Close();
        }
    }

    /// <summary>
    /// Close the transport if the instance is out of scope.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Handle retransmission time based in DTLS 1.3 
    /// </summary>
    /// <param name="currentWaitMillis"></param>
    /// <returns></returns>
    protected virtual int BackOff(int currentWaitMillis)
    {
        return System.Math.Min(currentWaitMillis * 2, 6000);
    }
}
