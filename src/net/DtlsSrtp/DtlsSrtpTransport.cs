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
using Org.BouncyCastle.Tls;
using SIPSorcery.Net.SharpSRTP.DTLS;
using SIPSorcery.Net.SharpSRTP.DTLSSRTP;
using SIPSorcery.Net.SharpSRTP.SRTP;

namespace SIPSorcery.Net
{
    public delegate void OnDataReadyEvent(byte[] data);
    public delegate void OnDtlsAlertEvent(TlsAlertLevelsEnum alertLevel, TlsAlertTypesEnum alertType, string alertDescription);

    public class DtlsSrtpTransport : DatagramTransport
    {
        public const int MAXIMUM_MTU = 1472; // 1500 - 20 (IP) - 8 (UDP)
        public const int DTLS_RETRANSMISSION_CODE = -1;

        private IDtlsSrtpPeer _connection;

        private ConcurrentQueue<byte[]> _data = new ConcurrentQueue<byte[]>();
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
            this._connection.OnSessionStarted += DtlsSrtpTransport_OnSessionStarted;
            this._connection.OnAlert += DtlsSrtpTransport_OnAlert;
        }

        private void DtlsSrtpTransport_OnSessionStarted(object sender, DtlsSessionStartedEventArgs e)
        {
            this._peerCertificate = e.PeerCertificate;
            this.Context = e.Context;
        }

        private void DtlsSrtpTransport_OnAlert(object sender, DtlsAlertEventArgs args)
        {
            OnAlert?.Invoke(args.Level, args.AlertType, args.Description);
        }

        public bool DoHandshake(out string handshakeError)
        {
            DtlsTransport transport = _connection.DoHandshake(out handshakeError, this, null, (remoteEndpoint) => this);
            Transport = transport;
            return string.IsNullOrEmpty(handshakeError);
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.ProtectRtp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.UnprotectRtp(payload, length, out outputBufferLength);
        }        

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.ProtectRtcp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.UnprotectRtcp(payload, length, out outputBufferLength);
        }

        public Certificate GetRemoteCertificate()
        {
            return _peerCertificate;
        }

        public int GetReceiveLimit() => MAXIMUM_MTU;

        public int GetSendLimit() => MAXIMUM_MTU;

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
            OnDataReady?.Invoke(buf.AsSpan(off, len).ToArray());
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
