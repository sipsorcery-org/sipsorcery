

using System.Collections.Generic;
/**
* SRTCPTransformer implements PacketTransformer.
* It encapsulate the encryption / decryption logic for SRTCP packets
* 
* @author Bing SU (nova.su@gmail.com)
* @author Werner Dittmann <Werner.Dittmann@t-online.de>
*/
namespace Org.BouncyCastle.Crypto.DtlsSrtp
{
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

        /**
         * Encrypts a SRTCP packet
         * 
         * @param pkt plain SRTCP packet to be encrypted
         * @return encrypted SRTCP packet
         */
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

        /**
         * Close the transformer and underlying transform engine.
         * 
         * The close functions closes all stored crypto contexts. This deletes key data 
         * and forces a cleanup of the crypto contexts.
         */
        public void Close()
        {
            forwardEngine.Close();
            if (forwardEngine != reverseEngine)
                reverseEngine.Close();

            var keys = new List<long>(contexts.Keys);
            foreach (var ssrc in keys)
            {
                var context = contexts[ssrc];

                contexts.Remove(ssrc);
                if (context != null)
                    context.Close();
            }
        }
    }
}
