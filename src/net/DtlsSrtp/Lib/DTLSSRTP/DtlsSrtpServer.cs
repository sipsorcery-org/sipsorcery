// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using SIPSorcery.Net.SharpSRTP.DTLS;
using SIPSorcery.Net.SharpSRTP.SRTP;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.Net.SharpSRTP.DTLSSRTP
{
    // Useful link for troubleshooting WebRTC in Chrome/Edge: https://learn.microsoft.com/en-us/azure/communication-services/resources/troubleshooting/voice-video-calling/references/how-to-collect-browser-verbose-log
    public class DtlsSrtpServer : DtlsServer, IDtlsSrtpPeer
    {
        /// <summary>
        /// Used in WebRTC to tell the server to not use MKI even if the client requested it.
        /// </summary>
        /// <remarks>
        /// RFC 8827 states: An SRTP Master Key Identifier (MKI) MUST NOT be used.
        /// </remarks>
        public bool ForceDisableMKI { get; set; } = false;

        private UseSrtpData _srtpData;

        public event EventHandler<DtlsSessionStartedEventArgs> OnSessionStarted;

        public DtlsSrtpServer(Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256) 
            : this(new BcTlsCrypto(), certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm)
        { }

        public DtlsSrtpServer(TlsCrypto crypto, Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256) 
            : base(crypto, certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm)
        {
            this.OnHandshakeCompleted += DtlsSrtpServer_OnHandshakeCompleted;
        }

        private void DtlsSrtpServer_OnHandshakeCompleted(object sender, DtlsHandshakeCompletedEventArgs e)
        {
            SrtpSessionContext context = CreateSessionContext(e.SecurityParameters);
            Certificate peerCertificate = e.SecurityParameters.PeerCertificate;
            OnSessionStarted?.Invoke(this, new DtlsSessionStartedEventArgs(context, peerCertificate, base._clientDatagramTransport));
        }

        protected virtual int[] GetSupportedProtectionProfiles()
        {
            return new int[] 
            {
                ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM,
                ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM,

                ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_256_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_128_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32,
                
                ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_256_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_128_GCM,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_32,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_32,

                ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32,
            };
        }

        protected override string GetCertificateCommonName()
        {
            return "WebRTC";
        }

        public override void ProcessClientExtensions(IDictionary<int, byte[]> clientExtensions)
        {
            base.ProcessClientExtensions(clientExtensions);

            UseSrtpData clientSrtpExtension = TlsSrtpUtilities.GetUseSrtpExtension(clientExtensions);

            int[] serverSupportedProfiles = GetSupportedProtectionProfiles();
            int[] mutuallySupportedProfiles = clientSrtpExtension.ProtectionProfiles.Where(x => serverSupportedProfiles.Contains(x)).ToArray();
            if (mutuallySupportedProfiles.Length == 0)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            int selectedProfile = mutuallySupportedProfiles.OrderBy(x => Array.IndexOf(serverSupportedProfiles, x)).First(); // Choose the highest priority profile supported by the server
            _srtpData = new UseSrtpData(new int[] { selectedProfile }, ForceDisableMKI ? new byte[0] : clientSrtpExtension.Mki); // Server must return only a single selected profile
        }

        public override IDictionary<int, byte[]> GetServerExtensions()
        {
            var extensions = base.GetServerExtensions();
            TlsSrtpUtilities.AddUseSrtpExtension(extensions, _srtpData);
            return extensions;
        }

        public virtual SrtpSessionContext CreateSessionContext(SecurityParameters securityParameters)
        {
            // this should only be called from OnHandshakeCompleted so we should still have _srtpData from the connection
            if (m_context == null)
            {
                throw new InvalidOperationException();
            }

            int selectedProtectionProfile = _srtpData.ProtectionProfiles[0];
            DtlsSrtpKeys keys = DtlsSrtpProtocol.CreateMasterKeys(_srtpData.ProtectionProfiles[0], _srtpData.Mki, securityParameters, ForceUseExtendedMasterSecret);
            return DtlsSrtpProtocol.CreateSrtpServerSessionContext(keys);
        }
    }
}
