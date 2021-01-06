//-----------------------------------------------------------------------------
// Filename: SrtpTransformer.cs
//
// Description:  SRTPTransformer implements PacketTransformer and provides 
// implementations for RTP packet to SRTP packet transformation and SRTP 
// packet to RTP packet transformation logic.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/SRTPTransformer.java
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

/**
* 
* Code derived and adapted from the Jitsi client side SRTP framework.
* 
* Distributed under LGPL license.
* See terms of license at gnu.org.
*//**
* SRTPTransformer implements PacketTransformer and provides implementations for
* RTP packet to SRTP packet transformation and SRTP packet to RTP packet
* transformation logic.
* 
* It will first find the corresponding SRTPCryptoContext for each packet based
* on their SSRC and then invoke the context object to perform the
* transformation and reverse transformation operation.
* 
* @author Bing SU (nova.su@gmail.com)
* @author Rafael Soares (raf.csoares@kyubinteractive.com)
* 
*/

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SIPSorcery.Net
{
    public class SrtpTransformer : IPacketTransformer
    {
        private int _isLocked = 0;
        private RawPacket rawPacket;

        private SrtpTransformEngine forwardEngine;
        private SrtpTransformEngine reverseEngine;

        /**
	     * All the known SSRC's corresponding SRTPCryptoContexts
	     */
        private ConcurrentDictionary<long, SrtpCryptoContext> contexts;

        public SrtpTransformer(SrtpTransformEngine engine) : this(engine, engine)
        {
        }

        public SrtpTransformer(SrtpTransformEngine forwardEngine, SrtpTransformEngine reverseEngine)
        {
            this.forwardEngine = forwardEngine;
            this.reverseEngine = reverseEngine;
            this.contexts = new ConcurrentDictionary<long, SrtpCryptoContext>();
            this.rawPacket = new RawPacket();
        }

        public byte[] Transform(byte[] pkt)
        {
            return Transform(pkt, 0, pkt.Length);
        }

        public byte[] Transform(byte[] pkt, int offset, int length)
        {
            var isLocked = Interlocked.CompareExchange(ref _isLocked, 1, 0) != 0;

            try
            {
                // Updates the contents of raw packet with new incoming packet 
                var rawPacket = !isLocked ? this.rawPacket : new RawPacket();
                rawPacket.Wrap(pkt, offset, length);

                // Associate packet to a crypto context
                long ssrc = rawPacket.GetSSRC();
                SrtpCryptoContext context = null;
                contexts.TryGetValue(ssrc, out context);

                if (context == null)
                {
                    context = forwardEngine.GetDefaultContext().deriveContext(ssrc, 0, 0);
                    context.DeriveSrtpKeys(0);
                    contexts.AddOrUpdate(ssrc, context, (a, b) => context);
                }

                // Transform RTP packet into SRTP
                context.TransformPacket(rawPacket);
                byte[] result = rawPacket.GetData();

                return result;
            }
            finally
            {
                //Unlock
                if (!isLocked)
                    Interlocked.CompareExchange(ref _isLocked, 0, 1);
            }
        }

        /**
         * Reverse-transforms a specific packet (i.e. transforms a transformed
         * packet back).
         * 
         * @param pkt
         *            the transformed packet to be restored
         * @return the restored packet
         */
        public byte[] ReverseTransform(byte[] pkt)
        {
            return ReverseTransform(pkt, 0, pkt.Length);
        }

        public byte[] ReverseTransform(byte[] pkt, int offset, int length)
        {
            var isLocked = Interlocked.CompareExchange(ref _isLocked, 1, 0) != 0;
            try
            {
                // Wrap data into the raw packet for readable format
                var rawPacket = !isLocked ? this.rawPacket : new RawPacket();
                rawPacket.Wrap(pkt, offset, length);

                // Associate packet to a crypto context
                long ssrc = rawPacket.GetSSRC();
                SrtpCryptoContext context = null;
                this.contexts.TryGetValue(ssrc, out context);
                if (context == null)
                {
                    context = this.reverseEngine.GetDefaultContext().deriveContext(ssrc, 0, 0);
                    context.DeriveSrtpKeys(rawPacket.GetSequenceNumber());
                    contexts.AddOrUpdate(ssrc, context, (a, b) => context);
                }

                byte[] result = null;
                bool reversed = context.ReverseTransformPacket(rawPacket);
                if (reversed)
                {
                    result = rawPacket.GetData();
                }

                return result;
            }
            finally
            {
                //Unlock
                if (!isLocked)
                    Interlocked.CompareExchange(ref _isLocked, 0, 1);
            }
        }

        /**
         * Close the transformer and underlying transform engine.
         * 
         * The close functions closes all stored crypto contexts. This deletes key
         * data and forces a cleanup of the crypto contexts.
         */
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
                SrtpCryptoContext context;
                contexts.TryRemove(ssrc, out context);
                if (context != null)
                {
                    context.Close();
                }
            }
        }
    }
}