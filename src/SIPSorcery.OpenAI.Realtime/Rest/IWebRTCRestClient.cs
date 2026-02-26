//-----------------------------------------------------------------------------
// Filename: IWebRTCRestClient.cs
//
// Description: Interface for the OpenAI WebRTC REST client.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.OpenAI.Realtime.Models;

namespace SIPSorcery.OpenAI.Realtime;

public interface IWebRTCRestClient
{
    Task<Either<Error, string>> CreateEphemeralKeyAsync(
        RealtimeVoicesEnum voice = RealtimeVoicesEnum.shimmer,
        RealtimeModelsEnum? model = null,
        CancellationToken ct = default);

    Task<Either<Error, string>> GetSdpAnswerAsync(
        string offerSdp,
        RealtimeModelsEnum? model,
        CancellationToken ct = default);
}
