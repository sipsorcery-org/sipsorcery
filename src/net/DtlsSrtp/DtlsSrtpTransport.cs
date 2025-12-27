using System;
using System.Collections.Concurrent;
using System.Linq;
using Org.BouncyCastle.Tls;
using SharpSRTP.SRTP;

namespace SIPSorcery.net.DtlsSrtp
{
    public class DtlsSrtpTransport : DatagramTransport
    {
        private const int MTU = 1472;
        private const uint E_FLAG = 0x80000000;

        public DatagramTransport Transport { get; internal set; }
        public bool IsClient { get; internal set; }
        private IDtlsSrtpPeer connection;

        private ConcurrentQueue<byte[]> _data = new ConcurrentQueue<byte[]>();

        public SRTPContext ClientRtpContext { get; private set; }
        public SRTPContext ClientRtcpContext { get; private set; }
        public SRTPContext ServerRtpContext { get; private set; }
        public SRTPContext ServerRtcpContext { get; private set; }

        public delegate void OnDataReadyEvent(byte[] data);
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

        public bool DoHandshake(out object handshakeError)
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
                    handshakeError = ex;
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
                    handshakeError = ex;
                    return false;
                }
            }
            else
            {
                throw new NotSupportedException();
            }

            handshakeError = null;

            // derive SRTP keys
            this.ClientRtpContext = new SRTPContext(keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SRTPContextType.RTP);
            this.ClientRtcpContext = new SRTPContext(keys.ProtectionProfile, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SRTPContextType.RTCP);
            this.ServerRtpContext = new SRTPContext(keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SRTPContextType.RTP);
            this.ServerRtcpContext = new SRTPContext(keys.ProtectionProfile, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SRTPContextType.RTCP);

            return true;
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ServerRtpContext;

            uint ssrc = RTPReader.ReadSsrc(payload);
            ushort sequenceNumber = RTPReader.ReadSequenceNumber(payload);
            int offset = RTPReader.ReadHeaderLen(payload);

            uint roc = context.Roc;
            ulong index = SRTPProtocol.GenerateRTPIndex(roc, sequenceNumber);

            byte[] iv = SRTPProtocol.GenerateMessageIV(context.K_s, ssrc, index);
            AESCTR.Encrypt(context.AES, payload, offset, length, iv);

            payload[length + 0] = (byte)(roc >> 24);
            payload[length + 1] = (byte)(roc >> 16);
            payload[length + 2] = (byte)(roc >> 8);
            payload[length + 3] = (byte)roc;

            byte[] auth = SRTPProtocol.GenerateAuthTag(context.HMAC, payload, 0, length + 4);
            System.Buffer.BlockCopy(auth, 0, payload, length, context.N_tag); // we don't append ROC in SRTP
            outputBufferLength = length + context.N_tag;

            if (sequenceNumber == 0xFFFF)
            {
                context.Roc++;
            }

            return 0;
        }        

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ClientRtpContext;
            outputBufferLength = length - context.N_tag;

            uint ssrc = RTPReader.ReadSsrc(payload);
            ushort sequenceNumber = RTPReader.ReadSequenceNumber(payload);

            // TODO: optimize memory allocation - we could preallocate 4 byte array and add another GenerateAuthTag overload that processes 2 blocks
            byte[] msgAuth = new byte[length + 4];
            Buffer.BlockCopy(payload, 0, msgAuth, 0, length);
            msgAuth[length + 0] = (byte)(context.Roc >> 24);
            msgAuth[length + 1] = (byte)(context.Roc >> 16);
            msgAuth[length + 2] = (byte)(context.Roc >> 8);
            msgAuth[length + 3] = (byte)(context.Roc);

            byte[] auth = SRTPProtocol.GenerateAuthTag(context.HMAC, msgAuth, 0, length - context.N_tag + 4);
            for (int i = 0; i < context.N_tag; i++)
            {
                if (payload[length - context.N_tag + i] != auth[i])
                {
                    return -1;
                }
            }

            msgAuth = null;

            if (!context.S_l_set)
            {
                SetSequenceNumber(sequenceNumber);
            }

            int offset = RTPReader.ReadHeaderLen(payload);

            uint roc = context.Roc;
            uint index = SRTPProtocol.DetermineRTPIndex(context.S_l, sequenceNumber, roc);

            if(!context.CheckandUpdateReplayWindow(index))
            {
                return -1;
            }

            byte[] iv = SRTPProtocol.GenerateMessageIV(context.K_s, ssrc, index);
            AESCTR.Encrypt(context.AES, payload, offset, length - context.N_tag, iv);

            return 0;
        }        

        /// <summary>
        /// S_l can be set by signaling. RFC 3711 3.3.1. 
        /// </summary>
        /// <param name="sequenceNumber"></param>
        public void SetSequenceNumber(ushort sequenceNumber)
        {
            var context = ClientRtpContext;

            if (context.S_l_set)
            {
                throw new InvalidOperationException("S_l already set!");
            }

            context.S_l = sequenceNumber;
            context.S_l_set = true;
        }

        /// <summary>
        /// ROC can be set by signaling. RFC 3711 3.3.1. 
        /// </summary>
        /// <param name="roc"></param>
        public void SetROC(ushort roc)
        {
            var context = ClientRtpContext;
            context.Roc = roc;
        }

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ServerRtcpContext;

            uint ssrc = RTCPReader.ReadSsrc(payload);
            int offset = RTCPReader.GetHeaderLen();

            byte[] iv = SRTPProtocol.GenerateMessageIV(context.K_s, ssrc, context.S_l);
            AESCTR.Encrypt(context.AES, payload, offset, length, iv);

            uint index = context.S_l | E_FLAG;
            payload[length + 0] = (byte)(index >> 24);
            payload[length + 1] = (byte)(index >> 16);
            payload[length + 2] = (byte)(index >> 8);
            payload[length + 3] = (byte)index;

            byte[] auth = SRTPProtocol.GenerateAuthTag(context.HMAC, payload, 0, length + 4);
            System.Buffer.BlockCopy(auth, 0, payload, length + 4, context.N_tag);
            outputBufferLength = length + 4 + context.N_tag;

            context.S_l = (context.S_l + 1) % 0x80000000;

            return 0;
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ClientRtcpContext;

            outputBufferLength = length - 4 - context.N_tag;

            uint ssrc = RTCPReader.ReadSsrc(payload);
            int offset = RTCPReader.GetHeaderLen();
            uint index = RTCPReader.SRTCPReadIndex(payload, context.N_tag);

            if ((index & E_FLAG) == E_FLAG)
            {
                index = index & ~E_FLAG;

                byte[] auth = SRTPProtocol.GenerateAuthTag(context.HMAC, payload, 0, length - context.N_tag);
                for (int i = 0; i < context.N_tag; i++)
                {
                    if (payload[length - context.N_tag + i] != auth[i])
                    {
                        return -1;
                    }
                }

                if (!context.CheckandUpdateReplayWindow(index))
                {
                    return -1;
                }

                byte[] iv = SRTPProtocol.GenerateMessageIV(context.K_s, ssrc, context.S_l);
                AESCTR.Encrypt(context.AES, payload, offset, length - 4 - context.N_tag, iv);

                return 0;
            }

            return -1;
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
