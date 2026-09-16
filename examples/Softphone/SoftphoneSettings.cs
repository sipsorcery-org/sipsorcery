//-----------------------------------------------------------------------------
// Filename: SoftphoneSettings.cs
//
// Description: Strongly typed view of the application's settings as loaded from
// the appsettings.json file (use the optional appsettings.Development.json to override).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Sep 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using SIPSorcery.SIP;

namespace SIPSorcery.SoftPhone
{
    /// <summary>
    /// The settings for the softphone. These are bound from the "Softphone" section of
    /// the appsettings.json file.
    /// </summary>
    public class SoftphoneSettings
    {
        public const string SectionName = "Softphone";

        /// <summary>
        /// Optional, the username for your SIP account.
        /// </summary>
        public string SIPUsername { get; set; }

        /// <summary>
        /// Optional, the password for your SIP account.
        /// </summary>
        public string SIPPassword { get; set; }

        /// <summary>
        /// Optional, the host of your SIP server.
        /// </summary>
        public string SIPServer { get; set; }

        /// <summary>
        /// Optional, the name you would like to appear as the display name on your SIP calls.
        /// </summary>
        public string SIPFromName { get; set; }

        /// <summary>
        /// Optional, the hostname of a public STUN server used to determine this machine's
        /// public IP address. If it can't be determined there will almost certainly be audio
        /// issues on some calls.
        /// </summary>
        public string STUNServerHostname { get; set; }

        public bool UseAudioScope { get; set; }

        /// <summary>
        /// The index of the audio output device to use. Defaults to -1 which means use the
        /// system default device.
        /// </summary>
        public int AudioOutDeviceIndex { get; set; } = -1;

        /// <summary>
        /// If set to true video streams will not be offered or accepted in SIP calls.
        /// </summary>
        public bool DisableVideo { get; set; }

        /// <summary>
        /// Optional, used to configure the SIP channels used by the SIP transport layer. If
        /// left empty a single UDP channel on the default SIP port will be used.
        /// </summary>
        public List<SIPSocketSettings> SIPSockets { get; set; } = new List<SIPSocketSettings>();
    }

    /// <summary>
    /// Describes a single SIP channel for the SIP transport layer to listen on.
    /// </summary>
    public class SIPSocketSettings
    {
        /// <summary>
        /// The local end point to listen on, e.g. "192.168.11.50:7060". If the port is left
        /// off the default port for the protocol is used.
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// The SIP protocol for the channel. Defaults to udp.
        /// </summary>
        public SIPProtocolsEnum Protocol { get; set; } = SIPProtocolsEnum.udp;

        /// <summary>
        /// Required for a tls channel, the path to the certificate's pkcs12 (.pfx) file.
        /// </summary>
        public string CertificatePath { get; set; }

        /// <summary>
        /// Optional, the password for the tls channel's certificate key.
        /// </summary>
        public string CertificateKeyPassword { get; set; }
    }
}
