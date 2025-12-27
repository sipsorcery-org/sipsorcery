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

        public SrtpContext ClientRtpContext { get; private set; }
        public SrtpContext ClientRtcpContext { get; private set; }
        public SrtpContext ServerRtpContext { get; private set; }
        public SrtpContext ServerRtcpContext { get; private set; }

        public DtlsSrtpTransport(IDtlsSrtpPeer connection)
        {
            this.connection = connection;

            ((DtlsSrtpServer)connection).OnAlert += DtlsSrtpTransport_OnAlert;
        }

        private void DtlsSrtpTransport_OnAlert(AlertLevelsEnum alertLevel, AlertTypesEnum alertType, string alertDescription)
        {
            OnAlert?.Invoke(alertLevel, alertType, alertDescription);
        }

        public delegate void OnDataReadyEvent(byte[] data);
        public event OnDataReadyEvent OnDataReady;

        public delegate void OnDtlsAlertEvent(AlertLevelsEnum alertLevel, AlertTypesEnum alertType, string alertDescription);
        public event OnDtlsAlertEvent OnAlert;

        public bool DoHandshake(out object handshakeError)
        {
            handshakeError = null;

            DtlsServerProtocol serverProtocol = new DtlsServerProtocol();
            try
            {
                var server = (DtlsSrtpServer)connection;

                Transport = serverProtocol.Accept(server, this);

                // policy
                var keys = server.Keys;

                this.ClientRtpContext = new SrtpContext(keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, true);
                this.ClientRtcpContext = new SrtpContext(keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, false);

                this.ServerRtpContext = new SrtpContext(keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, true);
                this.ServerRtcpContext = new SrtpContext(keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, false);

                return true;
            }
            catch (Exception ex)
            {
                handshakeError = ex;
                return false;
            }
        }

        public bool IsHandshakeComplete()
        {
            return Transport != null;
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ServerRtpContext;

            uint ssrc = SrtpKeyGenerator.RtpReadSsrc(payload);
            ushort sequenceNumber = SrtpKeyGenerator.RtpReadSequenceNumber(payload);
            int offset = SrtpKeyGenerator.RtpReadHeaderLen(payload);

            uint roc = context.Roc;
            ulong index = SrtpKeyGenerator.GenerateRTPIndex(roc, sequenceNumber);

            byte[] iv = SrtpKeyGenerator.GenerateMessageIV(context.K_s, ssrc, index);
            SrtpKeyGenerator.EncryptAESCTR(context.AES, payload, offset, length, iv);

            payload[length + 0] = (byte)(roc >> 24);
            payload[length + 1] = (byte)(roc >> 16);
            payload[length + 2] = (byte)(roc >> 8);
            payload[length + 3] = (byte)roc;

            byte[] auth = SrtpKeyGenerator.GenerateAuthTag(context.HMAC, payload, 0, length + 4);
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

            uint ssrc = SrtpKeyGenerator.RtpReadSsrc(payload);
            ushort sequenceNumber = SrtpKeyGenerator.RtpReadSequenceNumber(payload);

            // TODO: optimize memory allocation - we could preallocate 4 byte array and add another GenerateAuthTag overload that processes 2 blocks
            byte[] msgAuth = new byte[length + 4];
            Buffer.BlockCopy(payload, 0, msgAuth, 0, length);
            msgAuth[length + 0] = (byte)(context.Roc >> 24);
            msgAuth[length + 1] = (byte)(context.Roc >> 16);
            msgAuth[length + 2] = (byte)(context.Roc >> 8);
            msgAuth[length + 3] = (byte)(context.Roc);

            byte[] auth = SrtpKeyGenerator.GenerateAuthTag(context.HMAC, msgAuth, 0, length - context.N_tag + 4);
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

            int offset = SrtpKeyGenerator.RtpReadHeaderLen(payload);

            uint roc = context.Roc;
            uint index = SrtpKeyGenerator.DetermineRTPIndex(context.S_l, sequenceNumber, roc);

            if(!context.CheckandUpdateReplayWindow(index))
            {
                return -1;
            }

            byte[] iv = SrtpKeyGenerator.GenerateMessageIV(context.K_s, ssrc, index);
            SrtpKeyGenerator.EncryptAESCTR(context.AES, payload, offset, length - context.N_tag, iv);

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

            uint ssrc = SrtpKeyGenerator.RtcpReadSsrc(payload);
            int offset = SrtpKeyGenerator.RtcpReadHeaderLen(payload);

            byte[] iv = SrtpKeyGenerator.GenerateMessageIV(context.K_s, ssrc, context.S_l);
            SrtpKeyGenerator.EncryptAESCTR(context.AES, payload, offset, length, iv);

            uint index = context.S_l | E_FLAG;
            payload[length + 0] = (byte)(index >> 24);
            payload[length + 1] = (byte)(index >> 16);
            payload[length + 2] = (byte)(index >> 8);
            payload[length + 3] = (byte)index;

            byte[] auth = SrtpKeyGenerator.GenerateAuthTag(context.HMAC, payload, 0, length + 4);
            System.Buffer.BlockCopy(auth, 0, payload, length + 4, context.N_tag);
            outputBufferLength = length + 4 + context.N_tag;

            context.S_l = (context.S_l + 1) % 0x80000000;

            return 0;
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            var context = ClientRtcpContext;

            outputBufferLength = length - 4 - context.N_tag;

            uint ssrc = SrtpKeyGenerator.RtcpReadSsrc(payload);
            int offset = SrtpKeyGenerator.RtcpReadHeaderLen(payload);
            uint index = SrtpKeyGenerator.SrtcpReadIndex(payload, context.N_tag);

            if ((index & E_FLAG) == E_FLAG)
            {
                index = index & ~E_FLAG;

                byte[] auth = SrtpKeyGenerator.GenerateAuthTag(context.HMAC, payload, 0, length - context.N_tag);
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

                byte[] iv = SrtpKeyGenerator.GenerateMessageIV(context.K_s, ssrc, context.S_l);
                SrtpKeyGenerator.EncryptAESCTR(context.AES, payload, offset, length - 4 - context.N_tag, iv);

                return 0;
            }

            return -1;
        }

        public Certificate GetRemoteCertificate()
        {
            var server = (DtlsSrtpServer)connection;
            return server.ClientCertificate;
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
        public int Receive(Span<byte> buffer, int waitMillis) => throw new System.NotImplementedException();
        public void Send(ReadOnlySpan<byte> buffer) => throw new System.NotImplementedException();
#endif
    }
}
