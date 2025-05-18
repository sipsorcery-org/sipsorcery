//-----------------------------------------------------------------------------
// Filename: IOpenAIRealtimeRestClient.cs
//
// Description: Interface for the OpenAI WebRTC peer connection.
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

namespace SIPSorcery.OpenAI.RealtimeWebRTC;

public interface IOpenAIRealtimeRestClient
{
    Task<Either<Error, string>> CreateEphemeralKeyAsync(
        string model = OpenAIRealtimeRestClient.OPENAI_REALTIME_DEFAULT_MODEL,
        OpenAIVoicesEnum voice = OpenAIVoicesEnum.shimmer,
        CancellationToken ct = default);

    Task<Either<Error, string>> GetSdpAnswerAsync(
        string offerSdp,
        string model = OpenAIRealtimeRestClient.OPENAI_REALTIME_DEFAULT_MODEL,
        CancellationToken ct = default);
}
