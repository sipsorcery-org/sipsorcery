//-----------------------------------------------------------------------------
// Filename: SrtpHandler.cs
//
// Description: This class represents the SRTP handling for SIP calls
//
// Author(s):
// Kurt Kießling 
//
// History:
// 20 Jul 2021	Kurt Kießling	Created.
// 30 Dec 2025  Lukas Volf      New DTLS/SRTP impl
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

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
            return Context.ProtectRtp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.UnprotectRtp(payload, length, out outputBufferLength);
        }

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.ProtectRtcp(payload, length, out outputBufferLength);
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            return Context.UnprotectRtcp(payload, length, out outputBufferLength);
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
            var localProtectionProfile = SrtpProtocol.SrtpCryptoSuites[localSecurityDescription.CryptoSuite.ToString()];
            var remoteProtectionProfile = SrtpProtocol.SrtpCryptoSuites[remoteSecurityDescription.CryptoSuite.ToString()];

            var encodeRtpContext = new SrtpContext(SrtpContextType.RTP, localProtectionProfile, localSecurityDescription.KeyParams[0].Key, localSecurityDescription.KeyParams[0].Salt, mki);
            var encodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, localProtectionProfile, localSecurityDescription.KeyParams[0].Key, localSecurityDescription.KeyParams[0].Salt, mki);
            var decodeRtpContext = new SrtpContext(SrtpContextType.RTP, remoteProtectionProfile, remoteSecurityDescription.KeyParams[0].Key, remoteSecurityDescription.KeyParams[0].Salt, mki);
            var decodeRtcpContext = new SrtpContext(SrtpContextType.RTCP, remoteProtectionProfile, remoteSecurityDescription.KeyParams[0].Key, remoteSecurityDescription.KeyParams[0].Salt, mki);

            return new SrtpSessionContext(encodeRtpContext, decodeRtpContext, encodeRtcpContext, decodeRtcpContext);
        }
    }
}
