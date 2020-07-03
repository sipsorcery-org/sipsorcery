//-----------------------------------------------------------------------------
// Filename: SrtpTransformEngine.cs
//
// Description: SRTPTransformEngine class implements TransformEngine interface. 
// It stores important information / objects regarding SRTP processing.Through
// SRTPTransformEngine, we can get the needed PacketTransformer, which will be
// used by abstract TransformConnector classes.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/SRTPTransformEngine.java
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
    public class SrtpTransformEngine : ITransformEngine
    {
        /**
	    * The default SRTPCryptoContext, which will be used to derivate other
	    * contexts.
	    */
        private SrtpCryptoContext defaultContext;

        /**
         * The default SRTPCryptoContext, which will be used to derive other
         * contexts.
         */
        private SrtcpCryptoContext defaultContextControl;

        /**
         * Construct a SRTPTransformEngine based on given master encryption key,
         * master salt key and SRTP/SRTCP policy.
         * 
         * @param masterKey
         *            the master encryption key
         * @param masterSalt
         *            the master salt key
         * @param srtpPolicy
         *            SRTP policy
         * @param srtcpPolicy
         *            SRTCP policy
         */
        public SrtpTransformEngine(byte[] masterKey, byte[] masterSalt, SrtpPolicy srtpPolicy, SrtpPolicy srtcpPolicy)
        {
            defaultContext = new SrtpCryptoContext(0, 0, 0, masterKey, masterSalt, srtpPolicy);
            defaultContextControl = new SrtcpCryptoContext(0, masterKey, masterSalt, srtcpPolicy);
        }

        /**
         * Close the transformer engine.
         * 
         * The close functions closes all stored default crypto contexts. This
         * deletes key data and forces a cleanup of the crypto contexts.
         */
        public void Close()
        {
            if (defaultContext != null)
            {
                defaultContext.Close();
                defaultContext = null;
            }
            if (defaultContextControl != null)
            {
                defaultContextControl.Close();
                defaultContextControl = null;
            }
        }

        /**
         * Gets the <tt>PacketTransformer</tt> for RTCP packets.
         * 
         * @return the <tt>PacketTransformer</tt> for RTCP packets
         */
        public IPacketTransformer GetRTCPTransformer()
        {
            return new SrtcpTransformer(this);
        }

        /*
         * (non-Javadoc)
         * 
         * @see net.java.sip.communicator.impl.media.transform.
         * TransformEngine#getRTPTransformer()
         */
        public IPacketTransformer GetRTPTransformer()
        {
            return new SrtpTransformer(this);
        }

        /**
         * Get the default SRTPCryptoContext
         * 
         * @return the default SRTPCryptoContext
         */
        public SrtpCryptoContext GetDefaultContext()
        {
            return this.defaultContext;
        }

        /**
         * Get the default SRTPCryptoContext
         * 
         * @return the default SRTPCryptoContext
         */
        public SrtcpCryptoContext GetDefaultContextControl()
        {
            return this.defaultContextControl;
        }
    }
}
