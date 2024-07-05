using System;
using Org.BouncyCastle.Crypto.Tls;

namespace SIPSorcery.Net
{
    public interface IDtlsSrtpPeer
    {
        event Action<AlertLevels, AlertTypes, string> OnAlert;
        bool ForceUseExtendedMasterSecret { get; set; }
        SrtpPolicy SrtpPolicy { get; }
        SrtpPolicy SrtcpPolicy { get; }
        byte[] SrtpMasterServerKey { get; }
        byte[] SrtpMasterServerSalt { get; }
        byte[] SrtpMasterClientKey { get; }
        byte[] SrtpMasterClientSalt { get; }
        bool IsClient { get; }
        Certificate RemoteCertificate { get; }
    }
}
