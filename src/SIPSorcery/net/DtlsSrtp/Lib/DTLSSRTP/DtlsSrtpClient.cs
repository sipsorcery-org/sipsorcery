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
    public class DtlsSrtpClient : DtlsClient, IDtlsSrtpPeer
    {
        private UseSrtpData _srtpData;

        public event EventHandler<DtlsSessionStartedEventArgs> OnSessionStarted;
        public int MkiLength { get; private set; } = 0;

        public DtlsSrtpClient(Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256, TlsSession session = null) :
           this(new BcTlsCrypto(), certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm, session)
        { }

        public DtlsSrtpClient(TlsCrypto crypto, Certificate certificate = null, AsymmetricKeyParameter privateKey = null, short certificateSignatureAlgorithm = SignatureAlgorithm.ecdsa, short certificateHashAlgorithm = HashAlgorithm.sha256, TlsSession session = null) : 
            base(crypto, session, certificate, privateKey, certificateSignatureAlgorithm, certificateHashAlgorithm)
        {
            int[] protectionProfiles = GetSupportedProtectionProfiles();
            
            byte[] mki = DtlsSrtpProtocol.GenerateMki(MkiLength);
            this._srtpData = new UseSrtpData(protectionProfiles, mki);

            this.OnHandshakeCompleted += DtlsSrtpClient_OnHandshakeCompleted;
        }

        private void DtlsSrtpClient_OnHandshakeCompleted(object sender, DtlsHandshakeCompletedEventArgs e)
        {
            SrtpSessionContext context = CreateSessionContext(e.SecurityParameters);
            Certificate peerCertificate = e.SecurityParameters.PeerCertificate;
            OnSessionStarted?.Invoke(this, new DtlsSessionStartedEventArgs(context, peerCertificate, base._clientDatagramTransport));
        }
       
        public void SetMKI(byte[] mki)
        {
            if(mki == null)
            {
                MkiLength = 0;
                mki = new byte[0];
            }
            else
            {
                if (mki.Length > 255)
                {
                    throw new ArgumentOutOfRangeException(nameof(mki));
                }

                MkiLength = mki.Length;
            }

            this._srtpData = new UseSrtpData(_srtpData.ProtectionProfiles, mki);
        }

        protected virtual int[] GetSupportedProtectionProfiles()
        {
            return new int[] 
            {
                ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_256_GCM_AEAD_AES_256_GCM,
                ExtendedSrtpProtectionProfile.DOUBLE_AEAD_AES_128_GCM_AEAD_AES_128_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_256_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_256_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_AES_128_GCM,
                ExtendedSrtpProtectionProfile.SRTP_AEAD_ARIA_128_GCM,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_80,
                ExtendedSrtpProtectionProfile.SRTP_AES128_CM_HMAC_SHA1_32,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_256_CTR_HMAC_SHA1_32,
                ExtendedSrtpProtectionProfile.SRTP_ARIA_128_CTR_HMAC_SHA1_32,

                // do not offer NULL profiles to make sure these do not get selected by accident
                //ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_80,
                //ExtendedSrtpProtectionProfile.SRTP_NULL_HMAC_SHA1_32
            };
        }

        protected override string GetCertificateCommonName()
        {
            return "WebRTC";
        }

        public override void ProcessServerExtensions(IDictionary<int, byte[]> serverExtensions)
        {
            base.ProcessServerExtensions(serverExtensions);

            // https://www.rfc-editor.org/rfc/rfc5764#section-4.1
            UseSrtpData serverSrtpExtension = TlsSrtpUtilities.GetUseSrtpExtension(serverExtensions);

            // verify that the server has selected exactly 1 profile
            int[] clientSupportedProfiles = GetSupportedProtectionProfiles();
            if (serverSrtpExtension.ProtectionProfiles.Length != 1)
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            // verify that the server has selected a profile we support
            int selectedProfile = serverSrtpExtension.ProtectionProfiles[0];
            if (!clientSupportedProfiles.Contains(selectedProfile))
            {
                throw new TlsFatalAlert(AlertDescription.internal_error);
            }

            // verify the mki sent by the server matches our mki
            if (_srtpData.Mki != null && serverSrtpExtension.Mki != null && !Enumerable.SequenceEqual(_srtpData.Mki, serverSrtpExtension.Mki))
            {
                throw new TlsFatalAlert(AlertDescription.illegal_parameter);
            }

            // store the server extension as it contains the selected profile
            _srtpData = serverSrtpExtension;
        }

        public override IDictionary<int, byte[]> GetClientExtensions()
        {
            var extensions = base.GetClientExtensions();
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
            DtlsSrtpKeys keys = DtlsSrtpProtocol.CreateMasterKeys(selectedProtectionProfile, _srtpData.Mki, securityParameters, ForceUseExtendedMasterSecret);
            return DtlsSrtpProtocol.CreateSrtpClientSessionContext(keys);
        }
    }
}
