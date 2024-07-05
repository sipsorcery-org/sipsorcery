using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SIPSorcery.Net
{
    public class SrtcpPacketTransformer : IDataPacketTransformer, IDisposable
    {
        private int _isLocked = 0;
        private readonly RTPPacket _sharedPacket;
        private readonly SecureRtpTransformEngine _forwardEngine;
        private readonly SecureRtpTransformEngine _reverseEngine;
        private readonly ConcurrentDictionary<long, SrtcpCryptoContext> _contexts;

        public SrtcpPacketTransformer(SecureRtpTransformEngine engine) : this(engine, engine) { }

        public SrtcpPacketTransformer(SecureRtpTransformEngine forwardEngine, SecureRtpTransformEngine reverseEngine)
        {
            _sharedPacket = new RTPPacket();
            _forwardEngine = forwardEngine;
            _reverseEngine = reverseEngine;
            _contexts = new ConcurrentDictionary<long, SrtcpCryptoContext>();
        }

        public byte[] EncodePacket(byte[] pkt) => EncodePacket(pkt, 0, pkt.Length);

        public byte[] EncodePacket(byte[] pkt, int offset, int length)
        {
            ValidatePacketData(pkt, offset, length);

            if (Interlocked.CompareExchange(ref _isLocked, 1, 0) == 0)
            {
                try
                {
                    return EncodeOrDecodePacket(pkt, offset, length, _forwardEngine, true);
                }
                finally
                {
                    Interlocked.Exchange(ref _isLocked, 0);
                }
            }

            return EncodeOrDecodePacket(pkt, offset, length, _forwardEngine, true);
        }

        public byte[] DecodePacket(byte[] pkt) => DecodePacket(pkt, 0, pkt.Length);

        public byte[] DecodePacket(byte[] pkt, int offset, int length)
        {
            ValidatePacketData(pkt, offset, length);

            if (Interlocked.CompareExchange(ref _isLocked, 1, 0) == 0)
            {
                try
                {
                    return EncodeOrDecodePacket(pkt, offset, length, _reverseEngine, false);
                }
                finally
                {
                    Interlocked.Exchange(ref _isLocked, 0);
                }
            }

            return EncodeOrDecodePacket(pkt, offset, length, _reverseEngine, false);
        }

        public void Shutdown()
        {
            try
            {
                _forwardEngine.CloseContexts();
                if (_forwardEngine != _reverseEngine)
                {
                    _reverseEngine.CloseContexts();
                }

                foreach (var ssrc in _contexts.Keys)
                {
                    if (_contexts.TryRemove(ssrc, out var context))
                    {
                        context.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to shutdown transformer.", ex);
            }
        }

        public void Dispose()
        {
            Shutdown();
        }

        private byte[] EncodeOrDecodePacket(byte[] pkt, int offset, int length, SecureRtpTransformEngine engine, bool isEncode)
        {
            using (var packet = new RTPPacket())
            {
                packet.Wrap(pkt, offset, length);
                var context = GetOrCreateContext(packet.GetRTCPSSRC(), engine);

                if (isEncode)
                {
                    context.TransformPacket(packet);
                }
                else if (!context.ReverseTransformPacket(packet))
                {
                    return null;
                }

                return packet.GetData();
            }
        }

        private void ValidatePacketData(byte[] pkt, int offset, int length)
        {
            if (pkt == null)
            {
                throw new ArgumentNullException(nameof(pkt));
            }
            if (offset < 0 || offset >= pkt.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (length < 0 || offset + length > pkt.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }

        private SrtcpCryptoContext GetOrCreateContext(long ssrc, SecureRtpTransformEngine engine)
        {
            return _contexts.GetOrAdd(ssrc, _ => {
                var context = engine.GetDefaultSrtcpContext().DeriveContext(ssrc);
                context.DeriveSrtcpKeys();
                return context;
            });
        }
    }
}
