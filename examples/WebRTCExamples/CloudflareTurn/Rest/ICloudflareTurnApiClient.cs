//-----------------------------------------------------------------------------
// Filename: ICloudflareTurnApiClient.cs
//
// Description: Interface for the Cloudflare TURN API.
//
// See: https://developers.cloudflare.com/realtime/turn/generate-credentials/
// To generate the long-tem key see: https://developers.cloudflare.com/realtime/turn/generate-credentials/#create-a-turn-key
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

using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;

namespace SIPSorcery.Examples;

public interface ICloudflareTurnApiClient
{
    Task<Either<Error, CloudflareIceServers>> CreateCredentialsAsync(
        string turnKeyID,
        int secondsToLive,
        CancellationToken ct = default);

    Task<Option<Error>> RevokeCredentialsAsync(
        string turnKeyID,
        string username,
        CancellationToken ct = default);
}
