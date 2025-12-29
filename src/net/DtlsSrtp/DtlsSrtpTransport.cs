using System;
using System.Collections.Concurrent;
using System.Linq;
using Org.BouncyCastle.Tls;
using SharpSRTP.DTLS;
using SharpSRTP.SRTP;
using SharpSRTP.UDP;

namespace SIPSorcery.Net
{
    public delegate void OnDataReadyEvent(byte[] data);
    public delegate void OnDtlsAlertEvent(TlsAlertLevelsEnum alertLevel, TlsAlertTypesEnum alertType, string alertDescription);

    public class DtlsSrtpTransport : DatagramTransport
    {
        private IDtlsSrtpPeer _connection;

        private ConcurrentQueue<byte[]> _data = new ConcurrentQueue<byte[]>();

        public DatagramTransport Transport { get; internal set; }
        public bool IsClient { get { return _connection is DtlsSrtpClient; } }

        public SrtpContext DecodeRtpContext { get; private set; }
        public SrtpContext DecodeRtcpContext { get; private set; }
        public SrtpContext EncodeRtpContext { get; private set; }
        public SrtpContext EncodeRtcpContext { get; private set; }

        public event OnDataReadyEvent OnDataReady;

        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpTransport(IDtlsSrtpPeer connection)
        {
            this._connection = connection;
            connection.OnAlert += DtlsSrtpTransport_OnAlert;
        }

        private void DtlsSrtpTransport_OnAlert(object sender, DtlsAlertEventArgs args)
        {
            OnAlert?.Invoke(args.Level, args.AlertType, args.Description);
        }

        public bool DoHandshake(out string handshakeError)
        {
            SrtpKeys keys;
            DtlsTransport transport = null;

            if (_connection is DtlsSrtpServer server)
            {
                try
                {
                    DtlsServerProtocol serverProtocol = new DtlsServerProtocol();
                    transport = serverProtocol.Accept(server, this);
                    keys = server.Keys;
                }
                catch (Exception ex)
                {
                    handshakeError = ex.Message;
                    return false;
                }

                this.EncodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTP);
                this.EncodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTCP);
                this.DecodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTP);
                this.DecodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTCP);
            }
            else if (_connection is DtlsSrtpClient client)
            {
                try
                {
                    DtlsClientProtocol clientProtocol = new DtlsClientProtocol();
                    transport = clientProtocol.Connect(client, this);
                    keys = client.Keys;
                }
                catch (Exception ex)
                {
                    handshakeError = ex.Message;
                    return false;
                }

                this.EncodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTP);
                this.EncodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTCP);
                this.DecodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTP);
                this.DecodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTCP);
            }
            else
            {
                handshakeError = "Unsupported connection type";
                return false;
            }

            handshakeError = null;

            Transport = transport;

            return true;
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return EncodeRtpContext.ProtectRTP(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return DecodeRtpContext.UnprotectRTP(payload, length, out outputBufferLength);
        }        

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return EncodeRtcpContext.ProtectRTCP(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return DecodeRtcpContext.UnprotectRTCP(payload, length, out outputBufferLength);
        }

        public Certificate GetRemoteCertificate()
        {
            return _connection.PeerCertificate;
        }
        
        public int GetReceiveLimit() => UdpDatagramTransport.MTU;

        public int GetSendLimit() => UdpDatagramTransport.MTU;

        public void WriteToRecvStream(byte[] buffer)
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
            long t = 0;
            while(true)
            {
                if (_data.TryDequeue(out var data))
                {
                    Buffer.BlockCopy(data, 0, buf, 0, data.Length);
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
            OnDataReady?.Invoke(buf.ToArray());
        }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        public int Receive(Span<byte> buffer, int waitMillis)
        {
            return Receive(buffer.ToArray(), 0, buffer.Length, waitMillis);
        }

        public void Send(ReadOnlySpan<byte> buffer)
        {
            Send(buffer.ToArray(), 0, buffer.Length);
        }
#endif
    }
}
