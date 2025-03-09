//-----------------------------------------------------------------------------
// Filename: ConfigKeys.cs
// 
// Description: ASP.NET application configuration keys
// 
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace demo;

public class ConfigKeys
{
    public const string LND_URL = "Lnd:Url";

    public const string LND_MACAROON_PATH = "Lnd:MacaroonPath";

    public const string LND_MACAROON_HEX = "Lnd:MacaroonHex";

    public const string LND_CERTIFICATE_PATH = "Lnd:CertificatePath";

    public const string LND_CERTIFICATE_BASE64 = "Lnd:CertificateBase64";
}
