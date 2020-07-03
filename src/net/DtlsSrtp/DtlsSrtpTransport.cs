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
//                              memory stream.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;

namespace SIPSorcery.Net
{
    public class DtlsSrtpTransport : DatagramTransport
    {
        public const int DEFAULT_MTU = 1500;
        public const int MIN_IP_OVERHEAD = 20;
        public const int MAX_IP_OVERHEAD = MIN_IP_OVERHEAD + 64;
        public const int UDP_OVERHEAD = 8;
        public const int MAX_DELAY = 20000;

        private IPacketTransformer srtpEncoder;
        private IPacketTransformer srtpDecoder;
        private IPacketTransformer srtcpEncoder;
        private IPacketTransformer srtcpDecoder;
        IDtlsSrtpPeer connection = null;

        MemoryStream _inStream = new MemoryStream();

        public Action<byte[]> OnDataReady;

        private System.DateTime startTime = System.DateTime.MinValue;

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

        public bool DoHandshakeAsClient()
        {
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
                    clientProtocol.Connect(client, this);
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
                catch (System.Exception)
                {
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
                    serverProtocol.Accept(server, this);
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
                catch (System.Exception)
                {
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

        protected bool HasTimeout()
        {
            return this.startTime == System.DateTime.MinValue || (System.DateTime.Now - this.startTime).TotalMilliseconds > MAX_DELAY;
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
            lock (_inStream)
            {
                _inStream.Write(buf, 0, buf.Length);
            }
        }

        public int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            if(_inStream.Position <= 0)
            {
                Task.Delay(waitMillis).Wait();
            }
                
            if (_inStream.Position > 0)
            {
                lock (_inStream)
                {
                    var msBuf = _inStream.ToArray();
                    Buffer.BlockCopy(msBuf, 0, buf, off, msBuf.Length);
                    _inStream.Position = 0;
                    return msBuf.Length;
                }
            }

            return -1;
        }

        public void Send(byte[] buf, int off, int len)
        {
            if (len > 0)
            {
                OnDataReady?.Invoke(buf.Skip(off).Take(len).ToArray());
            }
        }

        public virtual void Close()
        {
            this.startTime = System.DateTime.MinValue;
        }
    }
}
