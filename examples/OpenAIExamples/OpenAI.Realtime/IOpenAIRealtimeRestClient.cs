
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;

namespace demo;

public interface IOpenAIRealtimeRestClient
{
    Task<Either<Error, string>> CreateEphemeralKeyAsync(
        string model,
        OpenAIVoicesEnum voice,
        CancellationToken ct = default);

    Task<Either<Error, string>> GetSdpAnswerAsync(
        string model,
        string offerSdp,
        CancellationToken ct = default);
}
