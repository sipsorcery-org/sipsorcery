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
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SrtpHandler: IDisposable
    {
        private static readonly ILogger logger = Log.Logger;

        public List<SDPSecurityDescription> m_localSecurityDescriptions;
        public List<SDPSecurityDescription> m_remoteSecurityDescriptions;

        public IPacketTransformer SrtpDecoder { get; private set; }
        public IPacketTransformer SrtpEncoder { get; private set; }
        public IPacketTransformer SrtcpDecoder { get; private set; }
        public IPacketTransformer SrtcpEncoder { get; private set; }

        public bool IsNegotiationComplete { get; private set; } = false;


        public SrtpHandler()
        {
        }

        public bool SetupLocal(List<SDPSecurityDescription> securityDescription, SdpType sdpType)
        {
            m_localSecurityDescriptions = securityDescription;

            if (sdpType == SdpType.offer)
            {
               IsNegotiationComplete = false;
               return true;
            }

            if (m_remoteSecurityDescriptions.Count==0)
            {
               throw new ApplicationException("Setup local crypto failed. No cryto attribute in offer.");
            }

            if (m_localSecurityDescriptions.Count==0)
            {
                throw new ApplicationException("Setup local crypto failed. No crypto attribute in answer.");
            }

            var lsec = m_localSecurityDescriptions[0];
            var rsec = m_remoteSecurityDescriptions.FirstOrDefault(x => x.CryptoSuite == lsec.CryptoSuite);

            if (rsec != null && rsec.Tag == lsec.Tag)
            {
               IsNegotiationComplete = true;
               SrtpEncoder = GenerateRtpEncoder(lsec);
               SrtpDecoder = GenerateRtpDecoder(rsec);
               SrtcpEncoder = GenerateRtcpEncoder(lsec);
               SrtcpDecoder = GenerateRtcpDecoder(rsec);
               return true;
            }

            return false;
        }

        public bool SetupRemote(List<SDPSecurityDescription> securityDescription, SdpType sdpType)
        {
            m_remoteSecurityDescriptions = securityDescription;

            if (sdpType == SdpType.offer)
            {
                IsNegotiationComplete = false;
                return true;
            }

            if (m_localSecurityDescriptions.Count==0)
            {
                throw new ApplicationException("Setup remote crypto failed. No cryto attribute in offer.");
            }

            if (m_remoteSecurityDescriptions.Count==0)
            {
                throw new ApplicationException("Setup remote crypto failed. No cryto attribute in answer.");
            }

            var rsec = m_remoteSecurityDescriptions[0];
            var lsec = m_localSecurityDescriptions.FirstOrDefault(x => x.CryptoSuite == rsec.CryptoSuite);

            if (lsec != null && lsec.Tag == rsec.Tag)
            {
                IsNegotiationComplete = true;
                SrtpEncoder = GenerateRtpEncoder(lsec);
                SrtpDecoder = GenerateRtpDecoder(rsec);
                SrtcpEncoder = GenerateRtcpEncoder(lsec);
                SrtcpDecoder = GenerateRtcpDecoder(rsec);
                return true;
            }
            
            return false;
        }

        protected IPacketTransformer GenerateRtpEncoder(SDPSecurityDescription securityDescription)
        {
            return GenerateTransformer(securityDescription, true);
        }

        protected IPacketTransformer GenerateRtpDecoder(SDPSecurityDescription securityDescription)
        {
            return GenerateTransformer(securityDescription, true);
        }

        protected IPacketTransformer GenerateRtcpEncoder(SDPSecurityDescription securityDescription)
        {
            return GenerateTransformer(securityDescription, false);
        }

        protected IPacketTransformer GenerateRtcpDecoder(SDPSecurityDescription securityDescription)
        {
            return GenerateTransformer(securityDescription, false);
        }

        protected IPacketTransformer GenerateTransformer(SDPSecurityDescription securityDescription, bool isRtp)
        {
            var srtpParams = SrtpParameters.GetSrtpParametersForProfile((int)securityDescription.CryptoSuite);

            var engine = new SrtpTransformEngine(securityDescription.KeyParams[0].Key,
                                                 securityDescription.KeyParams[0].Salt,
                                                 srtpParams.GetSrtpPolicy(), 
                                                 srtpParams.GetSrtcpPolicy() );

            if (isRtp)
            {
                return engine.GetRTPTransformer();
            }
            else
            {
                return engine.GetRTCPTransformer();
            }
        }

        public byte[] UnprotectRTP(byte[] packet, int offset, int length)
        {
            lock (SrtpDecoder)
            {
                return SrtpDecoder.ReverseTransform(packet, offset, length);
            }
        }

        public int UnprotectRTP(byte[] payload, int length, out int outLength)
        {
            var result = UnprotectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTP(byte[] packet, int offset, int length)
        {
            lock (SrtpEncoder)
            {
                return SrtpEncoder.Transform(packet, offset, length);
            }
        }

        public int ProtectRTP(byte[] payload, int length, out int outLength)
        {
            var result = ProtectRTP(payload, 0, length);

            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] UnprotectRTCP(byte[] packet, int offset, int length)
        {
            lock (SrtcpDecoder)
            {
                return SrtcpDecoder.ReverseTransform(packet, offset, length);
            }
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outLength)
        {
            var result = UnprotectRTCP(payload, 0, length);
            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        public byte[] ProtectRTCP(byte[] packet, int offset, int length)
        {
            lock (SrtcpEncoder)
            {
                return SrtcpEncoder.Transform(packet, offset, length);
            }
        }

        public int ProtectRTCP(byte[] payload, int length, out int outLength)
        {
            var result = ProtectRTCP(payload, 0, length);
            if (result == null)
            {
                outLength = 0;
                return -1;
            }

            System.Buffer.BlockCopy(result, 0, payload, 0, result.Length);
            outLength = result.Length;

            return 0; //No Errors
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
        }
    }
}