//-----------------------------------------------------------------------------
// Filename: IPacketTransformer.cs
//
// Description: Encapsulate the concept of packet transformation. Given a packet,
// PacketTransformer can either transform it or reverse the
// transformation.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/PacketTransformer.java
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

namespace SIPSorcery.Net
{
    public interface IPacketTransformer
    {
        /**
         * Transforms a non-secure packet.
         * 
         * @param pkt
         *            the packet to be transformed
         * @return The transformed packet. Returns null if the packet cannot be transformed.
         */
        byte[] Transform(byte[] pkt);

        /**
         * Transforms a specific non-secure packet.
         * 
         * @param pkt
         *            The packet to be secured
         * @param offset
         *            The offset of the packet data
         * @param length
         *            The length of the packet data
         * @return The transformed packet. Returns null if the packet cannot be
         *         transformed.
         */
        byte[] Transform(byte[] pkt, int offset, int length);

        /**
         * Reverse-transforms a specific packet (i.e. transforms a transformed
         * packet back).
         * 
         * @param pkt
         *            the transformed packet to be restored
         * @return Whether the packet was successfully restored
         */
        byte[] ReverseTransform(byte[] pkt);

        /**
         * Reverse-transforms a specific packet (i.e. transforms a transformed
         * packet back).
         * 
         * @param pkt
         *            the packet to be restored
         * @param offset
         *            the offset of the packet data
         * @param length
         *            the length of data in the packet
         * @return The restored packet. Returns null if packet cannot be restored.
         */
        byte[] ReverseTransform(byte[] pkt, int offset, int length);

        /**
         * Close the transformer and underlying transform engine.
         * 
         * The close functions closes all stored crypto contexts. This deletes key
         * data and forces a cleanup of the crypto contexts.
         */
        void Close();
    }
}