//-----------------------------------------------------------------------------
// Filename: SrtcpTransformer.cs
//
// Description: Encapsulates the encryption/decryption logic for SRTCP packets.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/SRTCPTransformer.java
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// Original Source: AGPL-3.0 License
//-----------------------------------------------------------------------------

using System.Collections.Generic;

namespace SIPSorcery.Net
{
    /// <summary>
    /// SRTCPTransformer implements PacketTransformer.
    /// It encapsulate the encryption / decryption logic for SRTCP packets
    ///
    /// @author Bing SU (nova.su @gmail.com)
    /// @author Werner Dittmann<Werner.Dittmann@t-online.de>
    /// </summary>
    public class SrtcpTransformer : IPacketTransformer
    {
        private RawPacket packet;

        private SrtpTransformEngine forwardEngine;
        private SrtpTransformEngine reverseEngine;

        /** All the known SSRC's corresponding SRTCPCryptoContexts */
        private Dictionary<long, SrtcpCryptoContext> contexts;

        public SrtcpTransformer(SrtpTransformEngine engine) : this(engine, engine)
        {

        }

        public SrtcpTransformer(SrtpTransformEngine forwardEngine, SrtpTransformEngine reverseEngine)
        {
            this.packet = new RawPacket();
            this.forwardEngine = forwardEngine;
            this.reverseEngine = reverseEngine;
            this.contexts = new Dictionary<long, SrtcpCryptoContext>();
        }

        /// <summary>
        /// Encrypts a SRTCP packet
        /// </summary>
        /// <param name="pkt">plain SRTCP packet to be encrypted.</param>
        /// <returns>encrypted SRTCP packet.</returns>
        public byte[] Transform(byte[] pkt)
        {
            return Transform(pkt, 0, pkt.Length);
        }

        public byte[] Transform(byte[] pkt, int offset, int length)
        {
            // Wrap the data into raw packet for readable format
            this.packet.Wrap(pkt, offset, length);

            // Associate the packet with its encryption context
            long ssrc = this.packet.GetRTCPSSRC();
            SrtcpCryptoContext context = null;
            contexts.TryGetValue(ssrc, out context);

            if (context == null)
            {
                context = forwardEngine.GetDefaultContextControl().DeriveContext(ssrc);
                context.DeriveSrtcpKeys();
                contexts[ssrc] = context;
            }

            // Secure packet into SRTCP format
            context.TransformPacket(packet);
            return packet.GetData();
        }

        public byte[] ReverseTransform(byte[] pkt)
        {
            return ReverseTransform(pkt, 0, pkt.Length);
        }

        public byte[] ReverseTransform(byte[] pkt, int offset, int length)
        {
            // wrap data into raw packet for readable format
            this.packet.Wrap(pkt, offset, length);

            // Associate the packet with its encryption context
            long ssrc = this.packet.GetRTCPSSRC();
            SrtcpCryptoContext context = null;
            contexts.TryGetValue(ssrc, out context);

            if (context == null)
            {
                context = reverseEngine.GetDefaultContextControl().DeriveContext(ssrc);
                context.DeriveSrtcpKeys();
                contexts[ssrc] = context;
            }

            // Decode packet to RTCP format
            bool reversed = context.ReverseTransformPacket(packet);
            if (reversed)
            {
                return packet.GetData();
            }
            return null;
        }

        /// <summary>
        /// Close the transformer and underlying transform engine.
        /// The close functions closes all stored crypto contexts. This deletes key data
        /// and forces a cleanup of the crypto contexts.
        /// </summary>
        public void Close()
        {
            forwardEngine.Close();
            if (forwardEngine != reverseEngine)
            {
                reverseEngine.Close();
            }

            var keys = new List<long>(contexts.Keys);
            foreach (var ssrc in keys)
            {
                var context = contexts[ssrc];

                contexts.Remove(ssrc);
                if (context != null)
                {
                    context.Close();
                }
            }
        }
    }
}
