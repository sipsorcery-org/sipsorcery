//-----------------------------------------------------------------------------
// Filename: CloudflareIceServers.cs
//
// Description: Model for the Cloudflare ICE servers which are returned when calling
// the TURN credentials creation API.
//
// See: https://developers.cloudflare.com/realtime/turn/generate-credentials/#create-credentials
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 02 Jun 2026  Aaron Clauson   Created, Wexford, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;

namespace SIPSorcery.Examples;

public class CloudflareIceServer
{
    public string Username { get; set; } = string.Empty;

    public string Credential { get; set; } = string.Empty;

    public List<string> Urls { get; set; } = [];
}

public class CloudflareIceServers
{
    public List<CloudflareIceServer> IceServers { get; set; } = [];
}
