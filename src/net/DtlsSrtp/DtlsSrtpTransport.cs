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
        private string _remoteEndPoint;
        private Certificate _peerCertificate;

        public DatagramTransport Transport { get; internal set; }
        public bool IsClient { get { return _connection is DtlsSrtpClient; } }
        public SrtpKeys Keys { get; private set; }

        public SrtpSessionContext Context { get; private set; }

        public int TimeoutMilliseconds { get { return _connection.TimeoutMilliseconds; } set { _connection.TimeoutMilliseconds = value; } }

        public event OnDataReadyEvent OnDataReady;

        public event OnDtlsAlertEvent OnAlert;

        public DtlsSrtpTransport(IDtlsSrtpPeer connection)
        {
            this._connection = connection;
            this._connection.OnHandshakeCompleted += DtlsSrtpTransport_OnHandshakeCompleted;
            this._connection.OnAlert += DtlsSrtpTransport_OnAlert;
        }

        private void DtlsSrtpTransport_OnHandshakeCompleted(object sender, DtlsHandshakeCompletedEventArgs e)
        {
            this._peerCertificate = e.SecurityParameters.PeerCertificate;

            // derive SRTP/SRTCP master keys and contexts
            this.Context = _connection.CreateSessionContext(e.SecurityParameters);
        }

        private void DtlsSrtpTransport_OnAlert(object sender, DtlsAlertEventArgs args)
        {
            OnAlert?.Invoke(args.Level, args.AlertType, args.Description);
        }

        public bool DoHandshake(out string handshakeError)
        {
            DtlsTransport transport = _connection.DoHandshake(out handshakeError, this, () => _remoteEndPoint);
            Transport = transport;
            return string.IsNullOrEmpty(handshakeError);
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.EncodeRtpContext.ProtectRtp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.DecodeRtpContext.UnprotectRtp(payload, length, out outputBufferLength);
        }        

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.EncodeRtcpContext.ProtectRtcp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.DecodeRtcpContext.UnprotectRtcp(payload, length, out outputBufferLength);
        }

        public Certificate GetRemoteCertificate()
        {
            return _peerCertificate;
        }
        
        public int GetReceiveLimit() => UdpDatagramTransport.MTU;

        public int GetSendLimit() => UdpDatagramTransport.MTU;

        public void WriteToRecvStream(byte[] buffer, string remoteEndPoint) // remoteEndPoint = "127.0.0.1:80"
        {
            _remoteEndPoint = remoteEndPoint;
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
