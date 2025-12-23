using System;
using System.Collections.Generic;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Net
{
    public class SrtpHandler
    {
        public bool IsNegotiationComplete { get; internal set; }
        public SDPSecurityDescription LocalSecurityDescription { get; internal set; }

        public int ProtectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            throw new NotImplementedException();
        }

        public int ProtectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            throw new NotImplementedException();
        }

        public bool RemoteSecurityDescriptionUnchanged(List<SDPSecurityDescription> securityDescriptions)
        {
            throw new NotImplementedException();
        }

        public void SetupLocal(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
        {
            throw new NotImplementedException();
        }

        public bool SetupRemote(List<SDPSecurityDescription> securityDescriptions, SdpType sdpType)
        {
            throw new NotImplementedException();
        }

        public int UnprotectRTCP(byte[] payload, int length, out int outputBufferLength)
        {
            throw new NotImplementedException();
        }

        public int UnprotectRTP(byte[] payload, int length, out int outputBufferLength)
        {
            throw new NotImplementedException();
        }
    }
}
