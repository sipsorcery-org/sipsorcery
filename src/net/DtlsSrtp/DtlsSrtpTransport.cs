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
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Security;
using SIPSorcery.Sys;
using System.Buffers;

namespace SIPSorcery.Net
{
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

        private IPacketTransformer srtpEncoder;
        private IPacketTransformer srtpDecoder;
        private IPacketTransformer srtcpEncoder;
        private IPacketTransformer srtcpDecoder;
        IDtlsSrtpPeer connection = null;

        /// <summary>The collection of chunks to be written.</summary>
        private BlockingCollection<ArraySegment<byte>> _chunks = new(new ConcurrentQueue<ArraySegment<byte>>());

        public DtlsTransport Transport { get; private set; }

        /// <summary>
        /// Sets the period in milliseconds that the handshake attempt will timeout
        /// after.
        /// </summary>
        public int TimeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS;

        /// <summary>
        /// Sets the period in milliseconds that receive will wait before try retransmission
        /// </summary>
        public int RetransmissionMilliseconds = DEFAULT_RETRANSMISSION_WAIT_MILLIS;

        public delegate void OnBytesReadyDelegate(ReadOnlySpan<byte> bytes);
        public OnBytesReadyDelegate OnDataReady;

        /// <summary>
        /// Parameters:
        ///  - alert level,
        ///  - alert type,
        ///  - alert description.
        /// </summary>
        public event Action<AlertLevelsEnum, AlertTypesEnum, string> OnAlert;

        private System.DateTime _startTime = System.DateTime.MinValue;
        private Once _isClosed;

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

        public IPacketTransformer SrtpDecoder
        {
            get
            {
                return srtpDecoder;
            }
        }

        public IPacketTransformer SrtpEncoder
        {
            get
            {
                return srtpEncoder;
            }
        }

        public IPacketTransformer SrtcpDecoder
        {
            get
            {
                return srtcpDecoder;
            }
        }

        public IPacketTransformer SrtcpEncoder
        {
            get
            {
                return srtcpEncoder;
            }
        }

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

        public bool DoHandshake(out string handshakeError)
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

