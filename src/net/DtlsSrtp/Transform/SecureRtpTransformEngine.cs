//-----------------------------------------------------------------------------
// Filename: SecureRtpTransformEngine.cs
//
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public class SecureRtpTransformEngine : IPacketTransformEngine, IDisposable
    {
        // The default SRTP encryption context, used to derive other contexts.
        private SrtpCryptoContext _defaultSrtpContext;

        // The default SRTCP encryption context, used to derive other contexts.
        private SrtcpCryptoContext _defaultSrtcpContext;

        /// <summary>
        /// Constructs a SecureRtpTransformEngine based on the given master encryption key,
        /// master salt key, and SRTP/SRTCP policies.
        /// </summary>
        /// <param name="masterKey">The master encryption key.</param>
        /// <param name="masterSalt">The master salt key.</param>
        /// <param name="srtpPolicy">SRTP policy.</param>
        /// <param name="srtcpPolicy">SRTCP policy.</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the parameters are null or empty.</exception>
        public SecureRtpTransformEngine(byte[] masterKey, byte[] masterSalt, SrtpPolicy srtpPolicy, SrtpPolicy srtcpPolicy)
        {
            _defaultSrtpContext = new SrtpCryptoContext(0, 0, 0, masterKey ?? throw new ArgumentNullException(nameof(masterKey)), masterSalt ?? throw new ArgumentNullException(nameof(masterSalt)), srtpPolicy ?? throw new ArgumentNullException(nameof(srtpPolicy)));
            _defaultSrtcpContext = new SrtcpCryptoContext(0, masterKey, masterSalt, srtcpPolicy ?? throw new ArgumentNullException(nameof(srtcpPolicy)));
        }

        /// <summary>
        /// Closes the transformer engine and cleans up resources.
        /// This deletes key data and forces a cleanup of the crypto contexts.
        /// </summary>
        public void Dispose()
        {
            CloseContexts();
            GC.SuppressFinalize(this);
        }

        public void CloseContexts()
        {
            _defaultSrtpContext?.Close();
            _defaultSrtpContext = null;

            _defaultSrtcpContext?.Close();
            _defaultSrtcpContext = null;
        }

        /// <summary>
        /// Gets the transformer for RTCP packets.
        /// </summary>
        /// <returns>The transformer for RTCP packets.</returns>
        public IDataPacketTransformer CreateRtcpPacketTransformer()
        {
            return new SrtcpPacketTransformer(this);
        }

        /// <summary>
        /// Gets the transformer for RTP packets.
        /// </summary>
        /// <returns>The transformer for RTP packets.</returns>
        public IDataPacketTransformer CreateRtpPacketTransformer()
        {
            return new SrtpPacketTransformer(this);
        }

        /// <summary>
        /// Gets the default SRTP encryption context.
        /// </summary>
        /// <returns>The default SRTP encryption context.</returns>
        public SrtpCryptoContext GetDefaultSrtpContext()
        {
            return _defaultSrtpContext;
        }

        /// <summary>
        /// Gets the default SRTCP encryption context.
        /// </summary>
        /// <returns>The default SRTCP encryption context.</returns>
        public SrtcpCryptoContext GetDefaultSrtcpContext()
        {
            return _defaultSrtcpContext;
        }
    }

}
