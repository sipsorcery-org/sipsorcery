﻿//-----------------------------------------------------------------------------
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
using System.Linq;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class DtlsSrtpTransport : DatagramTransport, IDisposable
    {
        public const int DEFAULT_MTU = 1500;
        public const int MIN_IP_OVERHEAD = 20;
        public const int MAX_IP_OVERHEAD = MIN_IP_OVERHEAD + 64;
        public const int UDP_OVERHEAD = 8;
        public const int DEFAULT_TIMEOUT_MILLISECONDS = 20000;
        public const int DTLS_RECEIVE_ERROR_CODE = -1;

        private static readonly ILogger logger = Log.Logger;

        private IPacketTransformer srtpEncoder;
        private IPacketTransformer srtpDecoder;
        private IPacketTransformer srtcpEncoder;
        private IPacketTransformer srtcpDecoder;
        IDtlsSrtpPeer connection = null;

        private PipedMemoryStream _inStream = new PipedMemoryStream();

        public DtlsTransport Transport { get; private set; }

        /// <summary>
        /// Sets the period in milliseconds that the handshake attempt will timeout
        /// after.
        /// </summary>
        public int TimeoutMilliseconds = DEFAULT_TIMEOUT_MILLISECONDS;

        public Action<byte[]> OnDataReady;

        /// <summary>
        /// Parameters:
        ///  - alert level,
        ///  - alert type,
        ///  - alert description.
        /// </summary>
        public event Action<AlertLevelsEnum, AlertTypesEnum, string> OnAlert;

        private System.DateTime startTime = System.DateTime.MinValue;
        private bool _isClosed = false;

        // Network properties
        private int mtu;
        private int receiveLimit;
        private int sendLimit;

        private volatile bool handshakeComplete;
        private volatile bool handshakeFailed;
        private volatile bool handshaking;

        public DtlsSrtpTransport(IDtlsSrtpPeer connection, int mtu = DEFAULT_MTU)
        {
            // Network properties
            this.mtu = mtu;
            this.receiveLimit = System.Math.Max(0, mtu - MIN_IP_OVERHEAD - UDP_OVERHEAD);
            this.sendLimit = System.Math.Max(0, mtu - MAX_IP_OVERHEAD - UDP_OVERHEAD);

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
            return handshakeComplete;
        }

        public bool IsHandshakeFailed()
        {
            return handshakeFailed;
        }

        public bool IsHandshaking()
        {
            return handshaking;
        }

        public bool DoHandshake()
        {
            if (connection.IsClient())
            {
                return DoHandshakeAsClient();
            }
            else
            {
                return DoHandshakeAsServer();
            }
        }

        public bool IsClient
        {
            get { return connection.IsClient(); }
        }

        public bool DoHandshakeAsClient()
        {
            logger.LogDebug("DTLS commencing handshake as client.");

            if (!handshaking && !handshakeComplete)
            {
                this.startTime = System.DateTime.Now;
                this.handshaking = true;
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
                    if (client.GetSrtpPolicy() != null)
                    {
                        srtpDecoder = GenerateRtpDecoder();
                        srtpEncoder = GenerateRtpEncoder();
                        srtcpDecoder = GenerateRtcpDecoder();
                        srtcpEncoder = GenerateRtcpEncoder();
                    }
                    // Declare handshake as complete
                    handshakeComplete = true;
                    handshakeFailed = false;
                    handshaking = false;
                    // Warn listeners handshake completed
                    //UnityEngine.Debug.Log("DTLS Handshake Completed");

                    return true;
                }
                catch (System.Exception excp)
                {
                    logger.LogWarning($"DTLS handshake as client failed. {excp.Message}");

                    // Declare handshake as failed
                    handshakeComplete = false;
                    handshakeFailed = true;
                    handshaking = false;
                    // Warn listeners handshake completed
                    //UnityEngine.Debug.Log("DTLS Handshake failed\n" + e);
                }
            }
            return false;
        }

        public bool DoHandshakeAsServer()
        {
            logger.LogDebug("DTLS commencing handshake as server.");

            if (!handshaking && !handshakeComplete)
            {
                this.startTime = System.DateTime.Now;
                this.handshaking = true;
                SecureRandom secureRandom = new SecureRandom();
                DtlsServerProtocol serverProtocol = new DtlsServerProtocol(secureRandom);
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
                    handshakeComplete = true;
                    handshakeFailed = false;
                    handshaking = false;
                    // Warn listeners handshake completed
                    //UnityEngine.Debug.Log("DTLS Handshake Completed");
                    return true;
                }
                catch (System.Exception excp)
                {
                    logger.LogWarning($"DTLS handshake as server failed. {excp.Message}");

                    // Declare handshake as failed
                    handshakeComplete = false;
                    handshakeFailed = true;
                    handshaking = false;
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

        public byte[] UnprotectRTP(byte[] packet, int offset, int length)
        {
            return this.srtpDecoder.ReverseTransform(packet, offset, length);
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
            return this.srtpEncoder.Transform(packet, offset, length);
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
            return this.srtcpDecoder.ReverseTransform(packet, offset, length);
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
            return this.srtcpEncoder.Transform(packet, offset, length);
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
            return TimeoutMilliseconds - (int)(System.DateTime.Now - this.startTime).TotalMilliseconds;
        }

        public int GetReceiveLimit()
        {
            return this.receiveLimit;
        }

        public int GetSendLimit()
        {
            return this.sendLimit;
        }

        public void WriteToRecvStream(byte[] buf)
        {
            _inStream.Write(buf, 0, buf.Length);
        }

        public int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            if (!handshakeComplete)
            {
                // The timeout for the handshake applies from when it started rather than
                // for each individual receive..
                int millisecondsRemaining = GetMillisecondsRemaining();

                if (millisecondsRemaining <= 0)
                {
                    logger.LogWarning($"DTLS transport timed out after {TimeoutMilliseconds}ms waiting for handshake from remote {(connection.IsClient() ? "server" : "client")}.");
                    throw new TimeoutException();
                }
                else if (!_isClosed)
                {
                    waitMillis = (int)System.Math.Min(waitMillis, millisecondsRemaining);
                    return _inStream.Read(buf, off, len, waitMillis);
                }
                else
                {
                    return DTLS_RECEIVE_ERROR_CODE;
                }
            }
            else if (!_isClosed)
            {
                return _inStream.Read(buf, off, len, waitMillis);
            }
            else
            {
                return DTLS_RECEIVE_ERROR_CODE;
            }
        }

        public void Send(byte[] buf, int off, int len)
        {
            OnDataReady?.Invoke(buf.Skip(off).Take(len).ToArray());
        }

        public virtual void Close()
        {
            _isClosed = true;
            this.startTime = System.DateTime.MinValue;
            _inStream.Close();
        }

        /// <summary>
        /// Close the transport if the instance is out of scope.
        /// </summary>
        protected void Dispose(bool disposing)
        {
            Close();
        }

        /// <summary>
        /// Close the transport if the instance is out of scope.
        /// </summary>
        public void Dispose()
        {
            Close();
        }
    }
}
