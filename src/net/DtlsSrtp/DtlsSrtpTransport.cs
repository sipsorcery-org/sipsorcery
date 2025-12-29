using System;
using System.Collections.Concurrent;
using System.Linq;
using Org.BouncyCastle.Tls;
using SharpSRTP.SRTP;

namespace SIPSorcery.Net
{
    public delegate void OnDataReadyEvent(byte[] data);

    public class DtlsSrtpTransport : DatagramTransport
    {
        private const int MTU = 1472;


        private IDtlsSrtpPeer connection;

        private ConcurrentQueue<byte[]> _data = new ConcurrentQueue<byte[]>();

        public DatagramTransport Transport { get; internal set; }
        public bool IsClient { get; internal set; }

        public SRTPContext ClientRtpContext { get; private set; }
        public SRTPContext ClientRtcpContext { get; private set; }
        public SRTPContext ServerRtpContext { get; private set; }
        public SRTPContext ServerRtcpContext { get; private set; }

        public event OnDataReadyEvent OnDataReady;

        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpTransport(IDtlsSrtpPeer connection)
        {
            this.connection = connection;
            connection.OnAlert += DtlsSrtpTransport_OnAlert;
        }

        private void DtlsSrtpTransport_OnAlert(AlertLevelsEnum alertLevel, AlertTypesEnum alertType, string alertDescription)
        {
            OnAlert?.Invoke(alertLevel, alertType, alertDescription);
        }

        public bool DoHandshake(out string handshakeError)
        {
            SRTPKeys keys;

            if (connection is DtlsSrtpServer server)
            {
                try
                {
                    DtlsServerProtocol serverProtocol = new DtlsServerProtocol();
                    Transport = serverProtocol.Accept(server, this);
                    keys = server.Keys;
                }
                catch (Exception ex)
                {
                    handshakeError = ex.Message;
                    return false;
                }
            }
            else if (connection is DtlsSrtpClient client)
            {
                try
                {
                    DtlsClientProtocol clientProtocol = new DtlsClientProtocol();
                    Transport = clientProtocol.Connect(client, this);
                    keys = client.Keys;
                }
                catch (Exception ex)
                {
                    handshakeError = ex.Message;
                    return false;
                }
            }
            else
            {
                handshakeError = "Unsupported connection type";
                return false;
            }

            handshakeError = null;

            // derive SRTP keys
            this.ClientRtpContext = new SRTPContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SRTPContextType.RTP);
            this.ClientRtcpContext = new SRTPContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SRTPContextType.RTCP);
            this.ServerRtpContext = new SRTPContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SRTPContextType.RTP);
            this.ServerRtcpContext = new SRTPContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SRTPContextType.RTCP);

            return true;
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return ServerRtpContext.ProtectRTP(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return ClientRtpContext.UnprotectRTP(payload, length, out outputBufferLength);
        }        

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return ServerRtcpContext.ProtectRTCP(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return ClientRtcpContext.UnprotectRTCP(payload, length, out outputBufferLength);
        }

        public Certificate GetRemoteCertificate()
        {
            return connection.PeerCertificate;
        }
        
        public int GetReceiveLimit() => MTU;

        public int GetSendLimit() => MTU;

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
