//-----------------------------------------------------------------------------
// Filename: IDataPacketTransformer.cs
//
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
    public interface IDataPacketTransformer
    {
        /// <summary>
        /// Encodes a standard packet.
        /// </summary>
        /// <param name="packet">The packet to encode.</param>
        /// <returns>The encoded packet, or null if the packet cannot be encoded.</returns>
        byte[] EncodePacket(byte[] packet);

        /// <summary>
        /// Encodes a specific segment of a packet.
        /// </summary>
        /// <param name="packet">The packet to encode.</param>
        /// <param name="startIndex">The starting index of the packet data.</param>
        /// <param name="count">The length of the packet data.</param>
        /// <returns>The encoded packet, or null if the packet cannot be encoded.</returns>
        byte[] EncodePacket(byte[] packet, int startIndex, int count);

        /// <summary>
        /// Decodes an encoded packet back to its original form.
        /// </summary>
        /// <param name="packet">The encoded packet to decode.</param>
        /// <returns>The decoded packet, or null if the packet cannot be decoded.</returns>
        byte[] DecodePacket(byte[] packet);

        /// <summary>
        /// Decodes a specific segment of an encoded packet back to its original form.
        /// </summary>
        /// <param name="packet">The encoded packet to decode.</param>
        /// <param name="startIndex">The starting index of the packet data.</param>
        /// <param name="count">The length of data in the packet.</param>
        /// <returns>The decoded packet, or null if the packet cannot be decoded.</returns>
        byte[] DecodePacket(byte[] packet, int startIndex, int count);

        /// <summary>
        /// Shuts down the transformer and releases all resources.
        /// </summary>
        /// <remarks>
        /// This method clears all stored cryptographic contexts, deleting key data and performing necessary cleanup.
        /// </remarks>
        void Shutdown();
    }
}
