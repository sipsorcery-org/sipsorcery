namespace SIPSorcery.Net
{
    public class SecureContext
    {
        public SecureContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            this.ProtectRtpPacket = protectRtp;
            this.UnprotectRtpPacket = unprotectRtp;
            this.ProtectRtcpPacket = protectRtcp;
            this.UnprotectRtcpPacket = unprotectRtcp;
        }

        public ProtectRtpPacket ProtectRtpPacket { get; internal set; }
        public ProtectRtpPacket ProtectRtcpPacket { get; internal set; }

        public ProtectRtpPacket UnprotectRtpPacket { get; internal set; }
        public ProtectRtpPacket UnprotectRtcpPacket { get; internal set; }
    }
}
