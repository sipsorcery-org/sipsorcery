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
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using SIPSorcery.Sys;

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

        private IDataPacketTransformer srtpEncoder;
        private IDataPacketTransformer srtpDecoder;
        private IDataPacketTransformer srtcpEncoder;
        private IDataPacketTransformer srtcpDecoder;
        IDtlsSrtpPeer connection = null;

        /// <summary>The collection of chunks to be written.</summary>
        private BlockingCollection<byte[]> _chunks = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

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

        public Action<byte[]> OnDataReady;

        /// <summary>
        /// Parameters:
        ///  - alert level,
        ///  - alert type,
        ///  - alert description.
        /// </summary>
        public event Action<AlertLevels, AlertTypes, string> OnAlert;

        private System.DateTime _startTime = System.DateTime.MinValue;
        private bool _isClosed = false;

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

        public IDataPacketTransformer SrtpDecoder
        {
            get
            {
                return srtpDecoder;
            }
        }

        public IDataPacketTransformer SrtpEncoder
        {
            get
            {
                return srtpEncoder;
            }
        }

        public IDataPacketTransformer SrtcpDecoder
        {
            get
            {
                return srtcpDecoder;
            }
        }

        public IDataPacketTransformer SrtcpEncoder
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
            if (connection.IsClient)
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
            get { return connection.IsClient; }
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
                SecureRandom secureRandom = new SecureRandom();
                DtlsClientProtocol clientProtocol = new DtlsClientProtocol(secureRandom);
                try
                {
                    var client = (DtlsSrtpClient)connection;
                    // Perform the handshake in a non-blocking fashion
                    Transport = clientProtocol.Connect(client, this);

                    // Prepare the shared key to be used in RTP streaming
                    //client.PrepareSrtpSharedSecret();
                    // Generate encoders for DTLS traffic
                    if (client.SrtpPolicy != null)
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
                        if (excp is Org.BouncyCastle.Crypto.Tls.TlsFatalAlert)
                        {
                            handshakeError = (excp as Org.BouncyCastle.Crypto.Tls.TlsFatalAlert).Message;
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
                SecureRandom secureRandom = new SecureRandom();
                DtlsServerProtocol serverProtocol = new DtlsServerProtocol(secureRandom);
                try
                {
                    var server = (DtlsSrtpSecureServer)connection;

                    // Perform the handshake in a non-blocking fashion
                    Transport = serverProtocol.Accept(server, this);
                    // Prepare the shared key to be used in RTP streaming
                    //server.PrepareSrtpSharedSecret();
                    // Generate encoders for DTLS traffic
                    if (server.SrtpPolicy != null)
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
                        if (excp is Org.BouncyCastle.Crypto.Tls.TlsFatalAlert)
                        {
                            handshakeError = (excp as Org.BouncyCastle.Crypto.Tls.TlsFatalAlert).Message;
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

        public Certificate RemoteCertificate
        {
            get
            {
                return connection.RemoteCertificate;
            }
        }

        protected byte[] GetMasterServerKey()
        {
            return connection.SrtpMasterServerKey;
        }

        protected byte[] GetMasterServerSalt()
        {
            return connection.SrtpMasterServerSalt;
        }

        protected byte[] GetMasterClientKey()
        {
            return connection.SrtpMasterClientKey;
        }

        protected byte[] GetMasterClientSalt()
        {
            return connection.SrtpMasterClientSalt;
        }

        protected SrtpPolicy GetSrtpPolicy()
        {
            return connection.SrtpPolicy;
        }

        protected SrtpPolicy GetSrtcpPolicy()
        {
            return connection.SrtcpPolicy;
        }

        protected IDataPacketTransformer GenerateRtpEncoder()
        {
            return GenerateTransformer(connection.IsClient, true);
        }

        protected IDataPacketTransformer GenerateRtpDecoder()
        {
            //Generate the reverse result of "GenerateRtpEncoder"
            return GenerateTransformer(!connection.IsClient, true);
        }

        protected IDataPacketTransformer GenerateRtcpEncoder()
        {
            var isClient = connection is DtlsSrtpClient;
            return GenerateTransformer(connection.IsClient, false);
        }

        protected IDataPacketTransformer GenerateRtcpDecoder()
        {
            //Generate the reverse result of "GenerateRctpEncoder"
            return GenerateTransformer(!connection.IsClient, false);
        }

        protected IDataPacketTransformer GenerateTransformer(bool isClient, bool isRtp)
        {
            SecureRtpTransformEngine engine = null;
            if (!isClient)
            {
                engine = new SecureRtpTransformEngine(GetMasterServerKey(), GetMasterServerSalt(), GetSrtpPolicy(), GetSrtcpPolicy());
            }
            else
            {
                engine = new SecureRtpTransformEngine(GetMasterClientKey(), GetMasterClientSalt(), GetSrtpPolicy(), GetSrtcpPolicy());
            }

            if (isRtp)
            {
                return engine.CreateRtpPacketTransformer();
            }
            else
            {
                return engine.CreateRtcpPacketTransformer();
            }
        }

        public byte[] UnprotectRTP(byte[] packet, int offset, int length)
        {
            lock (this.srtpDecoder)
            {
                return this.srtpDecoder.DecodePacket(packet, offset, length);
            }
        }

        public int UnprotectRTP(byte[] payload, int length, out int outLength)
        {
            var result = UnprotectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTP(byte[] packet, int offset, int length)
        {
            lock (this.srtpEncoder)
            {
                return this.srtpEncoder.EncodePacket(packet, offset, length);
            }
        }

        public int ProtectRTP(byte[] payload, int length, out int outLength)
        {
            var result = ProtectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] UnprotectRTCP(byte[] packet, int offset, int length)
        {
            lock (this.srtcpDecoder)
            {
                return this.srtcpDecoder.DecodePacket(packet, offset, length);
            }
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outLength)
        {
            var result = UnprotectRTCP(payload, 0, length);
            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTCP(byte[] packet, int offset, int length)
        {
            lock (this.srtcpEncoder)
            {
                return this.srtcpEncoder.EncodePacket(packet, offset, length);
            }
        }

        public int ProtectRTCP(byte[] payload, int length, out int outLength)
        {
            var result = ProtectRTCP(payload, 0, length);
            if (result == null)
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

        public void WriteToRecvStream(byte[] buf)
        {
            if (!_isClosed)
            {
                _chunks.Add(buf);
            }
        }

        private int Read(byte[] buffer, int offset, int count, int timeout)
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
                    Buffer.BlockCopy(item, 0, buffer, 0, item.Length);
                    return item.Length;
                }
            }
            catch (ObjectDisposedException) { }
            catch (ArgumentNullException) { }

            return DTLS_RETRANSMISSION_CODE;
        }

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
                    logger.LogWarning($"DTLS transport timed out after {TimeoutMilliseconds}ms waiting for handshake from remote {(connection.IsClient ? "server" : "client")}.");
                    throw new TimeoutException();
                }
                else if (!_isClosed)
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
            else if (!_isClosed)
            {
                return Read(buf, off, len, waitMillis);
            }
            else
            {
                //throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NotConnected);
                return DTLS_RECEIVE_ERROR_CODE;
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
            if (!_isClosed)
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
