using System;
using System.Collections.Generic;
using System.Linq;
using SharpSRTP.SRTP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Net
{
    public class SrtpHandler
    {
        private List<SDPSecurityDescription> _localSecurityDescriptions;
        private List<SDPSecurityDescription> _remoteSecurityDescriptions;

        public SrtpSessionContext Context { get; private set; }

        public bool IsNegotiationComplete { get; private set; }
        public SDPSecurityDescription LocalSecurityDescription { get; private set; }
        public SDPSecurityDescription RemoteSecurityDescription { get; private set; }

        public SrtpHandler()
        { }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.EncodeRtpContext.ProtectRtp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.DecodeRtpContext.UnprotectRtp(payload, length, out outputBufferLength);
        }

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.EncodeRtcpContext.ProtectRtcp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.DecodeRtcpContext.UnprotectRtcp(payload, length, out outputBufferLength);
        }

        public bool RemoteSecurityDescriptionUnchanged(List<SDPSecurityDescription> securityDescriptions)
        {
            if (LocalSecurityDescription == null || RemoteSecurityDescription == null)
            {
                return false;
            }

            var remoteCryptoSuite = securityDescriptions.FirstOrDefault(x => x.CryptoSuite == LocalSecurityDescription.CryptoSuite);
            return remoteCryptoSuite.ToString() == RemoteSecurityDescription.ToString();
        }

        public bool SetupLocal(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
        {
            _localSecurityDescriptions = securityDescriptions;

            if (sdpType == SdpType.offer)
            {
                IsNegotiationComplete = false;
                return true;
            }

            if (_remoteSecurityDescriptions.Count == 0)
            {
                throw new ApplicationException("Setup local crypto failed. No cryto attribute in offer.");
            }

            if (_localSecurityDescriptions.Count == 0)
            {
                throw new ApplicationException("Setup local crypto failed. No crypto attribute in answer.");
            }

            var localSecurityDescription = LocalSecurityDescription = _localSecurityDescriptions.First();
            var remoteSecurityDescription = RemoteSecurityDescription = _remoteSecurityDescriptions.FirstOrDefault(x => x.CryptoSuite == localSecurityDescription.CryptoSuite);

            if (remoteSecurityDescription != null && remoteSecurityDescription.Tag == localSecurityDescription.Tag)
            {
                IsNegotiationComplete = true;

                Context = CreateSessionContext(localSecurityDescription, remoteSecurityDescription);

                return true;
            }

            return false;
        }

        public bool SetupRemote(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
        {
            _remoteSecurityDescriptions = securityDescriptions;

            if (sdpType == SdpType.offer)
            {
                IsNegotiationComplete = false;
                return true;
            }

            if (_localSecurityDescriptions.Count == 0)
            {
                throw new ApplicationException("Setup remote crypto failed. No cryto attribute in offer.");
            }

            if (_remoteSecurityDescriptions.Count == 0)
            {
                throw new ApplicationException("Setup remote crypto failed. No cryto attribute in answer.");
            }

            var remoteSecurityDescription = RemoteSecurityDescription = _remoteSecurityDescriptions.First();
            var localSecurityDescription = LocalSecurityDescription = _localSecurityDescriptions.FirstOrDefault(x => x.CryptoSuite == remoteSecurityDescription.CryptoSuite);

            if (localSecurityDescription != null && localSecurityDescription.Tag == remoteSecurityDescription.Tag)
            {
                IsNegotiationComplete = true;

                Context = CreateSessionContext(localSecurityDescription, remoteSecurityDescription);

                return true;
            }

            return false;
        }

        private SrtpSessionContext CreateSessionContext(SDPSecurityDescription localSecurityDescription, SDPSecurityDescription remoteSecurityDescription, byte[] mki = null)
        {
            // TODO: not tested
            var securityDescription = localSecurityDescription;
            int protectionProfile = (int)securityDescription.CryptoSuite;

            var keys = new SrtpKeys(protectionProfile, mki);
            Buffer.BlockCopy(localSecurityDescription.KeyParams[0].Key, 0, keys.ClientWriteMasterKey, 0, keys.ClientWriteMasterKey.Length);
            Buffer.BlockCopy(localSecurityDescription.KeyParams[0].Salt, 0, keys.ClientWriteMasterSalt, 0, keys.ClientWriteMasterSalt.Length);
            Buffer.BlockCopy(remoteSecurityDescription.KeyParams[0].Key, 0, keys.ServerWriteMasterKey, 0, keys.ServerWriteMasterKey.Length);
            Buffer.BlockCopy(remoteSecurityDescription.KeyParams[0].Salt, 0, keys.ServerWriteMasterSalt, 0, keys.ServerWriteMasterSalt.Length);

            var encodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTP);
            var encodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ClientWriteMasterKey, keys.ClientWriteMasterSalt, SrtpContextType.RTCP);
            var decodeRtpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTP);
            var decodeRtcpContext = new SrtpContext(keys.ProtectionProfile, keys.Mki, keys.ServerWriteMasterKey, keys.ServerWriteMasterSalt, SrtpContextType.RTCP);

            return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
        }
    }
}
