using SIPSorcery.Net;
using SIPSorcery.Net.UnitTests.Helpers;

namespace SdpBenchmarks;

public class BenchmarkParams
{
    public required string Scenario { get; init; }
    public required string SdpText { get; init; }
    public required SDP Sdp { get; init; }

    public override string ToString() => Scenario;

    public static IEnumerable<BenchmarkParams> GetScenarios()
    {
        yield return Create(nameof(SdpFixtures.AudioOnlyOfferPcmu), SdpFixtures.AudioOnlyOfferPcmu);
        yield return Create(nameof(SdpFixtures.AudioOfferPcmuWithDtmf), SdpFixtures.AudioOfferPcmuWithDtmf);
        yield return Create(nameof(SdpFixtures.VideoOnlyOfferVp8), SdpFixtures.VideoOnlyOfferVp8);
        yield return Create(nameof(SdpFixtures.AudioVideoOfferAudioFirst), SdpFixtures.AudioVideoOfferAudioFirst);
        yield return Create(nameof(SdpFixtures.AudioVideoOfferVideoFirst), SdpFixtures.AudioVideoOfferVideoFirst);
        yield return Create(nameof(SdpFixtures.AudioOfferSendOnly), SdpFixtures.AudioOfferSendOnly);
        yield return Create(nameof(SdpFixtures.AudioOfferRecvOnly), SdpFixtures.AudioOfferRecvOnly);
        yield return Create(nameof(SdpFixtures.AudioOfferInactive), SdpFixtures.AudioOfferInactive);
        yield return Create(nameof(SdpFixtures.AudioOfferHoldNullConnectionAddress), SdpFixtures.AudioOfferHoldNullConnectionAddress);
        yield return Create(nameof(SdpFixtures.ReInviteRejectsVideoPortZero), SdpFixtures.ReInviteRejectsVideoPortZero);
        yield return Create(nameof(SdpFixtures.WebRtcAudioOfferOpus), SdpFixtures.WebRtcAudioOfferOpus);
        yield return Create(nameof(SdpFixtures.WebRtcAudioVideoOfferBundled), SdpFixtures.WebRtcAudioVideoOfferBundled);
        yield return Create(nameof(SdpFixtures.AudioOfferWithSdesCrypto), SdpFixtures.AudioOfferWithSdesCrypto);
        yield return Create(nameof(SdpFixtures.ChromeAudioVideoWebRtcOffer), SdpFixtures.ChromeAudioVideoWebRtcOffer);
        yield return Create(nameof(SdpFixtures.FirefoxAudioOnlyWebRtcOffer), SdpFixtures.FirefoxAudioOnlyWebRtcOffer);
        yield return Create(nameof(SdpFixtures.LargeWebRtcOffer), SdpFixtures.LargeWebRtcOffer);
    }

    /// <summary>
    /// Creates a scenario from a fixture with its line endings normalized to CRLF, the SDP line ending (RFC 8866),
    /// so the measured input does not depend on how git checked out the fixtures file (core.autocrlf).
    /// </summary>
    private static BenchmarkParams Create(string scenario, string sdpText)
    {
        var text = sdpText.ReplaceLineEndings("\r\n");

        return new BenchmarkParams { Scenario = scenario, SdpText = text, Sdp = SDP.ParseSDPDescription(text) };
    }
}
