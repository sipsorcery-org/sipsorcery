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
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOnlyOfferPcmu), SdpText = SdpFixtures.AudioOnlyOfferPcmu, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferPcmuWithDtmf), SdpText = SdpFixtures.AudioOfferPcmuWithDtmf, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferPcmuWithDtmf) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.VideoOnlyOfferVp8), SdpText = SdpFixtures.VideoOnlyOfferVp8, Sdp = SDP.ParseSDPDescription(SdpFixtures.VideoOnlyOfferVp8) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioVideoOfferAudioFirst), SdpText = SdpFixtures.AudioVideoOfferAudioFirst, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioVideoOfferVideoFirst), SdpText = SdpFixtures.AudioVideoOfferVideoFirst, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferVideoFirst) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferSendOnly), SdpText  = SdpFixtures.AudioOfferSendOnly, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferSendOnly) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferRecvOnly), SdpText = SdpFixtures.AudioOfferRecvOnly, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferRecvOnly) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferInactive), SdpText = SdpFixtures.AudioOfferInactive, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferInactive) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferHoldNullConnectionAddress), SdpText = SdpFixtures.AudioOfferHoldNullConnectionAddress, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.ReInviteRejectsVideoPortZero), SdpText = SdpFixtures.ReInviteRejectsVideoPortZero, Sdp = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.WebRtcAudioOfferOpus), SdpText = SdpFixtures.WebRtcAudioOfferOpus, Sdp = SDP.ParseSDPDescription(SdpFixtures.WebRtcAudioOfferOpus) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.WebRtcAudioVideoOfferBundled), SdpText = SdpFixtures.WebRtcAudioVideoOfferBundled, Sdp = SDP.ParseSDPDescription(SdpFixtures.WebRtcAudioVideoOfferBundled) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.AudioOfferWithSdesCrypto), SdpText = SdpFixtures.AudioOfferWithSdesCrypto, Sdp = SDP.ParseSDPDescription(SdpFixtures.AudioOfferWithSdesCrypto) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.ChromeAudioVideoWebRtcOffer), SdpText = SdpFixtures.ChromeAudioVideoWebRtcOffer, Sdp = SDP.ParseSDPDescription(SdpFixtures.ChromeAudioVideoWebRtcOffer) };
        yield return new BenchmarkParams { Scenario = nameof(SdpFixtures.FirefoxAudioOnlyWebRtcOffer), SdpText = SdpFixtures.FirefoxAudioOnlyWebRtcOffer, Sdp = SDP.ParseSDPDescription(SdpFixtures.FirefoxAudioOnlyWebRtcOffer) };
    }
}
