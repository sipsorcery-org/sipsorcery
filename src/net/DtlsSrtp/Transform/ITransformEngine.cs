namespace Org.BouncyCastle.Crypto.DtlsSrtp
{
    public interface ITransformEngine
    {
        /**
         * Gets the <tt>PacketTransformer</tt> for RTP packets.
         *
         * @return the <tt>PacketTransformer</tt> for RTP packets
         */
       IPacketTransformer GetRTPTransformer();

        /**
         * Gets the <tt>PacketTransformer</tt> for RTCP packets.
         *
         * @return the <tt>PacketTransformer</tt> for RTCP packets
         */
        IPacketTransformer GetRTCPTransformer();
    }
}