        private bool DoHandshakeAsClient(out string handshakeError)
        {
            handshakeError = null;

            logger.LogDebug("DTLS commencing handshake as client.");

            if (!_handshaking && !_handshakeComplete)
            {
                this._waitMillis = RetransmissionMilliseconds;
                this._startTime = System.DateTime.Now;
                this._handshaking = true;
                DtlsClientProtocol clientProtocol = new DtlsClientProtocol();
                try
                {
                    var client = (DtlsSrtpClient)connection;
                    // Perform the handshake in a non-blocking fashion
                    Transport = clientProtocol.Connect(client, this);

                    // Prepare the shared key to be used in RTP streaming
                    //client.PrepareSrtpSharedSecret();
                    // Generate encoders for DTLS traffic
                    if (client.GetSrtpPolicy() != null)
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
                        logger.LogWarning(excp, $"DTLS handshake as client timed out waiting for handshake to complete.");
                        handshakeError = "timeout";
                    }
                    else
                    {
                        handshakeError = "unknown";
                        if (excp is Org.BouncyCastle.Tls.TlsFatalAlert)
                        {
                            handshakeError = (excp as Org.BouncyCastle.Tls.TlsFatalAlert).Message;
                        }

                        logger.LogWarning(excp, $"DTLS handshake as client failed. {excp.Message}");
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

        private bool DoHandshakeAsServer(out string handshakeError)
        {
            handshakeError = null;

            logger.LogDebug("DTLS commencing handshake as server.");

            if (!_handshaking && !_handshakeComplete)
            {
                this._waitMillis = RetransmissionMilliseconds;
                this._startTime = System.DateTime.Now;
                this._handshaking = true;
                DtlsServerProtocol serverProtocol = new DtlsServerProtocol();
                try
                {
                    var server = (DtlsSrtpServer)connection;

                    // Perform the handshake in a non-blocking fashion
                    Transport = serverProtocol.Accept(server, this);
                    // Prepare the shared key to be used in RTP streaming
                    //server.PrepareSrtpSharedSecret();
                    // Generate encoders for DTLS traffic
                    if (server.GetSrtpPolicy() != null)
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
                        logger.LogWarning(excp, $"DTLS handshake as server timed out waiting for handshake to complete.");
                        handshakeError = "timeout";
                    }
                    else
                    {
                        handshakeError = "unknown";
                        if (excp is Org.BouncyCastle.Tls.TlsFatalAlert)
                        {
                            handshakeError = (excp as Org.BouncyCastle.Tls.TlsFatalAlert).Message;
                        }

                        logger.LogWarning(excp, $"DTLS handshake as server failed. {excp.Message}");
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

        public Certificate GetRemoteCertificate()
        {
            return connection.GetRemoteCertificate();
        }

        protected byte[] GetMasterServerKey()
        {
            return connection.GetSrtpMasterServerKey();
        }

        protected byte[] GetMasterServerSalt()
        {
            return connection.GetSrtpMasterServerSalt();
        }

        protected byte[] GetMasterClientKey()
        {
            return connection.GetSrtpMasterClientKey();
        }

        protected byte[] GetMasterClientSalt()
        {
            return connection.GetSrtpMasterClientSalt();
        }

        protected SrtpPolicy GetSrtpPolicy()
        {
            return connection.GetSrtpPolicy();
        }

        protected SrtpPolicy GetSrtcpPolicy()
        {
            return connection.GetSrtcpPolicy();
        }

        protected IPacketTransformer GenerateRtpEncoder()
        {
            return GenerateTransformer(connection.IsClient(), true);
        }

        protected IPacketTransformer GenerateRtpDecoder()
        {
            //Generate the reverse result of "GenerateRtpEncoder"
            return GenerateTransformer(!connection.IsClient(), true);
        }

        protected IPacketTransformer GenerateRtcpEncoder()
        {
            var isClient = connection is DtlsSrtpClient;
            return GenerateTransformer(connection.IsClient(), false);
        }

        protected IPacketTransformer GenerateRtcpDecoder()
        {
            //Generate the reverse result of "GenerateRctpEncoder"
            return GenerateTransformer(!connection.IsClient(), false);
        }

        protected IPacketTransformer GenerateTransformer(bool isClient, bool isRtp)
        {
            SrtpTransformEngine engine = null;
            if (!isClient)
            {
                engine = new SrtpTransformEngine(GetMasterServerKey(), GetMasterServerSalt(), GetSrtpPolicy(), GetSrtcpPolicy());
            }
            else
            {
                engine = new SrtpTransformEngine(GetMasterClientKey(), GetMasterClientSalt(), GetSrtpPolicy(), GetSrtcpPolicy());
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

        public byte[] UnprotectRTP(Span<byte> packet, int offset, int length)
        {
            lock (this.srtpDecoder)
            {
                return this.srtpDecoder.ReverseTransform(packet, offset, length);
            }
        }

        public int UnprotectRTP(Span<byte> payload, int length, out int outLength)
        {
            var result = UnprotectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            result.AsSpan().CopyTo(payload);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTP(Span<byte> packet, int offset, int length)
        {
            lock (this.srtpEncoder)
            {
                return this.srtpEncoder.Transform(packet, offset, length);
            }
        }

        public int ProtectRTP(Span<byte> payload, int length, out int outLength)
        {
            var result = ProtectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            result.AsSpan().CopyTo(payload);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] UnprotectRTCP(Span<byte> packet, int offset, int length)
        {
            lock (this.srtcpDecoder)
            {
                return this.srtcpDecoder.ReverseTransform(packet, offset, length);
            }
        }

        public int UnprotectRTCP(Span<byte> payload, int length, out int outLength)
        {
            var result = UnprotectRTCP(payload, 0, length);
            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            result.AsSpan().CopyTo(payload);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTCP(Span<byte> packet, int offset, int length)
        {
            lock (this.srtcpEncoder)
            {
                return this.srtcpEncoder.Transform(packet, offset, length);
            }
        }

        public int ProtectRTCP(Span<byte> payload, int length, out int outLength)
        {
            var result = ProtectRTCP(payload, 0, length);
            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            result.AsSpan().CopyTo(payload);
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
            if (!_isClosed.HasOccurred)
            {
                var chunk = ArrayPool<byte>.Shared.Rent(buf.Length);
                buf.CopyTo(chunk);
                try
                {
                    _chunks.Add(new(chunk, 0, buf.Length));
                }
                catch (Exception) when (_isClosed.HasOccurred)
                {
                    ArrayPool<byte>.Shared.Return(chunk);
                }
            }
        }

        private ArraySegment<byte> _partialChunk = default;
        private int _partialChunkOffset = 0;
        private int Read(byte[] buffer, int offset, int count, int timeout)
        {
            try
            {
                if (_isClosed.HasOccurred)
                {
                    throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
                    //return DTLS_RECEIVE_ERROR_CODE;
                }
                else if (_partialChunk.Array != null)
                {
                    int bytesToCopy = Math.Min(count, _partialChunk.Count - _partialChunkOffset);
                    Buffer.BlockCopy(_partialChunk.Array, _partialChunkOffset, buffer, offset, bytesToCopy);
                    _partialChunkOffset += bytesToCopy;

                    if (_partialChunkOffset == _partialChunk.Count)
                    {
                        ArrayPool<byte>.Shared.Return(_partialChunk.Array);
                        _partialChunk = default;
                        _partialChunkOffset = 0;
                    }

                    return bytesToCopy;
                }
                else if (_chunks.TryTake(out var item, timeout))
                {
                    int bytesToCopy = Math.Min(count, item.Count);
                    Buffer.BlockCopy(item.Array, 0, buffer, offset, bytesToCopy);
                    if (bytesToCopy < item.Count)
                    {
                        _partialChunk = item;
                        _partialChunkOffset = bytesToCopy;
                    }
                    else
                    {
                        ArrayPool<byte>.Shared.Return(item.Array);
                    }
                    return bytesToCopy;
                }
            }
            catch (ObjectDisposedException) { }
            catch (ArgumentNullException) { }

            return DTLS_RETRANSMISSION_CODE;
        }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public int Receive(Span<byte> buf, int waitMillis)
        {
            throw new NotImplementedException();
            return Receive(buf.ToArray(), 0, buf.Length, waitMillis);
        }
#endif

        public int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            if (!_handshakeComplete)
            {
                // The timeout for the handshake applies from when it started rather than
                // for each individual receive..
                int millisecondsRemaining = GetMillisecondsRemaining();

                //Handle DTLS 1.3 Retransmission time (100 to 6000 ms)
                //https://tools.ietf.org/id/draft-ietf-tls-dtls13-31.html#rfc.section.5.7
                //As HandshakeReliable class contains too long hardcoded initial waitMillis (1000 ms) we must control this internally
                //PS: Random extra delta time guarantee that work in local networks.
                waitMillis = _waitMillis + random.Next(5, 25);

                if (millisecondsRemaining <= 0)
                {
                    logger.LogWarning($"DTLS transport timed out after {TimeoutMilliseconds}ms waiting for handshake from remote {(connection.IsClient() ? "server" : "client")}.");
                    throw new TimeoutException();
                }
                else if (!_isClosed.HasOccurred)
                {
                    waitMillis = Math.Min(waitMillis, millisecondsRemaining);
                    var receiveLen = Read(buf, off, len, waitMillis);

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
                else
                {
                    throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
                    //return DTLS_RECEIVE_ERROR_CODE;
                }
            }
            else if (!_isClosed.HasOccurred)
            {
                return Read(buf, off, len, waitMillis);
            }
            else
            {
                throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
                //return DTLS_RECEIVE_ERROR_CODE;
            }
        }

        public void Send(byte[] buf, int off, int len)
        {
            if (len != buf.Length)
            {
                // Only create a new buffer and copy bytes if the length is different
                var tempBuf = new byte[len];
                Buffer.BlockCopy(buf, off, tempBuf, 0, len);
                buf = tempBuf;
            }

            OnDataReady?.Invoke(buf);
        }
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public void Send(ReadOnlySpan<byte> buf)
        {
            OnDataReady?.Invoke(buf);
        }
#endif


        public virtual void Close()
        {
            if (!_isClosed.TryMarkOccurred())
            {
                return;
            }

            this._startTime = System.DateTime.MinValue;
            _chunks.CompleteAdding();
            foreach(var chunk in _chunks.GetConsumingEnumerable())
            {
                ArrayPool<byte>.Shared.Return(chunk.Array);
            }
            if (_partialChunk.Array is { } partialChunk)
            {
                ArrayPool<byte>.Shared.Return(partialChunk);
            }
            this._chunks?.Dispose();
            Transport?.Close();
        }

        /// <summary>
        /// Close the transport if the instance is out of scope.
        /// </summary>
        protected void Dispose(bool disposing)
        {
            if (!_isClosed.HasOccurred)
            {
                Close();
            }
        }

        /// <summary>
        /// Close the transport if the instance is out of scope.
        /// </summary>
        public void Dispose()
        {
            if (!_isClosed.HasOccurred)
            {
                Close();
            }
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
}