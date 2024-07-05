//-----------------------------------------------------------------------------
// Filename: ITransformEngine.cs
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
    public interface IPacketTransformEngine
    {
        /**
         * Retrieves the transformer for RTP (Real-time Transport Protocol) packets.
         *
         * @return the transformer for RTP packets
         */
        IDataPacketTransformer CreateRtpPacketTransformer();

        /**
         * Retrieves the transformer for RTCP (Real-time Transport Control Protocol) packets.
         *
         * @return the transformer for RTCP packets
         */
        IDataPacketTransformer CreateRtcpPacketTransformer();
    }
}
