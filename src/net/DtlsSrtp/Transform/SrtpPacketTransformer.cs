//-----------------------------------------------------------------------------
// Filename: SrtpPacketTransformer.cs
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Transformer class for SRTP packet encoding and decoding.
    /// </summary>
    public class SrtpPacketTransformer : IDataPacketTransformer
    {
        private readonly object _lock = new object();
        private readonly RTPPacket _rawPacket;

        private readonly SecureRtpTransformEngine _forwardEngine;
        private readonly SecureRtpTransformEngine _reverseEngine;

        private readonly ConcurrentDictionary<long, SrtpCryptoContext> _cryptoContexts;

        /// <summary>
        /// Initializes a new instance of the <see cref="SrtpPacketTransformer"/> class.
        /// Uses the same engine for both forward and reverse transformations.
        /// </summary>
        /// <param name="engine">The SRTP transform engine.</param>
        public SrtpPacketTransformer(SecureRtpTransformEngine engine) : this(engine, engine)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SrtpPacketTransformer"/> class.
        /// </summary>
        /// <param name="forwardEngine">The engine for forward transformations.</param>
        /// <param name="reverseEngine">The engine for reverse transformations.</param>
        public SrtpPacketTransformer(SecureRtpTransformEngine forwardEngine, SecureRtpTransformEngine reverseEngine)
        {
            _forwardEngine = forwardEngine;
            _reverseEngine = reverseEngine;
            _cryptoContexts = new ConcurrentDictionary<long, SrtpCryptoContext>();
            _rawPacket = new RTPPacket();
        }

        /// <summary>
        /// Encodes a packet using SRTP.
        /// </summary>
        /// <param name="packet">The packet to encode.</param>
        /// <returns>The encoded packet.</returns>
        public byte[] EncodePacket(byte[] packet)
        {
            return EncodePacket(packet, 0, packet.Length);
        }

        /// <summary>
        /// Encodes a packet using SRTP with specified offset and length.
        /// </summary>
        /// <param name="packet">The packet to encode.</param>
        /// <param name="offset">The offset to start encoding from.</param>
        /// <param name="length">The length of the packet to encode.</param>
        /// <returns>The encoded packet.</returns>
        public byte[] EncodePacket(byte[] packet, int offset, int length)
        {
            lock (_lock)
            {
                // Updates the contents of the raw packet with the new incoming packet.
                _rawPacket.Wrap(packet, offset, length);

                // Associate the packet to a crypto context.
                long ssrc = _rawPacket.GetSSRC();
                if (!_cryptoContexts.TryGetValue(ssrc, out var context))
                {
                    context = _forwardEngine.GetDefaultSrtpContext().DeriveContext(ssrc, 0, 0);
                    context.DeriveSrtpKeys(0);
                    _cryptoContexts[ssrc] = context;
                }

                // Transform the RTP packet into SRTP.
                context.TransformPacket(_rawPacket);
                var data = _rawPacket.GetData();

                // Clear sensitive data from memory
                ClearSensitiveData(packet);

                return data;
            }
        }

        /// <summary>
        /// Decodes an SRTP packet back to RTP.
        /// </summary>
        /// <param name="packet">The packet to decode.</param>
        /// <returns>The decoded packet.</returns>
        public byte[] DecodePacket(byte[] packet)
        {
            return DecodePacket(packet, 0, packet.Length);
        }

        /// <summary>
        /// Decodes an SRTP packet back to RTP with specified offset and length.
        /// </summary>
        /// <param name="packet">The packet to decode.</param>
        /// <param name="offset">The offset to start decoding from.</param>
        /// <param name="length">The length of the packet to decode.</param>
        /// <returns>The decoded packet.</returns>
        public byte[] DecodePacket(byte[] packet, int offset, int length)
        {
            lock (_lock)
            {
                // Wrap data into the raw packet for readable format.
                _rawPacket.Wrap(packet, offset, length);

                // Associate the packet to a crypto context.
                long ssrc = _rawPacket.GetSSRC();
                if (!_cryptoContexts.TryGetValue(ssrc, out var context))
                {
                    context = _reverseEngine.GetDefaultSrtpContext().DeriveContext(ssrc, 0, 0);
                    context.DeriveSrtpKeys(_rawPacket.GetSequenceNumber());
                    _cryptoContexts[ssrc] = context;
                }

                // Reverse transform the SRTP packet back to RTP.
                var data = context.ReverseTransformPacket(_rawPacket) ? _rawPacket.GetData() : null;

                // Clear sensitive data from memory
                ClearSensitiveData(packet);

                return data;
            }
        }

        /// <summary>
        /// Closes the transformer and underlying transform engine.
        /// </summary>
        public void Shutdown()
        {
            _forwardEngine.CloseContexts();
            if (_forwardEngine != _reverseEngine)
            {
                _reverseEngine.CloseContexts();
            }

            foreach (var ssrc in _cryptoContexts.Keys)
            {
                if (_cryptoContexts.TryRemove(ssrc, out var context))
                {
                    context?.Close();
                }
            }
        }

        /// <summary>
        /// Clears sensitive data from the specified byte array.
        /// </summary>
        /// <param name="data">The byte array to clear.</param>
        private void ClearSensitiveData(byte[] data)
        {
            if (data != null)
            {
                System.Array.Clear(data, 0, data.Length);
            }
        }
    }
}
