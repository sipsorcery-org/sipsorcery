
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;

namespace demo;

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
