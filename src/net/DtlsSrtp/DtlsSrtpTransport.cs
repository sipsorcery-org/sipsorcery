/**
 * 
 * This class represents the DTLS SRTP transport connection to use as Client or Server.
 * 
 * 
 * @author Rafael Soares (raf.csoares@kyubinteractive.com)
 * 
 *
 */

using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Org.BouncyCastle.Crypto.DtlsSrtp
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

        //Socket to receive
        //Socket connectedSocket = null;
        //Socket to send
        //Socket remoteSocket = null;
        //IPEndPoint remoteEndPoint = null;
        //PipedStream _pipedStreamRecv;
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

        #region Internal Handshake Functions

        public bool DoHandshakeAsClient()
        {
            //if (serverEndPoint == null && clientSocket.Connected)
            //    serverEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;

            //this.remoteEndPoint = serverEndPoint;
            //this.connectedSocket = clientSocket;

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

        #endregion

        #region RTP Public Functions

        public byte[] UnprotectRTP(byte[] packet, int offset, int length)
        {
            try
            {
                return this.srtpDecoder.ReverseTransform(packet, offset, length);
            }
            catch (Exception)
            {
                //UnityEngine.Debug.Log("[DecodeRTP] Invalid packet");
            }
            return null;
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
            try
            {
                return this.srtpEncoder.Transform(packet, offset, length);
            }
            catch (Exception)
            {
                //UnityEngine.Debug.Log("[EncodeRTP] Invalid packet");
            }
            return null;
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

        #endregion

        #region RTCP Public Functions

        public byte[] UnprotectRTCP(byte[] packet, int offset, int length)
        {
            try
            {
                return this.srtcpDecoder.ReverseTransform(packet, offset, length);
            }
            catch (Exception)
            {
                //UnityEngine.Debug.Log("[DecodeRTCP] Invalid packet");
            }
            return null;
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
            try
            {
                return this.srtcpEncoder.Transform(packet, offset, length);
            }
            catch (Exception)
            {
                //UnityEngine.Debug.Log("[EncodeRTCP] Invalid packet");
            }
            return null;
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

        #endregion

        #region Datagram Transport Implementations

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
            //_pipedStreamRecv.Write(buf, 0, buf.Length);
            lock (_inStream)
            {
                _inStream.Write(buf, 0, buf.Length);
            }
        }

        public int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            if (_inStream.Position > 0)
            {
                lock (_inStream)
                {
                    Console.WriteLine($"DtlsSrtpTransport {_inStream.Position} bytes available.");
                    var msBuf = _inStream.ToArray();
                    Buffer.BlockCopy(msBuf, 0, buf, off, msBuf.Length);
                    _inStream.Position = 0;

                    Console.WriteLine($"DtlsSrtpTransport {msBuf.Length} bytes supplied to DTLS context.");
                    return msBuf.Length;
                }
            }
            else
            {
                Task.Delay(waitMillis).Wait();

                if (_inStream.Position > 0)
                {
                    lock (_inStream)
                    {
                        Console.WriteLine($"DtlsSrtpTransport {_inStream.Position} bytes available.");
                        var msBuf = _inStream.ToArray();
                        Buffer.BlockCopy(msBuf, 0, buf, off, msBuf.Length);
                        _inStream.Position = 0;

                        Console.WriteLine($"DtlsSrtpTransport {msBuf.Length} bytes supplied to DTLS context.");
                        return msBuf.Length;
                    }
                }
            }

            return 0;

            //System.Random random = new Random();
            ////Handshake reliable contains too long default backoff times
            //waitMillis = System.Math.Max(100, waitMillis / (random.Next(100, 1000)));

            //var readStartTime = System.DateTime.Now;
            //var curren tWaitTime = waitMillis;
            //var totalReceivedLen = 0;

            //while (currentWaitTime > 0)
            //{
            //    // MEDIA-48: DTLS handshake thread does not terminate
            //    // https://telestax.atlassian.net/browse/MEDIA-48
            //    if (this.HasTimeout())
            //    {
            //        Close();
            //        throw new System.Exception("Handshake is taking too long! (>" + MAX_DELAY + "ms");
            //    }
            //    if (!connectedSocket.IsBound)
            //    {
            //        Close();
            //        throw new System.Exception("Socked not bound to any IP Address");
            //    }

            //    var cachedTimeout = connectedSocket.ReceiveTimeout;
            //    try
            //    {
            //        connectedSocket.ReceiveTimeout = currentWaitTime;
            //        // Creates an IPEndPoint to capture the identity of the sending host.
            //        // In UDP we dont have Connect/Accept rule, so we must listen to packets to know the sender
            //        if (remoteEndPoint == null || remoteEndPoint.AddressFamily != connectedSocket.AddressFamily)
            //            remoteEndPoint = new IPEndPoint(connectedSocket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
            //        EndPoint senderRemote = /*remoteEndPoint == null? 
            //            new IPEndPoint(connectedSocket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0) :*/ 
            //            (EndPoint)remoteEndPoint;

            //        if (!connectedSocket.IsBound)
            //            connectedSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));

            //        var receivedLength = 0;
            //        if (off == 0)
            //        {
            //            receivedLength = connectedSocket.ReceiveFrom(buf, len, SocketFlags.None, ref senderRemote);
            //        }
            //        else
            //        {
            //            byte[] rv = new byte[len];
            //            receivedLength = connectedSocket.ReceiveFrom(rv, len, SocketFlags.None, ref senderRemote);

            //            if(receivedLength > 0)
            //                Buffer.BlockCopy(rv, 0, buf, off, receivedLength);
            //        }

            //        //Set IPEndPoint from received message
            //        remoteEndPoint = senderRemote as IPEndPoint;

            //        //Update offset and received length
            //        off += receivedLength;
            //        totalReceivedLen += receivedLength;
            //        //Reduce the receive limit to prevent errors in next run
            //        len -= receivedLength;
            //    }
            //    catch (System.Net.Sockets.SocketException) { }
            //    finally
            //    {
            //        if (connectedSocket != null)
            //        {
            //            connectedSocket.ReceiveTimeout = cachedTimeout;
            //        }
            //    }
            //    System.Threading.Thread.Sleep(1);
            //    currentWaitTime = waitMillis - (int)(System.DateTime.Now - readStartTime).TotalMilliseconds;
            //}

            //return totalReceivedLen > 0 ? totalReceivedLen : -1;

            //return 0;
        }

        public void Send(byte[] buf, int off, int len)
        {
            if (len > 0)
            {
                OnDataReady?.Invoke(buf.Skip(off).Take(len).ToArray());
            }
            //_pipedStreamSend.Write(buf, off, len);
            //if (!HasTimeout())
            //{
            //    TryCreateSenderFromEndpoint();
            //    if (remoteSocket != null && this.remoteEndPoint != null)
            //    {
            //        if (remoteEndPoint.AddressFamily == AddressFamily.InterNetwork && remoteSocket.AddressFamily != remoteEndPoint.AddressFamily)
            //            remoteEndPoint = new IPEndPoint(remoteEndPoint.Address.MapToIPv6(), remoteEndPoint.Port);

            //        remoteSocket.SendTo(buf, off, len, SocketFlags.None, remoteEndPoint);
            //        System.Threading.Thread.Sleep(1);
            //    }
            //    else
            //    {
            //        //UnityEngine.Debug.Log("Handler skipped send operation because channel is not open or connected.");
            //    }
            //}
            //else
            //{
            //    //UnityEngine.Debug.Log("Handler has timed out so send operation will be skipped.");
            //    //logger.warn("Handler has timed out so send operation will be skipped.");
            //}
        }

        /// <summary>
        /// Create a new socket to respond to client based in remoteEndPoint received from last message
        /// </summary>
        //protected virtual void TryCreateSenderFromEndpoint()
        //{
        //    if (this.remoteEndPoint != null)
        //    {
        //        if (remoteSocket == null || 
        //            (remoteSocket.AddressFamily != this.remoteEndPoint.AddressFamily &&
        //            (remoteSocket.AddressFamily == AddressFamily.InterNetwork || !remoteSocket.DualMode)))
        //        {
        //            if (this.remoteSocket != null && remoteSocket != connectedSocket)
        //            {
        //                this.remoteSocket.Close();
        //                this.remoteSocket = null;
        //            }

        //            if (connectedSocket != null && 
        //                ((connectedSocket.AddressFamily == AddressFamily.InterNetworkV6 && connectedSocket.DualMode) || 
        //                connectedSocket.AddressFamily == remoteEndPoint.Address.AddressFamily))
        //                this.remoteSocket = connectedSocket;
        //            else
        //            {
        //                //Create a new socket to send data using IPV6 Dual Mode
        //                this.remoteSocket = new Socket(AddressFamily.InterNetworkV6,
        //                                SocketType.Dgram,
        //                                ProtocolType.Udp);
        //                remoteSocket.DualMode = true;
        //            }
        //        }
        //    }
        //}

        public virtual void Close()
        {
            //if (this.remoteSocket != null && remoteSocket != connectedSocket)
            //    this.remoteSocket.Close();
            //this.remoteSocket = null;
            //this.connectedSocket = null;
            //this.startTime = System.DateTime.MinValue;
            //this.remoteEndPoint = null;
            //_pipedStreamRecv.Close();
        }

        #endregion
    }
}
